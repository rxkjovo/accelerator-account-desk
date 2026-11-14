# Accelerator Account Desk Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver a working Windows exe for the user's QQ/Weibo account, phone, membership, redemption, and cancellation records.

**Architecture:** A .NET 10 core owns SQLite persistence and state derivation. A WPF desktop interface provides editable records, search, filters, and a three-step official-entry workflow. Registration, SMS, authorization, redemption submission, and cancellation occur in official interfaces with the user's participation.

**Tech Stack:** .NET SDK 10.0.404, WPF, Microsoft.Data.Sqlite 10.0.15, SQLite, PowerShell packaging; self-contained win-x64 folder distribution.

**Spec:** `docs/superpowers/specs/2026-11-15-desktop-assistant-design.md`

**Authorized visual revision:** `docs/superpowers/specs/2026-11-15-ui-redesign.md` records the user's later choice of a dark gaming client and the readability correction.

## Global Constraints

- Windows desktop exe; Chinese product UI.
- QQ and Weibo are account providers; do not equate provider signup or cancellation with accelerator eligibility or unlinking.
- Records persist locally and remain queryable after cancellation.
- Membership expiration, cancellation, and phone restriction are separate states; unknown official dates stay unknown.
- Store actual supplied expiry and redemption result; do not invent 10–15 days of credit.
- Official verification and agreement steps remain user-operated; no bulk signup, verification bypass, automatic account deletion, or phone-rotation scheduling.
- No user credentials, SMS codes, or authorization tokens in the database.
- No simulated successful signup, authorization, redemption, or cancellation.
- Data location defaults to `%LOCALAPPDATA%/AcceleratorAccountDesk`; `--data-dir <path>` selects a separate local data directory for portable use and UI checks.
- The dedicated workspace was empty apart from this session's design doc; initialize a local feature branch and retain all source and docs.

## File Structure

- `src/AcceleratorDesk.Core/Models.cs`: record types and enum contracts.
- `src/AcceleratorDesk.Core/DeskRepository.cs`: SQLite schema, parameterized persistence, explicit transaction semantics.
- `src/AcceleratorDesk.Core/AccountPresentation.cs`: membership status, phone masking, and enum labels.
- `tests/AcceleratorDesk.Tests/Program.cs`: meaningful tests against actual SQLite and literal clock fixtures, runnable as a console test harness.
- `src/AcceleratorDesk.App/App.xaml`, `Theme.xaml`: WPF startup and shared dark theme.
- `src/AcceleratorDesk.App/MainWindow.xaml`, `.xaml.cs`: account/phone/redemption/settings views, search, metrics, selection, official workflow buttons.
- `src/AcceleratorDesk.App/Dialogs/`: account, phone, redemption editors with validation and explicit result entry.
- `src/AcceleratorDesk.App/Services/`: local settings, user-selected client launching, CSV export.
- `scripts/build.ps1`, `scripts/test.ps1`: reproducible build, test and self-contained folder publish.
- `README.md`: startup, local data, workflow boundaries, build commands.

### Task 1: Durable records and state rules

**Files:** core project and console test project listed above.

**Interfaces:** Namespace `AcceleratorDesk.Core`; mutable records suitable for dialog editing. IDs use `Guid`, dates use nullable `DateTimeOffset` when optional.

```csharp
public enum AccountProvider { QQ, Weibo }
public enum AccountStage { Registered, Authorized, Redeemed }
public enum CancellationStatus { None, Requested, Waiting, Cancelled, Completed, Unverified }
public enum RedemptionOutcome { Pending, Succeeded, Failed }

// PhoneRecord: Id, Number, Label, OfficialRestrictionNote, RestrictionUntil?, CreatedAt.
// AccountRecord: Id, Provider, Identifier, Nickname, PhoneId?, Stage, CreatedAt,
// ExpiresAt?, CancellationState, CancellationRequestedAt?, CancellationCompletedAt?,
// OfficialWaitUntil?, OfficialNotice, Notes.
// RedemptionRecord: Id, AccountId, Code, Outcome, RedeemedAt,
// GrantedDurationText, ExpiresAt?, ResultNote.

public sealed class DeskRepository(string databasePath)
{
    public void Initialize();
    public IReadOnlyList<AccountRecord> GetAccounts();
    public IReadOnlyList<PhoneRecord> GetPhones();
    public IReadOnlyList<RedemptionRecord> GetRedemptions(Guid? accountId = null);
    public void SaveAccount(AccountRecord account);
    public void SavePhone(PhoneRecord phone);
    public void SaveRedemption(RedemptionRecord redemption);
}

public static class AccountPresentation
{
    public static string MembershipStatus(AccountRecord account, DateTimeOffset now);
    public static string MaskPhone(string number);
    public static string ProviderLabel(AccountProvider provider);
    public static string StageLabel(AccountStage stage);
    public static string CancellationLabel(CancellationStatus status);
    public static string OutcomeLabel(RedemptionOutcome outcome);
}
```

- [x] Write integration tests first. Examples: fresh database has zero records; reopen repository preserves QQ/Weibo and phone link; same provider+identifier cannot create duplicates; same identifier on different providers is allowed; null expiry displays `未记录到期`; expiry equal to fixture clock displays `已到期`; expiry within three days displays `即将到期`; later expiry displays `使用中`; unknown restriction remains null; completed cancellation preserves history and requires a completion date; failed redemption never advances account; successful redemption sets `Redeemed` and uses only an explicitly supplied expiry; updating one redemption does not create a second record or reapply older expiry; invalid foreign keys do not create partial writes; quoted/Unicode account notes round-trip safely.
- [x] Run failing behavior tests against unimplemented members, preserving the RED output in the task report.
- [x] Implement validated parameterized SQLite storage. Empty identifiers/codes, malformed phone numbers, duplicate identities, impossible cancellation timestamps, and invalid foreign-key links receive actionable validation errors. A membership expiry may be in the past. Waiting states may have no known deadline. Successful redemption persistence and its account update occur atomically. Keep a previously supplied account expiry if a later success has unknown expiry; never move the expiry backward when editing an older redemption.
- [x] Run `.tools/dotnet/dotnet.exe run --project tests/AcceleratorDesk.Tests -c Release` and record the GREEN result.
- [x] Commit only Task 1 source/tests and provide a report with exact API, test output, and any concerns.

### Task 2: Desktop UI and official-entry workflow

**Files:** app project, theme, main window, dialogs and services in the structure above.

**Consumes:** Task 1 contracts, unchanged. UI uses `DeskRepository` to load fresh records after each successful save.

- [x] Create a WPF app referencing Core. Launch with `--data-dir` support, explicit startup errors, and no sample user records.
- [x] Build a warm charcoal / olive interface with lime actions, a left navigation, compact statistics, searchable/filterable account list, a useful empty state, and a three-step official-entry strip with selected-account details, per the authorized visual revision. Keep controls keyboard accessible and visible at 1280×800 and 1100×720.
- [x] Provide modal account, phone, and redemption editors. Input dates use the visible format `yyyy-MM-dd HH:mm` in the computer's local time zone; null fields remain null. Account editing covers provider, identifier, phone link, stage, actual expiry, cancellation state/timestamps/official notice and notes. Phone editing covers number, label and official restriction date/notice. Redemption editing covers code, supplied outcome, actual duration/expiry and result note.
- [x] Add phone and redemption views, account copying, masked phone display, filtered CSV export with formula-prefix escaping, settings for the user-selected accelerator exe, and open-data-folder action. Selecting an exe is required before launching a client; never execute arbitrary URL schemes.
- [x] QQ button opens the verified HTTPS official registration page. Client buttons launch the selected local accelerator executable and clearly direct the user to QQ/Weibo login or redemption within that official client. Launching a page never marks an account as registered or a redemption as successful.
- [x] Add in-app upcoming/expired counts and refresh them every minute. User-triggered saves display saved status only after repository success; validation errors leave editors open.
- [x] Build and validate the desktop UI through real WPF form save/reload checks, six offscreen layouts and a published-window accessibility check. Actual desktop capture/input was unavailable; full physical click-through remains unverified, as recorded in the validation report. Do not submit any external signup, SMS, authorization, redemption or cancellation during validation.
- [x] Commit only Task 2 source and record tested behaviors.

### Task 3: Packaging and delivery

**Files:** scripts, README, plan/spec status and release output.

- [x] Add PowerShell scripts resolving the project-local SDK first. `scripts/test.ps1` runs core behavior tests. `scripts/build.ps1` builds and publishes `win-x64` self-contained output to `dist/加速账户助手-0.2.0`; fail immediately on native command errors.
- [x] Publish with the concrete command below; preserve the entire folder, including native SQLite and framework files.

```powershell
& .\.tools\dotnet\dotnet.exe publish .\src\AcceleratorDesk.App\AcceleratorDesk.App.csproj -c Release -r win-x64 --self-contained true -o .\dist\加速账户助手-0.2.0
```

- [x] Provide a Chinese-named launcher exe or folder alongside a brief Chinese usage guide. README documents default data location, optional `--data-dir`, user-operated official flows, expiry/cancellation semantics, build/test commands and backup via copying the data directory while the app is closed.
- [x] Run the behavior tests, Release build, and published-exe smoke check. Verify UI persistence with a separate test data directory; reopen the delivered exe on the real empty user data directory for handoff.
- [x] Review the final change for state integrity, false success, sensitive credential storage and any invented eligibility/date claim. Resolve material findings, record output sizes/checksum, and report the clickable exe path with accurate test boundaries.
