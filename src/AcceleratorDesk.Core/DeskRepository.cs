using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace AcceleratorDesk.Core;

/// <summary>Local records only. Each operation owns its connection; successful redemption effects share a transaction.</summary>
public sealed class DeskRepository
{
    private readonly string _databasePath;
    private readonly string _connectionString;

    public DeskRepository(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath)) throw new ArgumentException("请选择有效的本地数据库路径。", nameof(databasePath));
        try { _databasePath = Path.GetFullPath(databasePath); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        { throw new ArgumentException("数据库路径无效，请选择可写入的本地文件。", nameof(databasePath), ex); }
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath, Mode = SqliteOpenMode.ReadWriteCreate, ForeignKeys = true, DefaultTimeout = 5
        }.ToString();
    }

    public void Initialize()
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { throw new InvalidOperationException("无法创建数据目录，请检查目录权限和剩余空间。", ex); }
        WithConnection(connection =>
        {
            using var transaction = connection.BeginTransaction();
            using var command = Command(connection, transaction, """
                CREATE TABLE IF NOT EXISTS phones (
                    id TEXT PRIMARY KEY, number TEXT NOT NULL, label TEXT NOT NULL, official_restriction_note TEXT NOT NULL,
                    restriction_until TEXT NULL, created_at TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS accounts (
                    id TEXT PRIMARY KEY, provider INTEGER NOT NULL CHECK(provider IN (0, 1)),
                    identifier TEXT NOT NULL COLLATE NOCASE, nickname TEXT NOT NULL,
                    phone_id TEXT NULL REFERENCES phones(id), stage INTEGER NOT NULL CHECK(stage IN (0, 1, 2)),
                    created_at TEXT NOT NULL, expires_at TEXT NULL,
                    cancellation_state INTEGER NOT NULL CHECK(cancellation_state BETWEEN 0 AND 5),
                    cancellation_requested_at TEXT NULL, cancellation_completed_at TEXT NULL, official_wait_until TEXT NULL,
                    official_notice TEXT NOT NULL, notes TEXT NOT NULL,
                    UNIQUE(provider, identifier)
                );
                CREATE TABLE IF NOT EXISTS redemptions (
                    id TEXT PRIMARY KEY, account_id TEXT NOT NULL REFERENCES accounts(id), code TEXT NOT NULL,
                    outcome INTEGER NOT NULL CHECK(outcome IN (0, 1, 2)), redeemed_at TEXT NOT NULL,
                    granted_duration_text TEXT NOT NULL, expires_at TEXT NULL, result_note TEXT NOT NULL,
                    account_effect_applied INTEGER NOT NULL DEFAULT 0 CHECK(account_effect_applied IN (0, 1))
                );
                CREATE INDEX IF NOT EXISTS ix_redemptions_account_id ON redemptions(account_id);
                PRAGMA user_version = 1;
                """);
            command.ExecuteNonQuery(); transaction.Commit(); return 0;
        });
    }

    public IReadOnlyList<AccountRecord> GetAccounts() => WithConnection(connection =>
    {
        using var command = Command(connection, null, "SELECT * FROM accounts ORDER BY created_at, id");
        using var reader = command.ExecuteReader(); var result = new List<AccountRecord>();
        while (reader.Read()) result.Add(ReadAccount(reader));
        return (IReadOnlyList<AccountRecord>)result;
    });

    public IReadOnlyList<PhoneRecord> GetPhones() => WithConnection(connection =>
    {
        using var command = Command(connection, null, "SELECT * FROM phones ORDER BY created_at, id");
        using var reader = command.ExecuteReader(); var result = new List<PhoneRecord>();
        while (reader.Read()) result.Add(new()
        {
            Id = Guid.Parse(Text(reader, "id")), Number = Text(reader, "number"), Label = Text(reader, "label"),
            OfficialRestrictionNote = Text(reader, "official_restriction_note"), RestrictionUntil = Date(reader, "restriction_until"),
            CreatedAt = Date(reader, "created_at")!.Value
        });
        return (IReadOnlyList<PhoneRecord>)result;
    });

    public IReadOnlyList<RedemptionRecord> GetRedemptions(Guid? accountId = null) => WithConnection(connection =>
    {
        using var command = Command(connection, null,
            "SELECT * FROM redemptions" + (accountId.HasValue ? " WHERE account_id = $account_id" : "") + " ORDER BY redeemed_at, id");
        if (accountId.HasValue) Add(command, "$account_id", accountId.Value);
        using var reader = command.ExecuteReader(); var result = new List<RedemptionRecord>();
        while (reader.Read()) result.Add(new()
        {
            Id = Guid.Parse(Text(reader, "id")), AccountId = Guid.Parse(Text(reader, "account_id")), Code = Text(reader, "code"),
            Outcome = (RedemptionOutcome)reader.GetInt32(reader.GetOrdinal("outcome")), RedeemedAt = Date(reader, "redeemed_at")!.Value,
            GrantedDurationText = Text(reader, "granted_duration_text"), ExpiresAt = Date(reader, "expires_at"), ResultNote = Text(reader, "result_note")
        });
        return (IReadOnlyList<RedemptionRecord>)result;
    });

    public void SavePhone(PhoneRecord phone)
    {
        if (phone is null) throw new ArgumentException("请提供要保存的手机号记录。", nameof(phone));
        RequireId(phone.Id, "手机号记录");
        var number = phone.Number?.Trim() ?? "";
        var digits = number.Count(char.IsAsciiDigit);
        if (digits is < 6 or > 15 || !Regex.IsMatch(number, @"^\+?[0-9][0-9 ()-]*[0-9]$", RegexOptions.CultureInvariant)
            || !BalancedParentheses(number))
            throw new ArgumentException("手机号格式无效：请输入 6 至 15 位数字，可包含开头的 +、空格、短横线和成对括号。", nameof(phone));
        WithConnection(connection =>
        {
            using var command = Command(connection, null, """
                INSERT INTO phones (id, number, label, official_restriction_note, restriction_until, created_at)
                VALUES ($id, $number, $label, $note, $restriction, $created)
                ON CONFLICT(id) DO UPDATE SET number=excluded.number, label=excluded.label,
                    official_restriction_note=excluded.official_restriction_note, restriction_until=excluded.restriction_until, created_at=excluded.created_at
                """);
            Add(command, "$id", phone.Id); Add(command, "$number", number); Add(command, "$label", phone.Label ?? "");
            Add(command, "$note", phone.OfficialRestrictionNote ?? ""); Add(command, "$restriction", phone.RestrictionUntil); Add(command, "$created", phone.CreatedAt);
            command.ExecuteNonQuery(); return 0;
        });
    }

    public void SaveAccount(AccountRecord account)
    {
        if (account is null) throw new ArgumentException("请提供要保存的账号记录。", nameof(account));
        RequireId(account.Id, "账号");
        if (string.IsNullOrWhiteSpace(account.Identifier)) throw new ArgumentException("账号标识不能为空，请填写 QQ 号或微博账号。", nameof(account));
        RequireEnum(account.Provider, "账号平台"); RequireEnum(account.Stage, "账号阶段"); RequireEnum(account.CancellationState, "注销状态");
        if (account.CancellationState == CancellationStatus.Completed && account.CancellationCompletedAt is null)
            throw new ArgumentException("标记为已注销时，请填写官方确认的注销完成时间。", nameof(account));
        if (account.CancellationRequestedAt is { } requested)
        {
            if (account.CancellationCompletedAt is { } completed && completed < requested)
                throw new ArgumentException("注销完成时间不能早于申请时间，请核对官方记录。", nameof(account));
            if (account.OfficialWaitUntil is { } deadline && deadline < requested)
                throw new ArgumentException("官方等待截止时间不能早于注销申请时间。", nameof(account));
        }
        WithConnection(connection =>
        {
            using var transaction = connection.BeginTransaction();
            if (account.PhoneId is { } phoneId && !Exists(connection, transaction, "phones", phoneId))
                throw new ArgumentException("关联的手机号记录不存在，请重新选择手机号。", nameof(account));
            using (var duplicate = Command(connection, transaction, "SELECT 1 FROM accounts WHERE provider=$provider AND identifier=$identifier COLLATE NOCASE AND id<>$id"))
            {
                Add(duplicate, "$provider", (int)account.Provider); Add(duplicate, "$identifier", account.Identifier.Trim()); Add(duplicate, "$id", account.Id);
                if (duplicate.ExecuteScalar() is not null) throw new InvalidOperationException("该平台下的账号标识已存在，请编辑已有记录。不同平台可使用相同标识。");
            }
            using var command = Command(connection, transaction, """
                INSERT INTO accounts (id, provider, identifier, nickname, phone_id, stage, created_at, expires_at,
                    cancellation_state, cancellation_requested_at, cancellation_completed_at, official_wait_until, official_notice, notes)
                VALUES ($id, $provider, $identifier, $nickname, $phone, $stage, $created, $expiry, $cancellation, $requested, $completed, $wait, $notice, $notes)
                ON CONFLICT(id) DO UPDATE SET provider=excluded.provider, identifier=excluded.identifier, nickname=excluded.nickname,
                    phone_id=excluded.phone_id, stage=excluded.stage, created_at=excluded.created_at, expires_at=excluded.expires_at,
                    cancellation_state=excluded.cancellation_state, cancellation_requested_at=excluded.cancellation_requested_at,
                    cancellation_completed_at=excluded.cancellation_completed_at, official_wait_until=excluded.official_wait_until,
                    official_notice=excluded.official_notice, notes=excluded.notes
                """);
            Add(command, "$id", account.Id); Add(command, "$provider", (int)account.Provider); Add(command, "$identifier", account.Identifier.Trim());
            Add(command, "$nickname", account.Nickname ?? ""); Add(command, "$phone", account.PhoneId); Add(command, "$stage", (int)account.Stage);
            Add(command, "$created", account.CreatedAt); Add(command, "$expiry", account.ExpiresAt); Add(command, "$cancellation", (int)account.CancellationState);
            Add(command, "$requested", account.CancellationRequestedAt); Add(command, "$completed", account.CancellationCompletedAt);
            Add(command, "$wait", account.OfficialWaitUntil); Add(command, "$notice", account.OfficialNotice ?? ""); Add(command, "$notes", account.Notes ?? "");
            command.ExecuteNonQuery(); transaction.Commit(); return 0;
        });
    }

    public void SaveRedemption(RedemptionRecord redemption)
    {
        if (redemption is null) throw new ArgumentException("请提供要保存的兑换记录。", nameof(redemption));
        RequireId(redemption.Id, "兑换记录"); RequireId(redemption.AccountId, "关联账号"); RequireEnum(redemption.Outcome, "兑换结果");
        if (string.IsNullOrWhiteSpace(redemption.Code)) throw new ArgumentException("兑换码不能为空，请填写实际使用的兑换码。", nameof(redemption));
        WithConnection(connection =>
        {
            using var transaction = connection.BeginTransaction();
            AccountRecord account;
            using (var lookup = Command(connection, transaction, "SELECT * FROM accounts WHERE id=$id"))
            {
                Add(lookup, "$id", redemption.AccountId); using var reader = lookup.ExecuteReader();
                if (!reader.Read()) throw new ArgumentException("关联的账号不存在，请先保存账号记录。", nameof(redemption));
                account = ReadAccount(reader);
            }
            var alreadyApplied = false;
            using (var lookup = Command(connection, transaction, "SELECT account_id, account_effect_applied FROM redemptions WHERE id=$id"))
            {
                Add(lookup, "$id", redemption.Id); using var reader = lookup.ExecuteReader();
                if (reader.Read())
                {
                    if (Guid.Parse(reader.GetString(0)) != redemption.AccountId)
                        throw new ArgumentException("已有兑换记录不能更换关联账号，请在正确的账号下新增记录。", nameof(redemption));
                    alreadyApplied = reader.GetBoolean(1);
                }
            }
            var apply = redemption.Outcome == RedemptionOutcome.Succeeded && !alreadyApplied;
            using (var command = Command(connection, transaction, """
                INSERT INTO redemptions (id, account_id, code, outcome, redeemed_at, granted_duration_text, expires_at, result_note, account_effect_applied)
                VALUES ($id, $account, $code, $outcome, $redeemed, $duration, $expiry, $note, $applied)
                ON CONFLICT(id) DO UPDATE SET code=excluded.code, outcome=excluded.outcome, redeemed_at=excluded.redeemed_at,
                    granted_duration_text=excluded.granted_duration_text, expires_at=excluded.expires_at,
                    result_note=excluded.result_note, account_effect_applied=excluded.account_effect_applied
                """))
            {
                Add(command, "$id", redemption.Id); Add(command, "$account", redemption.AccountId); Add(command, "$code", redemption.Code.Trim());
                Add(command, "$outcome", (int)redemption.Outcome); Add(command, "$redeemed", redemption.RedeemedAt);
                Add(command, "$duration", redemption.GrantedDurationText ?? ""); Add(command, "$expiry", redemption.ExpiresAt);
                Add(command, "$note", redemption.ResultNote ?? ""); Add(command, "$applied", alreadyApplied || apply ? 1 : 0); command.ExecuteNonQuery();
            }
            if (apply)
            {
                // Duration text is evidence, never arithmetic. Unknown or older expiry cannot overwrite a known later date.
                var expiry = account.ExpiresAt;
                if (redemption.ExpiresAt is { } supplied && (expiry is null || supplied > expiry.Value)) expiry = supplied;
                using var update = Command(connection, transaction, "UPDATE accounts SET stage=$stage, expires_at=$expiry WHERE id=$id");
                Add(update, "$stage", (int)AccountStage.Redeemed); Add(update, "$expiry", expiry); Add(update, "$id", account.Id); update.ExecuteNonQuery();
            }
            transaction.Commit(); return 0;
        });
    }

    private T WithConnection<T>(Func<SqliteConnection, T> action)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString); connection.Open(); return action(connection);
        }
        catch (SqliteException ex)
        {
            throw new InvalidOperationException(ex.SqliteErrorCode is 5 or 6
                ? "数据库正被其他操作占用，请稍后重试。"
                : "无法读写本地数据库，请检查记录关联、数据文件权限和剩余空间；本次保存未完成。", ex);
        }
    }

    private static SqliteCommand Command(SqliteConnection connection, SqliteTransaction? transaction, string sql)
    {
        var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = sql; return command;
    }

    private static void Add(SqliteCommand command, string name, object? value) => command.Parameters.AddWithValue(name, value switch
    {
        null => DBNull.Value, Guid guid => guid.ToString("D"), DateTimeOffset date => date.ToString("O", CultureInfo.InvariantCulture), _ => value
    });

    private static bool Exists(SqliteConnection connection, SqliteTransaction transaction, string table, Guid id)
    {
        // Table names come exclusively from fixed implementation constants.
        using var command = Command(connection, transaction, $"SELECT 1 FROM {table} WHERE id=$id"); Add(command, "$id", id); return command.ExecuteScalar() is not null;
    }

    private static AccountRecord ReadAccount(SqliteDataReader reader) => new()
    {
        Id = Guid.Parse(Text(reader, "id")), Provider = (AccountProvider)reader.GetInt32(reader.GetOrdinal("provider")),
        Identifier = Text(reader, "identifier"), Nickname = Text(reader, "nickname"),
        PhoneId = reader.IsDBNull(reader.GetOrdinal("phone_id")) ? null : Guid.Parse(Text(reader, "phone_id")),
        Stage = (AccountStage)reader.GetInt32(reader.GetOrdinal("stage")), CreatedAt = Date(reader, "created_at")!.Value,
        ExpiresAt = Date(reader, "expires_at"), CancellationState = (CancellationStatus)reader.GetInt32(reader.GetOrdinal("cancellation_state")),
        CancellationRequestedAt = Date(reader, "cancellation_requested_at"), CancellationCompletedAt = Date(reader, "cancellation_completed_at"),
        OfficialWaitUntil = Date(reader, "official_wait_until"), OfficialNotice = Text(reader, "official_notice"), Notes = Text(reader, "notes")
    };

    private static string Text(SqliteDataReader reader, string name) => reader.GetString(reader.GetOrdinal(name));
    private static DateTimeOffset? Date(SqliteDataReader reader, string name) => reader.IsDBNull(reader.GetOrdinal(name))
        ? null : DateTimeOffset.Parse(Text(reader, name), CultureInfo.InvariantCulture, DateTimeStyles.None);
    private static void RequireId(Guid id, string label)
    {
        if (id == Guid.Empty) throw new ArgumentException($"{label}的记录标识无效，请重新新建记录。");
    }
    private static void RequireEnum<T>(T value, string label) where T : struct, Enum
    {
        if (!Enum.IsDefined(value)) throw new ArgumentException($"{label}无效，请从列表中重新选择。");
    }
    private static bool BalancedParentheses(string value)
    {
        var open = false;
        foreach (var character in value)
        {
            if (character == '(') { if (open) return false; open = true; }
            if (character == ')') { if (!open) return false; open = false; }
        }
        return !open;
    }
}
