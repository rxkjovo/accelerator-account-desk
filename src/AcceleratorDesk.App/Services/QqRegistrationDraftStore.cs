using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AcceleratorDesk.App.Services;

public sealed record QqRegistrationDraft
{
    public Guid AccountId { get; init; } = Guid.NewGuid();
    public Guid PhoneId { get; init; }
    public string PhoneNumber { get; init; } = "";
    public string Nickname { get; init; } = "";
    public string Password { get; init; } = "";
    public bool SubmissionAttempted { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public override string ToString() => "QQ 注册待确认草稿（敏感信息已隐藏）";
}

/// <summary>Persists one pending registration separately from confirmed accounts, with no plaintext disk writes.</summary>
public sealed class QqRegistrationDraftStore
{
    private const int MaximumFileBytes = 262_144;
    private static readonly byte[] Entropy = SHA256.HashData(Encoding.UTF8.GetBytes("AcceleratorDesk.QqRegistrationDraft.v1"));
    private readonly string _dataDirectory;
    private readonly string _draftPath;

    public QqRegistrationDraftStore(string dataDirectory)
    {
        if (string.IsNullOrWhiteSpace(dataDirectory)) throw new ArgumentException("请选择有效的草稿保存目录。", nameof(dataDirectory));
        _dataDirectory = Path.GetFullPath(dataDirectory);
        _draftPath = Path.Combine(_dataDirectory, "qq-registration-draft.bin");
    }

    public QqRegistrationDraft? Load()
    {
        byte[]? protectedBytes = null;
        byte[]? plaintext = null;
        try
        {
            using (var stream = new FileStream(_draftPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (stream.Length is < 1 or > MaximumFileBytes) throw new InvalidDataException();
                protectedBytes = new byte[(int)stream.Length]; stream.ReadExactly(protectedBytes);
            }
            plaintext = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
            var draft = JsonSerializer.Deserialize<QqRegistrationDraft>(plaintext) ?? throw new InvalidDataException();
            Validate(draft); return draft;
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { return null; }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or CryptographicException or JsonException or ArgumentException)
        { throw new InvalidOperationException("无法读取注册草稿。草稿可能损坏、被占用或属于其他 Windows 用户；原文件已保留，请恢复原用户环境或明确清除草稿后再试。"); }
        finally
        {
            if (plaintext is not null) CryptographicOperations.ZeroMemory(plaintext);
            if (protectedBytes is not null) CryptographicOperations.ZeroMemory(protectedBytes);
        }
    }

    public void Save(QqRegistrationDraft draft)
    {
        Validate(draft);
        // An unreadable existing draft must remain recoverable instead of being replaced by a new attempt.
        _ = Load();
        byte[]? plaintext = null;
        byte[]? protectedBytes = null;
        string? temporaryPath = null;
        try
        {
            plaintext = JsonSerializer.SerializeToUtf8Bytes(draft);
            if (plaintext.Length > MaximumFileBytes / 2) throw new ArgumentException("草稿内容过长，请缩短昵称或密码后重试。", nameof(draft));
            protectedBytes = ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.CurrentUser);
            if (protectedBytes.Length > MaximumFileBytes) throw new InvalidDataException();
            Directory.CreateDirectory(_dataDirectory);
            temporaryPath = _draftPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            { stream.Write(protectedBytes); stream.Flush(flushToDisk: true); }
            File.Move(temporaryPath, _draftPath, overwrite: true);
            temporaryPath = null;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or CryptographicException)
        { throw new InvalidOperationException("无法加密或保存注册草稿。请检查当前 Windows 用户、目录权限和剩余空间；已有草稿已保留。"); }
        finally
        {
            if (plaintext is not null) CryptographicOperations.ZeroMemory(plaintext);
            if (protectedBytes is not null) CryptographicOperations.ZeroMemory(protectedBytes);
            if (temporaryPath is not null)
            {
                try { File.Delete(temporaryPath); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { /* Any residue is encrypted. */ }
            }
        }
    }

    public void Clear()
    {
        try { File.Delete(_draftPath); }
        catch (DirectoryNotFoundException) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { throw new InvalidOperationException("无法清除注册草稿，请关闭占用文件的程序并检查目录权限后重试。"); }
    }

    private static void Validate(QqRegistrationDraft draft)
    {
        if (draft is null || draft.AccountId == Guid.Empty || draft.PhoneId == Guid.Empty
            || string.IsNullOrWhiteSpace(draft.PhoneNumber) || string.IsNullOrWhiteSpace(draft.Nickname) || string.IsNullOrEmpty(draft.Password))
            throw new ArgumentException("注册草稿必须包含有效的账号标识、手机号标识、手机号、昵称和密码。", nameof(draft));
    }
}
