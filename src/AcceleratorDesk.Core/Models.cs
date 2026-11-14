namespace AcceleratorDesk.Core;

public enum AccountProvider { QQ, Weibo }
public enum AccountStage { Registered, Authorized, Redeemed }
public enum CancellationStatus { None, Requested, Waiting, Cancelled, Completed, Unverified }
public enum RedemptionOutcome { Pending, Succeeded, Failed }

public sealed record PhoneRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Number { get; set; } = "";
    public string Label { get; set; } = "";
    public string OfficialRestrictionNote { get; set; } = "";
    public DateTimeOffset? RestrictionUntil { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed record AccountRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public AccountProvider Provider { get; set; }
    public string Identifier { get; set; } = "";
    public string Nickname { get; set; } = "";
    public Guid? PhoneId { get; set; }
    public AccountStage Stage { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ExpiresAt { get; set; }
    public CancellationStatus CancellationState { get; set; }
    public DateTimeOffset? CancellationRequestedAt { get; set; }
    public DateTimeOffset? CancellationCompletedAt { get; set; }
    public DateTimeOffset? OfficialWaitUntil { get; set; }
    public string OfficialNotice { get; set; } = "";
    public string Notes { get; set; } = "";
}

public sealed record RedemptionRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccountId { get; set; }
    public string Code { get; set; } = "";
    public RedemptionOutcome Outcome { get; set; }
    public DateTimeOffset RedeemedAt { get; set; } = DateTimeOffset.UtcNow;
    public string GrantedDurationText { get; set; } = "";
    public DateTimeOffset? ExpiresAt { get; set; }
    public string ResultNote { get; set; } = "";
}
