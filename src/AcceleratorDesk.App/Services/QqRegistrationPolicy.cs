using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace AcceleratorDesk.App.Services;

public static class QqRegistrationPolicy
{
    public const string RegistrationUrl = "https://ssl.zc.qq.com/v5/index/?lang=zhCN";
    public static bool IsRegistrationPage(string? source) => OfficialUri(source) is { } uri
        && uri.Host.Equals("ssl.zc.qq.com", StringComparison.OrdinalIgnoreCase)
        && uri.AbsolutePath == "/v5/index/";

    public static bool IsOfficialNavigation(string? source) => OfficialUri(source) is { } uri
        && (uri.Host.Equals("qq.com", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".qq.com", StringComparison.OrdinalIgnoreCase));

    private static Uri? OfficialUri(string? source) => Uri.TryCreate(source, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps && uri.Port == 443 && uri.UserInfo.Length == 0 ? uri : null;

    public static string NormalizePhone(string number)
    {
        var compact = Regex.Replace(number ?? "", @"[\s()\-]", "");
        if (compact.StartsWith("+86", StringComparison.Ordinal)) compact = compact[3..];
        if (!Regex.IsMatch(compact, @"^1[3-9][0-9]{9}$", RegexOptions.CultureInvariant))
            throw new ArgumentException("此注册入口当前使用 +86，请选择有效的中国大陆手机号；其他区号请在官方页面操作。");
        return compact;
    }

    public static string? ReadSuccessfulQq(string? source, string visibleText, string phoneNumber)
    {
        if (!IsRegistrationPage(source) || string.IsNullOrWhiteSpace(visibleText)) return null;
        if (Regex.IsMatch(visibleText, "注册失败|注册未成功|尚未注册成功|未完成注册")) return null;
        var lines = visibleText.Split('\n').Select(line => line.Replace("\uFFFC", "").Trim()).Where(line => line.Length > 0).ToArray();
        var successLines = Enumerable.Range(0, lines.Length).Where(index => Regex.IsMatch(lines[index], @"^(?:恭喜(?:你|您)?[，,！!\s]*)?(?:QQ(?:账号|帐号|号)?\s*)?注册成功[！!。\s]*$")).ToArray();
        if (successLines.Length == 0) return null;
        var matches = Regex.Matches(visibleText, @"(?:QQ\s*(?:号码|号|账号|帐号)|您的QQ号|你的QQ号)[\s:：]*(?<qq>[1-9][0-9]{4,11})(?![0-9])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            .Select(match => match.Groups["qq"].Value).Distinct().ToList();
        // The observed official success page omits the QQ label. Require its complete result context.
        if (matches.Count == 0 && successLines.Length == 1)
        {
            var resultIndex = successLines[0] + 1;
            var numericLines = lines.Where(line => Regex.IsMatch(line, @"^[1-9][0-9]{4,11}$")).ToArray();
            if (resultIndex + 1 < lines.Length && numericLines.Length == 1 && lines[resultIndex] == numericLines[0]
                && Regex.IsMatch(lines[resultIndex + 1], @"^三天内未登录[，,]\s*该号码将会被收回[。.!！]?$")
                && visibleText.Contains("已安装QQ，立即登录", StringComparison.Ordinal) && visibleText.Contains("下载QQ", StringComparison.Ordinal))
                matches.Add(numericLines[0]);
        }
        if (matches.Count != 1) return null;
        var phoneDigits = new string(phoneNumber.Where(char.IsAsciiDigit).ToArray());
        return phoneDigits.EndsWith(matches[0], StringComparison.Ordinal) && matches[0].Length == 11 ? null : matches[0];
    }

    public static (string Nickname, string Password) CreateCredentials()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";
        var password = new char[10];
        password[0] = 'A'; password[1] = 'a'; password[2] = '2';
        for (var i = 3; i < password.Length; i++) password[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
        for (var i = password.Length - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (password[i], password[j]) = (password[j], password[i]);
        }
        var value = new string(password);
        Array.Clear(password);
        return ($"玩家{RandomNumberGenerator.GetInt32(100000, 1000000)}", value);
    }
}
