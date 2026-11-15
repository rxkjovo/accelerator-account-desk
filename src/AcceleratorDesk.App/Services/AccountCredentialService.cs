using System.Security.Cryptography;
using System.Text;
using AcceleratorDesk.Core;

namespace AcceleratorDesk.App.Services;

/// <summary>Only this Windows boundary receives password text; the repository receives opaque DPAPI data.</summary>
public sealed class AccountCredentialService(DeskRepository repository)
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private readonly DeskRepository _repository = repository ?? throw new ArgumentNullException(nameof(repository));

    public void Save(AccountRecord account, string? password)
    {
        if (account is null) throw new ArgumentException("请提供要保存密码的账号。", nameof(account));
        if (password is null) { _repository.SaveAccount(account); return; }
        if (password.Length == 0) throw new ArgumentException("密码不能为空；不保存密码时请留用原有密码设置。", nameof(password));
        byte[]? plaintext = null;
        byte[]? protectedPassword = null;
        try
        {
            plaintext = Utf8.GetBytes(password);
            protectedPassword = ProtectedData.Protect(plaintext, account.Id.ToByteArray(), DataProtectionScope.CurrentUser);
            _repository.SaveAccount(account, protectedPassword);
        }
        catch (EncoderFallbackException)
        { throw new ArgumentException("密码包含无效的 Unicode 字符，请重新输入。", nameof(password)); }
        catch (CryptographicException)
        { throw new InvalidOperationException("无法使用当前 Windows 用户加密密码，本次账号和密码均未保存；请检查系统用户环境后重试。"); }
        finally
        {
            if (plaintext is not null) CryptographicOperations.ZeroMemory(plaintext);
            if (protectedPassword is not null) CryptographicOperations.ZeroMemory(protectedPassword);
        }
    }

    public string? ReadPassword(Guid accountId)
    {
        var protectedPassword = _repository.GetProtectedPassword(accountId);
        if (protectedPassword is null) return null;
        byte[]? plaintext = null;
        try
        {
            plaintext = ProtectedData.Unprotect(protectedPassword, accountId.ToByteArray(), DataProtectionScope.CurrentUser);
            return Utf8.GetString(plaintext);
        }
        catch (Exception error) when (error is CryptographicException or DecoderFallbackException)
        { throw new InvalidOperationException("无法解密已保存的密码。密码绑定原 Windows 用户和账号；请使用原 Windows 用户及本机数据，或重新保存密码。"); }
        finally
        {
            if (plaintext is not null) CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(protectedPassword);
        }
    }
}
