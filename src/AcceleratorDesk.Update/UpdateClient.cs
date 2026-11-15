using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

namespace AcceleratorDesk.Update;

public sealed class UpdateClient(HttpClient client)
{
    private readonly HttpClient _client = client;
    public async Task<AvailableUpdate?> CheckAsync(UpdateChannel channel, Version currentVersion, CancellationToken cancellationToken = default)
    {
        channel.Validate();
        HttpResponseMessage latest;
        try { latest = await GetAsync(new Uri($"https://api.github.com/repos/{channel.Repository}/releases/latest"), cancellationToken); }
        catch (HttpRequestException) { return await CheckManifestAsync(channel, currentVersion, cancellationToken); }
        using var response = latest;
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
            return await CheckManifestAsync(channel, currentVersion, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) throw new InvalidDataException("这个 GitHub 仓库还没有可用的正式发布，稍后可重新检查。");
        response.EnsureSuccessStatusCode();
        try
        {
            using var release = JsonDocument.Parse(await ReadLimitedAsync(response.Content, 1048576, cancellationToken));
            var root = release.RootElement;
            if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean()) return null;
            var tag = root.GetProperty("tag_name").GetString() ?? "";
            var version = UpdateVersion.Parse(tag);
            var current = new Version(currentVersion.Major, currentVersion.Minor, Math.Max(0, currentVersion.Build), Math.Max(0, currentVersion.Revision));
            if (version <= current) return null;
            var assets = root.GetProperty("assets").EnumerateArray().ToList();
            var manifests = assets.Where(x => x.GetProperty("name").GetString() == "update-manifest.json").ToList();
            if (manifests.Count != 1) throw new InvalidDataException("这个发布缺少唯一的更新清单，请联系发布者。");
            var manifestUri = ReleaseAsset(channel, tag, manifests[0].GetProperty("browser_download_url").GetString()!);
            using var manifestResponse = await GetAsync(manifestUri, cancellationToken, channel); manifestResponse.EnsureSuccessStatusCode();
            var manifest = JsonSerializer.Deserialize<UpdateManifest>(await ReadLimitedAsync(manifestResponse.Content, 65536, cancellationToken)) ?? throw new InvalidDataException("更新清单为空。");
            manifest.Validate();
            if (UpdateVersion.Parse(manifest.Version) != version || tag != "v" + manifest.Version) throw new InvalidDataException("发布版本与清单版本不一致。");
            var archives = assets.Where(x => x.GetProperty("name").GetString() == manifest.ArchiveName).ToList();
            if (archives.Count != 1 || archives[0].GetProperty("size").GetInt64() != manifest.ArchiveSize) throw new InvalidDataException("发布的更新包缺失或大小与清单不一致。");
            var archiveUri = ReleaseAsset(channel, tag, archives[0].GetProperty("browser_download_url").GetString()!);
            return new(channel, manifest, archiveUri, root.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "");
        }
        catch (Exception error) when (error is KeyNotFoundException or InvalidOperationException or FormatException or JsonException)
        { throw new InvalidDataException("GitHub 发布资料格式无效，请稍后重试或联系发布者。"); }
    }
    private async Task<AvailableUpdate?> CheckManifestAsync(UpdateChannel channel, Version currentVersion, CancellationToken cancellationToken)
    {
        using var response = await GetAsync(new Uri($"https://github.com/{channel.Repository}/releases/latest/download/update-manifest.json"), cancellationToken, channel);
        if (response.StatusCode == HttpStatusCode.NotFound) throw new InvalidDataException("这个 GitHub 仓库还没有可用的正式发布，稍后可重新检查。");
        response.EnsureSuccessStatusCode();
        try
        {
            var manifest = JsonSerializer.Deserialize<UpdateManifest>(await ReadLimitedAsync(response.Content, 65536, cancellationToken)) ?? throw new InvalidDataException("更新清单为空。");
            manifest.Validate();
            var current = new Version(currentVersion.Major, currentVersion.Minor, Math.Max(0, currentVersion.Build), Math.Max(0, currentVersion.Revision));
            if (UpdateVersion.Parse(manifest.Version) <= current) return null;
            var archive = ReleaseAsset(channel, "v" + manifest.Version, $"https://github.com/{channel.Repository}/releases/download/v{manifest.Version}/{manifest.ArchiveName}");
            return new(channel, manifest, archive, "从 GitHub 正式发布检测到新版。\n下载后会核对安装包的大小、完整性与程序版本。\n\n完整更新说明：\nhttps://github.com/" + channel.Repository + "/releases/tag/v" + manifest.Version);
        }
        catch (JsonException) { throw new InvalidDataException("GitHub 更新清单无法读取，请稍后重试。"); }
    }
    public async Task<string> DownloadAsync(AvailableUpdate update, string cacheDirectory, IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        update.Channel.Validate(); update.Manifest.Validate();
        ReleaseAsset(update.Channel, "v" + update.Manifest.Version, update.DownloadUri.AbsoluteUri);
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(cacheDirectory);
        var path = Path.Combine(cacheDirectory, Guid.NewGuid().ToString("N") + ".zip"); var partial = path + ".partial";
        try
        {
            using var response = await GetAsync(update.DownloadUri, cancellationToken, update.Channel); response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is { } length && length != update.Manifest.ArchiveSize) throw new InvalidDataException("更新包大小与清单不一致，未安装。");
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long received = 0;
            await using (var target = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, FileOptions.Asynchronous))
            {
                var buffer = new byte[131072]; int count;
                while ((count = await source.ReadAsync(buffer, cancellationToken)) != 0)
                {
                    received += count; if (received > update.Manifest.ArchiveSize) throw new InvalidDataException("更新包超过声明大小，未安装。");
                    hash.AppendData(buffer, 0, count); await target.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                    progress?.Report(new(received, update.Manifest.ArchiveSize));
                }
                await target.FlushAsync(cancellationToken);
            }
            if (received != update.Manifest.ArchiveSize || !CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(), Convert.FromHexString(update.Manifest.Sha256)))
                throw new InvalidDataException("更新包完整性校验失败，未安装。请重新下载。");
            File.Move(partial, path); progress?.Report(new(received, received, true)); return path;
        }
        finally { if (File.Exists(partial)) File.Delete(partial); }
    }

    private static Uri ReleaseAsset(UpdateChannel channel, string tag, string value)
    {
        var prefix = $"/{channel.Repository}/releases/download/{tag}/";
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || !Secure(uri) || uri.Host != "github.com"
            || !uri.AbsolutePath.StartsWith(prefix, StringComparison.Ordinal) || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new InvalidDataException("更新文件地址不属于配置的 GitHub 发布。");
        return uri;
    }
    private async Task<HttpResponseMessage> GetAsync(Uri uri, CancellationToken cancellationToken, UpdateChannel? channel = null)
    {
        for (var redirects = 0; redirects < 6; redirects++)
        {
            if (!Secure(uri) || uri.Host is not ("api.github.com" or "github.com" or "release-assets.githubusercontent.com" or "objects.githubusercontent.com"))
                throw new InvalidDataException("更新请求被重定向到不允许的地址。");
            if (channel is not null && uri.Host == "github.com" && !uri.AbsolutePath.StartsWith($"/{channel.Repository}/releases/", StringComparison.Ordinal))
                throw new InvalidDataException("更新请求离开了配置的 GitHub 发布仓库。");
            using var request = new HttpRequestMessage(HttpMethod.Get, uri); request.Headers.UserAgent.ParseAdd("AcceleratorAccountDesk-Updater/1.0");
            var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
            {
                var location = response.Headers.Location; response.Dispose();
                if (location is null) throw new InvalidDataException("更新服务器返回了无效跳转。");
                uri = location.IsAbsoluteUri ? location : new Uri(uri, location); continue;
            }
            if (response.RequestMessage?.RequestUri is { } final && (!Secure(final) || final.Host != uri.Host))
            { response.Dispose(); throw new InvalidDataException("更新连接使用了不允许的自动跳转。"); }
            return response;
        }
        throw new InvalidDataException("更新服务器跳转次数过多。");
    }
    private static bool Secure(Uri uri) => uri.Scheme == Uri.UriSchemeHttps && uri.Port == 443 && uri.UserInfo.Length == 0;
    private static async Task<byte[]> ReadLimitedAsync(HttpContent content, int maximum, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > maximum) throw new InvalidDataException("更新资料体积异常。");
        await using var source = await content.ReadAsStreamAsync(cancellationToken); using var target = new MemoryStream(); var buffer = new byte[8192]; int count;
        while ((count = await source.ReadAsync(buffer, cancellationToken)) != 0)
        { if (target.Length + count > maximum) throw new InvalidDataException("更新资料体积异常。"); target.Write(buffer, 0, count); }
        return target.ToArray();
    }
}
