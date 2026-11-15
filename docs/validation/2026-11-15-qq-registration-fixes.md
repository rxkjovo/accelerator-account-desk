# QQ registration final-review fixes

Status: all four findings in `artifacts/qq-registration-final-review.md` addressed in one cohesive source/test fix wave. Base: c7e2024146c89dee313ed9cd2c8fa5fa55e6eae0. Verification used local .NET SDK 10.0.404 on Windows. No release artifact or test/release script was changed.

## Changes and evidence mapping

1. Restored-draft phone identity and number consistency:
   - `QqRegistrationSession.ValidateDraftPhone()` compares the current stored phone ID and normalized mainland number with the immutable encrypted draft. It runs before MarkSubmitted and every confirmed account-save path. The native SubmitAsync path checks before touching the official form as well.
   - New `AccountCredentialService.SaveForRegistration(account, actualPassword, expectedPhoneId, expectedPhoneNumber)` encrypts at the existing Windows boundary and invokes a new four-argument repository SaveAccount overload. Existing general Save/SaveAccount APIs remain compatible and preserve their prior null-password behavior.
   - The repository checks account.PhoneId and the current normalized phone number inside the same SQLite write transaction before account/secret mutation and immediately before commit. A mismatch rolls everything back. No plaintext password enters Core.
   - Restored UI always labels the locked selection with the actual draft number, masked, even if its phone row was edited or removed. Mismatches show recovery guidance to restore the original phone record or explicitly abandon the retained draft.
   - Coverage: discard session/close wizard, edit the same phone ID to a different number, reopen, reject MarkSubmitted/manual/automatic save, retain original encrypted draft; reject a second repository's stale edit, reject a different PhoneId, accept equivalent formatting; real AFTER INSERT SQLite trigger mutates phone inside transaction, then final guard rolls back phone/account/secret together; stale update preserves prior account and ciphertext.

2. Trustworthy manual fallback password:
   - Manual confirmation now requires official-success confirmation AND either the explicitly checked, unchecked-by-default `本次使用软件生成的密码` option or deliberate entry in the hidden actual-password box.
   - ConfirmManual retains the two-argument entry point for callers, but that entry point rejects unknown password provenance. New explicit four-argument ConfirmManual supports generated-password confirmation or actual password; ambiguous dual input and empty/unknown actual passwords reject. Normal app-submission automatic saving continues to use the generated draft password.
   - Actual passwords are not trimmed. The original encrypted draft survives validation/duplicate/save failures, and is cleared only after the account/password transaction succeeds. The actual password box clears after successful persistence, on reset and on close.
   - Existing successful manual test callers now intentionally confirm the generated password. New tests prove success confirmation alone rejects without a row, a differing Unicode/space-containing actual password saves exactly, failure retains the original draft, and the native form starts with generated-password confirmation unchecked.
   - README/spec changes are limited to these manual password-source confirmation semantics.

3. Locked-draft Add behavior:
   - Adding/changing phone selection is disabled while a draft is active, and AddPhone independently guards the state.
   - RefreshPhones prioritizes the draft phone ID over a newly supplied selection. The focused native test adds another stored phone then refreshes with that ID and proves the displayed locked selection still matches the draft.

4. Recoverable unreadable credentials:
   - DPAPI/decoding failures now raise a typed CredentialDecryptionException, still compatible with InvalidOperationException callers. The password editor catches only this expected decryption failure at its UI boundary, opens with a hidden empty input, and displays recovery guidance. Ordinary database failures are not silently treated as missing passwords.
   - Opening, cancelling, or attempting an empty replacement leaves existing ciphertext untouched. An explicitly entered actual replacement goes through the existing atomic encrypted save. Existing reveal and close-clearing behavior remains covered.
   - Native tests exercise both tampered ciphertext and valid ciphertext copied from another account, verify exact ciphertext retention on opening/empty-save/cancel, and verify explicit replacement decrypts exactly afterward.

## Test-first results

Before production fixes, regressions were written and executed against c7e2024. Missing new APIs used temporary runtime reflection assertions only for the RED phase; final credential/session tests use strongly typed APIs.

| Command / suite | RED | Final GREEN |
| --- | --- | --- |
| `.tools\dotnet\dotnet.exe run --project tests\AcceleratorDesk.Credentials.Tests -c Release` | 27/32 passed, 5 failed; exit 1 | 32/32 passed, 0 failed; exit 0 |
| `.tools\dotnet\dotnet.exe run --project tests\AcceleratorDesk.Registration.Tests -c Release` | SESSION 9/15 passed, 6 failed; exit 1 before browser fixtures | SESSION 15/15 and BROWSER 6/6; exit 0 |
| `.tools\dotnet\dotnet.exe run --project tests\AcceleratorDesk.VisualPreview -c Release -- artifacts\qq-fix-native-red` | old FORM 9/9; REGISTRATION FORM 6/11, 5 failed; exit 1 | old FORM 9/9; REGISTRATION FORM 11/11, 0 failed; exit 0 |
| `.\scripts\test.ps1` | not run as RED | exit 0; 99/99 total |
| `git diff --check` and staged whitespace check | n/a | exit 0, no findings |

Complete script breakdown: 16 core + 10 app logic + 32 credential/draft/atomic-phone + 15 registration session + 6 intercepted WebView2 browser fixtures + 9 existing native forms + 11 registration/recovery native forms = 99 passing checks. Existing baseline was 83 checks, so 16 focused regressions were added.

One initial parallel build attempt failed with CS2012 because Core's intermediate assembly was locked during concurrent builds (diagnostic named the antivirus daemon). This was a build-infrastructure collision, not RED functional evidence; the executable RED, GREEN and full runs were performed sequentially and completed as reported. No script or production behavior was changed for that transient failure.

## Verification boundaries and remaining concerns

- All databases and drafts were isolated test/preview directories. No default user data or real user credential was read, edited or migrated.
- WPF form checks exercised actual controls and handlers offscreen. Browser fixture requests were fully intercepted and served local markup. No real QQ registration endpoint, SMS transmission, agreement acceptance, signup, safety verification or redemption was executed.
- Actual QQ service acceptance and the real success-page adapter remain unverified. Local passing fixture checks do not establish compatibility with a real registration success page.
- DPAPI cross-Windows-user/profile/reinstall recovery remains untested and retains its existing limitation. Power-loss/process-kill durability and race testing across complete production app instances were not performed. Atomic mismatch behavior was verified with separate repository instances and a real SQLite trigger.
- No known unresolved finding from the four-item review remains. General UI flows, browser policies, logging/export boundaries and release artifacts were otherwise kept within the requested scope.

## Exact RED output
### artifacts\qq-fix-credentials-red.log

```text
PASS newer database versions reject without downgrading the version or touching records
PASS original v1 schema upgrades idempotently without losing records or redemption state
PASS fresh databases and accounts without passwords have no secret
PASS Unicode and surrounding spaces survive restart exactly with CurrentUser account entropy
PASS null password and ordinary repository edits preserve the existing secret
PASS explicit password replacement updates one account and one secret
PASS empty passwords reject without adding accounts or replacing secrets
PASS account-bound ciphertext cannot decrypt after swapping two account blobs
PASS tampered ciphertext rejects with useful errors that contain no password
PASS SQLite secret insert failure rolls back the new account too
PASS SQLite secret update failure preserves both previous account and password
PASS duplicate identity and invalid phone association never leave a secret
PASS secret foreign key rejects orphan rows
PASS opaque blobs must be nonempty and bounded before any account mutation
PASS ciphertext read is detached and does not expose a mutable cached secret
PASS password plaintext is absent from account fields, database and active WAL bytes
PASS draft load returns null when absent without creating files
PASS pending draft survives restart with exact Unicode password and submission state
PASS draft password phone and nickname never appear in on-disk bytes
PASS valid draft replacement persists one new encrypted file and retains unrelated files
PASS tampered drafts report recovery guidance and cannot be silently overwritten
PASS empty or oversized draft files report useful errors and remain untouched
PASS failed atomic draft replacement preserves the original and removes its temporary file
PASS draft text representation suppresses password and phone details
PASS unreadable drafts reject load and overwrite without deleting the original
PASS invalid draft identities and required fields reject before persistence
PASS draft Clear removes only the owned pending file and is idempotent
FAIL registration save rejects stale expected phone after another repository edits the same ID: Exception: registration-specific atomic phone guard is not implemented
FAIL registration guard accepts equivalent normalized phone formatting: Exception: registration-specific atomic phone guard is not implemented
FAIL registration guard rejects a different account phone ID: Exception: registration-specific atomic phone guard is not implemented
FAIL transaction rechecks registration phone and rolls back trigger edits account and secret together: Exception: registration-specific atomic phone guard is not implemented
FAIL stale registration update preserves existing account and ciphertext: Exception: registration-specific atomic phone guard is not implemented
RESULT 27/32 passed; 5 failed

```

### artifacts\qq-fix-session-red.log

```text
PASS SESSION preparing registration creates recoverable encrypted draft without account
PASS SESSION prepared phone cannot be swapped under an existing result
FAIL SESSION restored draft rejects submission after its phone record number changes: Invalid state was accepted.
FAIL SESSION restored manual save rejects changed phone and keeps encrypted draft: explicit manual password confirmation API is not implemented
FAIL SESSION restored automatic save rejects changed phone and keeps encrypted draft: Invalid state was accepted.
FAIL SESSION manual success confirmation alone cannot assume generated password: Invalid state was accepted.
FAIL SESSION manual fallback saves deliberately entered differing password exactly: explicit manual password confirmation API is not implemented
FAIL SESSION manual empty password rejects and failed actual password save retains original draft: explicit manual password confirmation API is not implemented
PASS SESSION page success text before explicit submission never creates an account
PASS SESSION submitted official success saves phone and password once without invented membership
PASS SESSION unrelated or uncertain pages retain draft and no successful account
PASS SESSION manual fallback requires user confirmation and valid QQ
PASS SESSION duplicate QQ save failure retains draft and does not overwrite another account password
PASS SESSION known unsent rejection unlocks code correction without losing generated password
PASS SESSION only explicit official code failure permits another user submission
RESULT SESSION 9/15 passed; 6 failed
System.Exception: Registration session checks failed.
   at SessionChecks.Run() in C:\Users\admin\Desktop\迅雷加速器\tests\AcceleratorDesk.Registration.Tests\SessionChecks.cs:line 127
   at Program.Run() in C:\Users\admin\Desktop\迅雷加速器\tests\AcceleratorDesk.Registration.Tests\Program.cs:line 41
   at Program.<>c__DisplayClass1_0.<<Main>b__0>d.MoveNext() in C:\Users\admin\Desktop\迅雷加速器\tests\AcceleratorDesk.Registration.Tests\Program.cs:line 31

```

### artifacts\qq-fix-native-red.log

```text
PREVIEW C:\Users\admin\Desktop\迅雷加速器\artifacts\qq-fix-native-red\home-empty.png
PREVIEW C:\Users\admin\Desktop\迅雷加速器\artifacts\qq-fix-native-red\home-sample.png
CONTRAST primary button: 12.34:1
CONTRAST rendered primary text: 12.34:1
PREVIEW C:\Users\admin\Desktop\迅雷加速器\artifacts\qq-fix-native-red\home-compact.png
PREVIEW C:\Users\admin\Desktop\迅雷加速器\artifacts\qq-fix-native-red\account-editor.png
CONTRAST input text: 13.81:1
CONTRAST input text: 13.81:1
CONTRAST input text: 13.81:1
PREVIEW C:\Users\admin\Desktop\迅雷加速器\artifacts\qq-fix-native-red\phone-editor.png
CONTRAST input text: 13.81:1
CONTRAST input text: 13.81:1
CONTRAST input text: 13.81:1
CONTRAST input text: 13.81:1
PREVIEW C:\Users\admin\Desktop\迅雷加速器\artifacts\qq-fix-native-red\redemption-editor.png
CONTRAST input text: 13.81:1
CONTRAST input text: 13.81:1
CONTRAST input text: 13.81:1
CONTRAST input text: 13.81:1
CONTRAST input text: 13.81:1
PREVIEW C:\Users\admin\Desktop\迅雷加速器\artifacts\qq-fix-native-red\qq-registration.png
CONTRAST input text: 13.81:1
PREVIEW C:\Users\admin\Desktop\迅雷加速器\artifacts\qq-fix-native-red\account-password.png
CONTRAST input text: 13.81:1
CONTRAST password text: 13.81:1
PASS FORM phone form saves supplied number and unknown restriction
PASS FORM account form saves provider phone association and literal expiry
PASS FORM unsaved account edits do not mutate original record
PASS FORM completed cancellation without official completion date is rejected
PASS FORM recorded cancellation retains account and supplied completion
PASS FORM failed redemption form does not invent credit or advance stage
PASS FORM successful redemption form stores actual expiry
PASS FORM malformed date leaves previous account data intact
PASS FORM editing historical successful redemption does not replay expiry
RESULT FORM 9/9 passed
PASS REGISTRATION FORM opening wizard leaves account list and encrypted draft empty
PASS REGISTRATION FORM restored wizard locks matching phone and keeps generated password hidden
PASS REGISTRATION FORM manual fallback requires confirmation then saves actual QQ phone and password
PASS REGISTRATION FORM password editor saves encrypted value without changing membership
PASS REGISTRATION FORM password reveal is opt-in and all password fields clear on close
PASS REGISTRATION FORM closing unconfirmed wizard preserves encrypted draft without registered account
FAIL REGISTRATION FORM restored changed phone displays the draft number and recovery guidance: restored combo displays edited number instead of draft number
FAIL REGISTRATION FORM active draft disables Add and keeps its selection when another phone is added: Expected False; got True
FAIL REGISTRATION FORM manual form requires explicit password choice and saves actual differing password: Invalid form accepted.
FAIL REGISTRATION FORM tampered ciphertext editor opens hidden, cancel and empty save preserve, explicit replacement repairs: 无法解密已保存的密码。密码绑定原 Windows 用户和账号；请使用原 Windows 用户及本机数据，或重新保存密码。
FAIL REGISTRATION FORM cross-account ciphertext editor opens hidden, cancel and empty save preserve, explicit replacement repairs: 无法解密已保存的密码。密码绑定原 Windows 用户和账号；请使用原 Windows 用户及本机数据，或重新保存密码。
RESULT REGISTRATION FORM 6/11 passed; 5 failed
System.Exception: Registration form checks failed.
   at RegistrationFormChecks.Run(String root) in C:\Users\admin\Desktop\迅雷加速器\tests\AcceleratorDesk.VisualPreview\RegistrationFormChecks.cs:line 154
   at Program.Main(String[] args) in C:\Users\admin\Desktop\迅雷加速器\tests\AcceleratorDesk.VisualPreview\Program.cs:line 73

```

## Exact final full GREEN output

```text
PASS fresh persistent database is empty
PASS QQ and Weibo, phone links, unknown dates and Unicode survive reopen
PASS duplicate provider identity is rejected and cross-provider identity allowed
PASS account and phone edits upsert without adding rows
PASS membership boundaries use the supplied clock and preserve unknown
PASS Chinese enum labels and privacy masking
PASS completed cancellation remains queryable and requires date
PASS pending and failed redemption preserve account state and expiry
PASS success advances stage and only uses supplied expiry
PASS successful edits never replay expiry; pending to success applies once
PASS new successes never reduce expiry and accept explicitly past expiry
PASS invalid foreign keys preserve all prior rows
PASS SQLite failure rolls redemption and account mutation back together
PASS failed pending-to-success transaction can retry after reopen
PASS concurrent repository writers retain the greatest supplied expiry
PASS invalid inputs give actionable Chinese validation errors
RESULT 16/16 passed; 0 failed
PASS blank dates stay unknown
PASS explicit local date round-trips without clock drift
PASS impossible and ambiguous input formats are rejected
PASS CSV preserves Chinese quotes multiline and escapes spreadsheet formulas
PASS client setting persists and a fresh data directory starts empty
PASS registration filling rejects deceptive and unrelated origins
PASS official navigation permits QQ HTTPS only without widening form filling
PASS phone selection normalizes supported numbers and rejects other country formats
PASS explicit successful QQ label is required before automatic account recording
PASS each registration gets an independent QQ-compatible generated password
RESULT 10/10 passed; 0 failed
PASS newer database versions reject without downgrading the version or touching records
PASS original v1 schema upgrades idempotently without losing records or redemption state
PASS fresh databases and accounts without passwords have no secret
PASS Unicode and surrounding spaces survive restart exactly with CurrentUser account entropy
PASS null password and ordinary repository edits preserve the existing secret
PASS explicit password replacement updates one account and one secret
PASS empty passwords reject without adding accounts or replacing secrets
PASS account-bound ciphertext cannot decrypt after swapping two account blobs
PASS tampered ciphertext rejects with useful errors that contain no password
PASS SQLite secret insert failure rolls back the new account too
PASS SQLite secret update failure preserves both previous account and password
PASS duplicate identity and invalid phone association never leave a secret
PASS secret foreign key rejects orphan rows
PASS opaque blobs must be nonempty and bounded before any account mutation
PASS ciphertext read is detached and does not expose a mutable cached secret
PASS password plaintext is absent from account fields, database and active WAL bytes
PASS draft load returns null when absent without creating files
PASS pending draft survives restart with exact Unicode password and submission state
PASS draft password phone and nickname never appear in on-disk bytes
PASS valid draft replacement persists one new encrypted file and retains unrelated files
PASS tampered drafts report recovery guidance and cannot be silently overwritten
PASS empty or oversized draft files report useful errors and remain untouched
PASS failed atomic draft replacement preserves the original and removes its temporary file
PASS draft text representation suppresses password and phone details
PASS unreadable drafts reject load and overwrite without deleting the original
PASS invalid draft identities and required fields reject before persistence
PASS draft Clear removes only the owned pending file and is idempotent
PASS registration save rejects stale expected phone after another repository edits the same ID
PASS registration guard accepts equivalent normalized phone formatting
PASS registration guard rejects a different account phone ID
PASS transaction rechecks registration phone and rolls back trigger edits account and secret together
PASS stale registration update preserves existing account and ciphertext
RESULT 32/32 passed; 0 failed
PASS SESSION preparing registration creates recoverable encrypted draft without account
PASS SESSION prepared phone cannot be swapped under an existing result
PASS SESSION restored draft rejects submission after its phone record number changes
PASS SESSION restored manual save rejects changed phone and keeps encrypted draft
PASS SESSION restored automatic save rejects changed phone and keeps encrypted draft
PASS SESSION manual success confirmation alone cannot assume generated password
PASS SESSION manual fallback saves deliberately entered differing password exactly
PASS SESSION manual empty password rejects and failed actual password save retains original draft
PASS SESSION page success text before explicit submission never creates an account
PASS SESSION submitted official success saves phone and password once without invented membership
PASS SESSION unrelated or uncertain pages retain draft and no successful account
PASS SESSION manual fallback requires user confirmation and valid QQ
PASS SESSION duplicate QQ save failure retains draft and does not overwrite another account password
PASS SESSION known unsent rejection unlocks code correction without losing generated password
PASS SESSION only explicit official code failure permits another user submission
RESULT SESSION 15/15 passed; 0 failed
PASS BROWSER fill writes real fields and events without SMS agreement or submission
PASS BROWSER SMS request performs one explicit click
PASS BROWSER submission preparation without consent leaves agreement and code untouched
PASS BROWSER explicit consent and matching phone submit once
PASS BROWSER phone mismatch prevents submitting another phone's registration
PASS BROWSER foreign source cannot receive phone password SMS or registration actions
RESULT BROWSER 6/6 passed; 0 failed; all requests intercepted by local fixtures
PREVIEW C:\Users\admin\Desktop\迅雷加速器\artifacts\visual-preview\home-empty.png
PREVIEW C:\Users\admin\Desktop\迅雷加速器\artifacts\visual-preview\home-sample.png
CONTRAST primary button: 12.34:1
CONTRAST rendered primary text: 12.34:1
PREVIEW C:\Users\admin\Desktop\迅雷加速器\artifacts\visual-preview\home-compact.png
PREVIEW C:\Users\admin\Desktop\迅雷加速器\artifacts\visual-preview\account-editor.png
CONTRAST input text: 13.81:1
CONTRAST input text: 13.81:1
CONTRAST input text: 13.81:1
PREVIEW C:\Users\admin\Desktop\迅雷加速器\artifacts\visual-preview\phone-editor.png
CONTRAST input text: 13.81:1
CONTRAST input text: 13.81:1
CONTRAST input text: 13.81:1
CONTRAST input text: 13.81:1
PREVIEW C:\Users\admin\Desktop\迅雷加速器\artifacts\visual-preview\redemption-editor.png
CONTRAST input text: 13.81:1
CONTRAST input text: 13.81:1
CONTRAST input text: 13.81:1
CONTRAST input text: 13.81:1
CONTRAST input text: 13.81:1
PREVIEW C:\Users\admin\Desktop\迅雷加速器\artifacts\visual-preview\qq-registration.png
CONTRAST input text: 13.81:1
PREVIEW C:\Users\admin\Desktop\迅雷加速器\artifacts\visual-preview\account-password.png
CONTRAST input text: 13.81:1
CONTRAST password text: 13.81:1
PASS FORM phone form saves supplied number and unknown restriction
PASS FORM account form saves provider phone association and literal expiry
PASS FORM unsaved account edits do not mutate original record
PASS FORM completed cancellation without official completion date is rejected
PASS FORM recorded cancellation retains account and supplied completion
PASS FORM failed redemption form does not invent credit or advance stage
PASS FORM successful redemption form stores actual expiry
PASS FORM malformed date leaves previous account data intact
PASS FORM editing historical successful redemption does not replay expiry
RESULT FORM 9/9 passed
PASS REGISTRATION FORM opening wizard leaves account list and encrypted draft empty
PASS REGISTRATION FORM restored wizard locks matching phone and keeps generated password hidden
PASS REGISTRATION FORM manual fallback requires confirmation then saves actual QQ phone and password
PASS REGISTRATION FORM password editor saves encrypted value without changing membership
PASS REGISTRATION FORM password reveal is opt-in and all password fields clear on close
PASS REGISTRATION FORM closing unconfirmed wizard preserves encrypted draft without registered account
PASS REGISTRATION FORM restored changed phone displays the draft number and recovery guidance
PASS REGISTRATION FORM active draft disables Add and keeps its selection when another phone is added
PASS REGISTRATION FORM manual form requires explicit password choice and saves actual differing password
PASS REGISTRATION FORM tampered ciphertext editor opens hidden, cancel and empty save preserve, explicit replacement repairs
PASS REGISTRATION FORM cross-account ciphertext editor opens hidden, cancel and empty save preserve, explicit replacement repairs
RESULT REGISTRATION FORM 11/11 passed; 0 failed
Rendered native WPF content offscreen; no desktop input or external service actions.
C:\Users\admin\Desktop\迅雷加速器\artifacts\visual-preview

```

## Fix commit

daea95ea9973e03ae643c06b6e3061d75f18d865
