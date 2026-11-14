namespace AcceleratorDesk.Core;

public static class AccountPresentation
{
    public static string MembershipStatus(AccountRecord account, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(account);
        return account.ExpiresAt is not { } expiry ? "未记录到期"
            : expiry <= now ? "已到期"
            : expiry - now <= TimeSpan.FromDays(3) ? "即将到期" : "使用中";
    }

    public static string MaskPhone(string number)
    {
        if (string.IsNullOrEmpty(number)) return "";
        var digits = new string(number.Where(char.IsAsciiDigit).ToArray());
        return digits.Length >= 8 ? $"{digits[..3]}****{digits[^4..]}" : new string('*', digits.Length);
    }

    public static string ProviderLabel(AccountProvider provider) => provider switch
    {
        AccountProvider.QQ => "QQ", AccountProvider.Weibo => "微博", _ => "未知平台"
    };

    public static string StageLabel(AccountStage stage) => stage switch
    {
        AccountStage.Registered => "已注册", AccountStage.Authorized => "已授权", AccountStage.Redeemed => "已兑换", _ => "未知阶段"
    };

    public static string CancellationLabel(CancellationStatus status) => status switch
    {
        CancellationStatus.None => "未申请", CancellationStatus.Requested => "已申请", CancellationStatus.Waiting => "等待中",
        CancellationStatus.Cancelled => "已撤销", CancellationStatus.Completed => "已注销", CancellationStatus.Unverified => "待核实", _ => "未知状态"
    };

    public static string OutcomeLabel(RedemptionOutcome outcome) => outcome switch
    {
        RedemptionOutcome.Pending => "待确认", RedemptionOutcome.Succeeded => "成功", RedemptionOutcome.Failed => "失败", _ => "未知结果"
    };
}
