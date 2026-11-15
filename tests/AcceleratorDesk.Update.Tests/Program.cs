using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Reflection;
using System.Reflection.Emit;
using AcceleratorDesk.Update;

var passed = 0; var failed = 0;
async Task Test(string name, Func<string, Task> action)
{
    var directory = Path.Combine(Path.GetTempPath(), "AcceleratorDeskUpdate-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
    try { await action(directory); passed++; Console.WriteLine("PASS UPDATE " + name); }
    catch (Exception error) { failed++; Console.WriteLine("FAIL UPDATE " + name + ": " + error.Message); }
}
void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}; got {actual}"); }
async Task Reject(Func<Task> action)
{
    try { await action(); } catch (Exception error) when (error is ArgumentException or InvalidDataException or HttpRequestException or OperationCanceledException) { return; }
    throw new Exception("Unsafe or invalid update was accepted.");
}
UpdateManifest Manifest(byte[] archive, string version = "0.3.4") => new() { Version = version, ArchiveName = $"AcceleratorAccountDesk-{version}-win-x64.zip", ArchiveSize = archive.Length, Sha256 = Convert.ToHexString(SHA256.HashData(archive)) };
var channel = new UpdateChannel("example-owner/accelerator-desk");
Uri Asset(string name) => new("https://github.com/example-owner/accelerator-desk/releases/download/v0.3.4/" + name);
string Release(UpdateManifest manifest, string? manifestUrl = null, string? zipUrl = null, bool prerelease = false) => JsonSerializer.Serialize(new {
    tag_name = "v" + manifest.Version, draft = false, prerelease, body = "独立测试更新说明",
    assets = new[] { new { name = "update-manifest.json", browser_download_url = manifestUrl ?? Asset("update-manifest.json").AbsoluteUri, size = 1000L }, new { name = manifest.ArchiveName, browser_download_url = zipUrl ?? Asset(manifest.ArchiveName).AbsoluteUri, size = manifest.ArchiveSize } }
});
HttpClient Http(Func<HttpRequestMessage, HttpResponseMessage> respond) => new(new FixtureHandler(respond));
HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
byte[] ManagedAssembly(string version)
{
    var builder = new PersistedAssemblyBuilder(new AssemblyName("AcceleratorAccountDesk") { Version = UpdateVersion.Parse(version) }, typeof(object).Assembly);
    builder.DefineDynamicModule("main"); using var memory = new MemoryStream(); builder.Save(memory); return memory.ToArray();
}
byte[] Package(string? extra = null, bool duplicate = false, bool link = false)
{
    using var memory = new MemoryStream();
    using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
    {
        foreach (var name in new[] { "加速账户助手.exe", "AcceleratorAccountDesk.dll", "AcceleratorAccountDesk.runtimeconfig.json", "AcceleratorDesk.Updater.exe", "AcceleratorDesk.Updater.dll", "AcceleratorDesk.Updater.deps.json", "AcceleratorDesk.Updater.runtimeconfig.json", "AcceleratorDesk.Update.dll", "coreclr.dll", "e_sqlite3.dll", "WebView2Loader.dll" })
        { using var stream = zip.CreateEntry(name).Open(); stream.Write(name == "AcceleratorAccountDesk.dll" ? ManagedAssembly("0.3.4") : Encoding.UTF8.GetBytes("isolated non-executable fixture")); }
        if (extra is not null)
        {
            var entry = zip.CreateEntry(extra); if (link) entry.ExternalAttributes = 0xA000 << 16;
            using var stream = entry.Open(); stream.WriteByte(1);
        }
        if (duplicate) { using var stream = zip.CreateEntry("CORECLR.DLL").Open(); stream.WriteByte(2); }
    }
    return memory.ToArray();
}
string Current(string directory)
{
    var current = Path.Combine(directory, "current"); Directory.CreateDirectory(current);
    File.WriteAllBytes(Path.Combine(current, "AcceleratorAccountDesk.dll"), ManagedAssembly("0.3.3"));
    File.WriteAllText(Path.Combine(current, "existing.txt"), "keep"); return current;
}

await Test("stable version comparison prevents same-version downgrade and rejects preview tags", async _ =>
{
    Equal(true, UpdateVersion.Parse("0.3.10") > UpdateVersion.Parse("0.3.9"));
    Equal(new Version(0, 3, 3, 0), UpdateVersion.Parse("v0.3.3"));
    foreach (var value in new[] { "0.3.3-beta", "../0.3.3", "0.3", "v1.2.3.4", "" }) await Reject(() => { UpdateVersion.Parse(value); return Task.CompletedTask; });
});
await Test("channel accepts a GitHub repository and rejects URL injection", async _ =>
{
    channel.Validate();
    foreach (var value in new[] { "https://github.com/owner/repo", "owner/repo/extra", "owner/..", "owner/repo?x=1", "", "owner\\repo" }) await Reject(() => { new UpdateChannel(value).Validate(); return Task.CompletedTask; });
});
await Test("missing channel configuration gives a setup error without creating files", async directory =>
{
    await Reject(() => { UpdateChannel.Load(directory); return Task.CompletedTask; }); Equal(0, Directory.GetFiles(directory).Length);
});
await Test("handoff preserves custom Unicode data paths and uses only its own cache", directory =>
{
    var data = Path.Combine(directory, "独立数据 带空格");
    var jobPath = Path.Combine(data, "updates", "job-" + Guid.NewGuid().ToString("N"), "job.json");
    var job = new UpdateInstallJob(Current(directory), data, Path.Combine(data, "updates", "downloads", "test.zip"), Manifest([1]), channel.Repository, 123, 456);
    UpdateHandoff.WriteJson(jobPath, job); var loaded = UpdateHandoff.ReadJob(jobPath);
    Equal(job, loaded); Equal(true, UpdateHandoff.SamePath(data, data + Path.DirectorySeparatorChar));
    Equal(Path.Combine(directory, "加速账户助手-0.3.4"), UpdateHandoff.TargetDirectory(loaded));
    return Task.CompletedTask;
});
await Test("handoff rejects missing metadata and jobs or archives outside its cache", async directory =>
{
    var data = Path.Combine(directory, "data");
    var jobPath = Path.Combine(data, "updates", "job-" + Guid.NewGuid().ToString("N"), "job.json");
    var valid = new UpdateInstallJob(Current(directory), data, Path.Combine(data, "updates", "downloads", "test.zip"), Manifest([1]), channel.Repository, 123, 456);
    foreach (var job in new[] { valid with { ArchivePath = Path.Combine(directory, "untrusted.zip") }, valid with { ParentProcessId = 0 }, valid with { DataDirectory = "relative" }, valid with { Manifest = null! } })
    { UpdateHandoff.WriteJson(jobPath, job); await Reject(() => { UpdateHandoff.ReadJob(jobPath); return Task.CompletedTask; }); }
    var foreign = Path.Combine(directory, "job.json"); UpdateHandoff.WriteJson(foreign, valid);
    await Reject(() => { UpdateHandoff.ReadJob(foreign); return Task.CompletedTask; });
});
await Test("official latest release and manifest produce a newer matching update", async _ =>
{
    var manifest = Manifest([1, 2, 3]);
    using var http = Http(request => request.RequestUri!.Host == "api.github.com" ? Json(Release(manifest)) : Json(JsonSerializer.Serialize(manifest)));
    var update = await new UpdateClient(http).CheckAsync(channel, new Version(0, 3, 3, 0));
    Equal("0.3.4", update!.Manifest.Version); Equal(Asset(manifest.ArchiveName), update.DownloadUri); Equal("独立测试更新说明", update.Notes);
});
await Test("GitHub anonymous API limits fall back to the official latest manifest", async _ =>
{
    var manifest = Manifest([1, 2, 3]);
    foreach (var status in new[] { HttpStatusCode.Forbidden, HttpStatusCode.TooManyRequests })
    {
        var requests = new List<Uri>();
        using var http = Http(request =>
        {
            requests.Add(request.RequestUri!);
            return request.RequestUri!.Host == "api.github.com" ? new(status) : Json(JsonSerializer.Serialize(manifest));
        });
        var update = await new UpdateClient(http).CheckAsync(channel, new Version(0, 3, 3, 0));
        Equal("0.3.4", update!.Manifest.Version); Equal(Asset(manifest.ArchiveName), update.DownloadUri);
        Equal("https://github.com/example-owner/accelerator-desk/releases/latest/download/update-manifest.json", requests[1].AbsoluteUri);
    }
});
await Test("fallback manifest retains version integrity and repository redirect guards", async _ =>
{
    var valid = Manifest([1, 2, 3]);
    using var current = Http(request => request.RequestUri!.Host == "api.github.com" ? new(HttpStatusCode.Forbidden) : Json(JsonSerializer.Serialize(valid)));
    Equal<AvailableUpdate?>(null, await new UpdateClient(current).CheckAsync(channel, new Version(0, 3, 4, 0)));
    using var invalid = Http(request => request.RequestUri!.Host == "api.github.com" ? new(HttpStatusCode.Forbidden) : Json(JsonSerializer.Serialize(valid with { ProductId = "OtherApp" })));
    await Reject(async () => await new UpdateClient(invalid).CheckAsync(channel, new Version(0, 3, 3, 0)));
    var requests = 0;
    using var foreign = Http(request =>
    {
        requests++;
        if (request.RequestUri!.Host == "api.github.com") return new(HttpStatusCode.Forbidden);
        var response = new HttpResponseMessage(HttpStatusCode.Redirect); response.Headers.Location = new Uri("https://github.com/other/repo/releases/download/v0.3.4/update-manifest.json"); return response;
    });
    await Reject(async () => await new UpdateClient(foreign).CheckAsync(channel, new Version(0, 3, 3, 0))); Equal(2, requests);
});
await Test("equal older and prerelease releases never offer an install", async _ =>
{
    var manifest = Manifest([1, 2, 3]);
    foreach (var current in new[] { new Version(0, 3, 4, 0), new Version(0, 4, 0, 0) })
    {
        using var http = Http(_ => Json(Release(manifest))); Equal<AvailableUpdate?>(null, await new UpdateClient(http).CheckAsync(channel, current));
    }
    using var preview = Http(_ => Json(Release(manifest, prerelease: true))); Equal<AvailableUpdate?>(null, await new UpdateClient(preview).CheckAsync(channel, new Version(0, 3, 3, 0)));
});
await Test("a release cannot point manifests or archives at a foreign host or repository", async _ =>
{
    var manifest = Manifest([1, 2, 3]);
    foreach (var url in new[] { "https://evil.example/update.zip", "https://github.com/other/repo/releases/download/v0.3.4/update.zip", "http://github.com/example-owner/accelerator-desk/releases/download/v0.3.4/update.zip" })
    {
        using var http = Http(request => request.RequestUri!.Host == "api.github.com" ? Json(Release(manifest, zipUrl: url)) : Json(JsonSerializer.Serialize(manifest))); await Reject(async () => await new UpdateClient(http).CheckAsync(channel, new Version(0, 3, 3, 0)));
        using var foreignManifest = Http(_ => Json(Release(manifest, manifestUrl: url))); await Reject(async () => await new UpdateClient(foreignManifest).CheckAsync(channel, new Version(0, 3, 3, 0)));
    }
});
await Test("malformed metadata and noncanonical manifest versions are rejected clearly", async _ =>
{
    using var malformed = Http(_ => Json("{"));
    await Reject(async () => await new UpdateClient(malformed).CheckAsync(channel, new Version(0, 3, 3, 0)));
    foreach (var value in new[] { "v0.3.4", "00.3.4", "0.03.4" })
        await Reject(() => { Manifest([1], value).Validate(); return Task.CompletedTask; });
    var valid = Manifest([1]);
    using var tagMismatch = Http(request => request.RequestUri!.Host == "api.github.com" ? Json(Release(valid).Replace("\"v0.3.4\"", "\"0.3.4\"")) : Json(JsonSerializer.Serialize(valid)));
    await Reject(async () => await new UpdateClient(tagMismatch).CheckAsync(channel, new Version(0, 3, 3, 0)));
});
await Test("GitHub CDN redirects can download a matching package", async directory =>
{
    byte[] bytes = [4, 5, 6]; var manifest = Manifest(bytes); var requests = 0;
    using var http = Http(request =>
    {
        requests++;
        if (request.RequestUri!.Host == "github.com")
        { var redirect = new HttpResponseMessage(HttpStatusCode.Redirect); redirect.Headers.Location = new Uri("https://release-assets.githubusercontent.com/test/package?signature=test"); return redirect; }
        return new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
    });
    var path = await new UpdateClient(http).DownloadAsync(new(channel, manifest, Asset(manifest.ArchiveName), ""), directory);
    Equal(2, requests); Equal(true, bytes.SequenceEqual(File.ReadAllBytes(path)));
});
await Test("manifest product runtime version file name size and hash must match", async _ =>
{
    var valid = Manifest([1, 2, 3]);
    foreach (var manifest in new[] { valid with { ProductId = "OtherApp" }, valid with { RuntimeId = "linux-x64" }, valid with { Version = "0.3.5" }, valid with { ArchiveName = "other.zip" }, valid with { ArchiveSize = 0 }, valid with { Sha256 = "bad" } })
    {
        using var http = Http(request => request.RequestUri!.Host == "api.github.com" ? Json(Release(valid)) : Json(JsonSerializer.Serialize(manifest)));
        await Reject(async () => await new UpdateClient(http).CheckAsync(channel, new Version(0, 3, 3, 0)));
    }
});
await Test("foreign and insecure redirects are rejected before another request", async _ =>
{
    var requests = 0;
    using var http = Http(_ => { requests++; var response = new HttpResponseMessage(HttpStatusCode.Redirect); response.Headers.Location = new Uri("http://evil.example/update"); return response; });
    await Reject(async () => await new UpdateClient(http).CheckAsync(channel, new Version(0, 3, 3, 0))); Equal(1, requests);
});
await Test("verified download reports completion and returns only the matching bytes", async directory =>
{
    byte[] bytes = [11, 22, 33, 44]; var manifest = Manifest(bytes); var progress = new ImmediateProgress();
    using var http = Http(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
    var path = await new UpdateClient(http).DownloadAsync(new(channel, manifest, Asset(manifest.ArchiveName), ""), directory, progress);
    Equal(true, bytes.SequenceEqual(File.ReadAllBytes(path))); Equal(100d, progress.Last!.Percent); Equal(1, Directory.GetFiles(directory, "*", SearchOption.AllDirectories).Length);
});
await Test("wrong hash short oversized and cancelled downloads leave no executable update", async directory =>
{
    byte[] expected = [11, 22, 33, 44]; var manifest = Manifest(expected);
    foreach (var body in new byte[][] { [1, 2, 3, 4], [11], [11, 22, 33, 44, 55] })
    {
        using var http = Http(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
        await Reject(async () => await new UpdateClient(http).DownloadAsync(new(channel, manifest, Asset(manifest.ArchiveName), ""), directory));
    }
    using var cancelledHttp = Http(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(expected) }); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
    await Reject(async () => await new UpdateClient(cancelledHttp).DownloadAsync(new(channel, manifest, Asset(manifest.ArchiveName), ""), directory, cancellationToken: cancellation.Token));
    Equal(0, Directory.GetFiles(directory, "*", SearchOption.AllDirectories).Length);
});
await Test("verified package installs beside the old version and preserves data and channel", directory =>
{
    var bytes = Package(); var archive = Path.Combine(directory, "fixture.zip"); File.WriteAllBytes(archive, bytes); var current = Current(directory);
    File.WriteAllText(Path.Combine(current, "accounts.db"), "private user fixture");
    var target = UpdatePackage.Install(archive, Manifest(bytes), current, channel.Repository);
    Equal(Path.Combine(directory, "加速账户助手-0.3.4"), target); Equal(true, File.Exists(Path.Combine(target, "加速账户助手.exe")));
    Equal("keep", File.ReadAllText(Path.Combine(current, "existing.txt"))); Equal("private user fixture", File.ReadAllText(Path.Combine(current, "accounts.db")));
    Equal(false, File.Exists(Path.Combine(target, "accounts.db"))); Equal(channel.Repository, UpdateChannel.Load(target).Repository);
    Equal(target, UpdatePackage.Install(archive, Manifest(bytes), current, channel.Repository));
    return Task.CompletedTask;
});
await Test("traversal duplicate paths links and user-data files cannot be installed", async directory =>
{
    foreach (var entry in new[] { "../outside.txt", "C:/outside.txt", "folder\\..\\outside.txt", "settings.json", "accounts.db", "qq-browser/Cookies", "folder/CON.txt", "name. " })
    {
        var scenario = Path.Combine(directory, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(scenario);
        var bytes = Package(entry); var archive = Path.Combine(scenario, "fixture.zip"); File.WriteAllBytes(archive, bytes);
        var current = Current(scenario);
        await Reject(() => { UpdatePackage.Install(archive, Manifest(bytes), current, channel.Repository); return Task.CompletedTask; });
        Equal("keep", File.ReadAllText(Path.Combine(current, "existing.txt"))); Equal(false, File.Exists(Path.Combine(scenario, "outside.txt")));
    }
    foreach (var bytes in new[] { Package(duplicate: true), Package("linked.txt", link: true) })
    {
        var scenario = Path.Combine(directory, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(scenario); var archive = Path.Combine(scenario, "fixture.zip"); File.WriteAllBytes(archive, bytes);
        await Reject(() => { UpdatePackage.Install(archive, Manifest(bytes), Current(scenario), channel.Repository); return Task.CompletedTask; });
    }
});
await Test("missing app files and occupied target directory preserve the current installation", async directory =>
{
    using var memory = new MemoryStream(); using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true)) { using var writer = new StreamWriter(zip.CreateEntry("readme.txt").Open()); writer.Write("fixture"); }
    var bytes = memory.ToArray(); var archive = Path.Combine(directory, "fixture.zip"); File.WriteAllBytes(archive, bytes);
    var current = Current(directory);
    await Reject(() => { UpdatePackage.Install(archive, Manifest(bytes), current, channel.Repository); return Task.CompletedTask; });
    var target = Path.Combine(directory, "加速账户助手-0.3.4"); Directory.CreateDirectory(target); File.WriteAllText(Path.Combine(target, "foreign.txt"), "do not overwrite");
    bytes = Package(); File.WriteAllBytes(archive, bytes);
    await Reject(() => { UpdatePackage.Install(archive, Manifest(bytes), current, channel.Repository); return Task.CompletedTask; });
    Equal("keep", File.ReadAllText(Path.Combine(current, "existing.txt"))); Equal("do not overwrite", File.ReadAllText(Path.Combine(target, "foreign.txt")));
});
await Test("packages cannot downgrade or declare another managed app version", async directory =>
{
    var bytes = Package(); var archive = Path.Combine(directory, "fixture.zip"); File.WriteAllBytes(archive, bytes); var current = Current(directory);
    foreach (var version in new[] { "0.3.2", "0.3.3", "0.3.5" })
        await Reject(() => { UpdatePackage.Install(archive, Manifest(bytes, version), current, channel.Repository); return Task.CompletedTask; });
    Equal("keep", File.ReadAllText(Path.Combine(current, "existing.txt"))); Equal(false, Directory.Exists(Path.Combine(directory, "加速账户助手-0.3.5")));
});
Console.WriteLine($"RESULT UPDATE {passed}/{passed + failed} passed; {failed} failed");
return failed == 0 ? 0 : 1;

sealed class FixtureHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(respond(request)); }
}
sealed class ImmediateProgress : IProgress<DownloadProgress>
{
    public DownloadProgress? Last { get; private set; }
    public void Report(DownloadProgress value) => Last = value;
}
