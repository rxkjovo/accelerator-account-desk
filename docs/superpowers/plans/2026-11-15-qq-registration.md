# QQ Registration Implementation Plan

> **For agentic workers:** Execute the approved work in this session. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver a working QQ official-registration assistant with phone selection and encrypted saved passwords.

**Architecture:** WPF hosts the official QQ page through WebView2. Pure registration policies validate source, phone and visible result candidates. A Windows credential service encrypts passwords before repository transactions persist opaque blobs alongside the existing account model.

**Tech Stack:** .NET 10.0.404, WPF, SQLite, Windows DPAPI, WebView2 Evergreen runtime already installed on this computer.

**Spec:** `docs/superpowers/specs/2026-11-15-qq-registration-design.md`

## Global Constraints

- Existing user data and redemption semantics remain intact.
- No real SMS, CAPTCHA solving, agreement acceptance or registration submission during tests. Product requests SMS and submits only on explicit user's start/submit buttons. No bulk scheduling or bypass.
- Passwords never enter account models, logs, CSV, notes or public lists. Encrypt with DPAPI CurrentUser plus per-account ID entropy.
- Account and protected password commit atomically; legacy databases upgrade in place without losing records.
- Form filling only on the exact official HTTPS registration source. Page navigation never implies success.
- Result extraction is conservative and follows explicit user submission; clear official success saves automatically. Manual fallback requires official-success confirmation and an explicit password-source choice.
- Keep the approved dark theme and readable inputs. Publish a separate 0.3.0 folder and retain previous delivery.

### Task 1: Encrypted account credentials

Files: `src/AcceleratorDesk.Core/DeskRepository.cs`, new `src/AcceleratorDesk.App/Services/AccountCredentialService.cs`, `Services/QqRegistrationDraftStore.cs`, new `tests/AcceleratorDesk.Credentials.Tests`.

API: existing `SaveAccount(AccountRecord)` remains unchanged; add `SaveAccount(AccountRecord, byte[] protectedPassword)` and `byte[]? GetProtectedPassword(Guid accountId)`. Service constructor receives `DeskRepository`; `void Save(AccountRecord account, string? password)` encrypts or preserves; `string? ReadPassword(Guid accountId)` decrypts a stored blob. Null password preserves any existing secret; empty passwords reject.

- [x] Add failing real SQLite and Windows encryption tests, including legacy upgrade and trigger-induced transaction rollback.
- [x] Implement a FK-linked `account_secrets` table, idempotent schema version upgrade, and overloaded atomic persistence. Core never receives plaintext.
- [x] Implement DPAPI CurrentUser with account ID entropy, safe plaintext buffer clearing and useful decryption errors; no embedded keys or password logging.
- [x] Persist generated registration details as an encrypted pending draft before external steps; reopening restores it, confirmed account save removes it, corruption is never silently overwritten.
- [x] Run credential and original core tests and record exact results. Commit only owned core/service/test files.

### Task 2: Official registration wizard and password access

Files: new `Services/QqRegistrationPolicy.cs`, `Services/QqPageBridge.cs`, `Dialogs/QqRegistrationWindow.cs`, `Dialogs/CredentialDialog.cs`; existing main view/tools/records, dark theme, app package metadata and tests.

- [x] Add policy tests for trusted origins, normalized mainland phone numbers, random credentials, explicit success/candidate parsing and ambiguous/failure rejection. Browser fixtures assert fill-only does not send/submit, explicit SMS sends once, and submit requires explicit consent.
- [x] Add WebView2 package, own isolated browser data folder, fixed-source guarded fill scripts and conservative visible-text result retrieval.
- [x] Implement a readable window with phone selection/add, automatically generated hidden credentials, start/SMS button, code entry, consent-labeled submission button, official browser, inline failures and manual-result fallback. Keep account records unchanged until detected or explicitly confirmed success, then save automatically.
- [x] Integrate registration entry and password access for selected accounts. Keep passwords hidden until user reveal, clear controls on close and clear only unchanged copied secrets after a short timeout.
- [x] Verify actual WPF form persistence and state handling with independent data, and render the new offline wizard/password states. Browser failures must preserve manual result saving.

### Task 3: Delivery

Files: scripts, README, validation report and version labels.

- [x] Run the appropriate complete local checks, build and publish 0.3.0. Record unavailable real-service checks accurately.
- [x] Obtain final independent code review, resolve material findings with scoped verification, commit intended changes, and retain source locally.
- [x] Repoint the existing desktop shortcut to the new exe and start the new default-data app, preserving data and settings. Verify the published window and provide a direct exe link.

## Delivery record

Implemented in `2684fcb`, `c7e2024` and one review-fix commit `daea95e`. Final complete local verification passed 99/99 checks; the scoped independent re-review resolved all four initial findings. Published 0.3.0 matches the final Release assemblies, the default database upgraded without changing the saved phone/settings, and the new window and desktop shortcut were verified. See `docs/validation/2026-11-15-v0.3.0.md` for evidence and limitations. Actual QQ signup and real success-page compatibility remain unverified; no real SMS, signup or safety verification was performed.
