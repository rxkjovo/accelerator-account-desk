# QQ registration final feature review

Reviewed: `200402c..c7e2024146c89dee313ed9cd2c8fa5fa55e6eae0` (including `2684fcb`), against the approved 2026-11-15 specification and implementation plan. Review is read-only except this report. Source, repository state, user data and running application were not changed.

## Strengths

- Credential storage has a clear boundary: DPAPI CurrentUser plus account-ID entropy in `AccountCredentialService.cs:23`, opaque blobs in the repository, and account/secret writes in one SQLite transaction (`DeskRepository.cs:168-202`). Normal account edits preserve existing secrets. Version checks, idempotent v1 upgrade, rollback, ciphertext tampering and ID binding have substantive coverage.
- Pending credentials are encrypted and flushed before external form actions. Failed account saves retain the draft, and cleanup follows successful persistence. `MainWindow.Tools.cs:20-30` holds an exclusive file lease for the complete modal wizard, preventing simultaneous production wizards from replacing the same draft.
- Sensitive form operations check both the host URL and in-page exact origin/path. Navigation does not widen the fill allowlist. Browser password/autofill persistence, web messages, downloads and unsolicited permissions are disabled. The consent-labeled app action is the only application path that accepts the agreement and submits.
- Result parsing requires a clear success line and one explicit QQ candidate; it rejects foreign sources, failure text, ambiguous results and the selected mobile number. Successful records retain unknown membership expiry. Retry gates avoid automatic repeat submission.
- README and validation notes distinguish real WebView2 fixture checks from real registration, explicitly disclose the unverified success-page adapter, and disclose the offline preview placeholder and DPAPI backup limits.

## Issues

### Critical (Must Fix)

None found.

### Important (Should Fix)

1. **Restored drafts can save an account linked to a different telephone number.**
   - **Locations:** `src/AcceleratorDesk.App/Services/QqRegistrationSession.cs:78-89`; `src/AcceleratorDesk.App/Dialogs/QqRegistrationWindow.cs:245-254`.
   - **Trigger:** Prepare a draft for phone A, close the wizard, edit that phone record's number to B in the existing phone editor, then reopen the wizard. Manual confirmation proceeds directly to `SaveConfirmed`; restored submission also uses the draft without calling `Prepare`. Both bypass the current-number comparison at `QqRegistrationSession.cs:25-31`.
   - **Impact:** The official registration still uses A from the encrypted draft, but the saved account points to the phone row now containing B. The locked combo also displays B, so it cannot serve as a reliable check. This violates the explicitly required phone/result consistency and can mislead later account recovery.
   - **Remedy:** Validate the stored phone identity and normalized number against the immutable draft before submission and confirmation/save. Reject changes with recovery guidance while retaining the draft. Ideally enforce the expected phone number within the account-save transaction to cover another application instance editing the phone concurrently. Show the draft's actual number when restoring it. Add a focused test covering close/edit/reopen and both manual/automatic save paths.

2. **Manual fallback records the generated password without confirming that it was actually used.**
   - **Locations:** `src/AcceleratorDesk.App/Dialogs/QqRegistrationWindow.cs:92-95,273-275`; `src/AcceleratorDesk.App/Services/QqRegistrationSession.cs:69-89`.
   - **Trigger:** Use the advertised system-browser fallback, or manually finish the official form after a selector/loading failure, with a different password. The checkbox confirms only official success and the QQ number. `ConfirmManual` then unconditionally persists `draft.Password` and clears the pending draft.
   - **Impact:** A valid account is saved with a password that cannot log into it, while the success notice asserts that its password was saved. The software cannot infer an externally entered password, and the specification explicitly forbids monitoring password input.
   - **Remedy:** On the manual path, require explicit confirmation that the generated password was used, or let the user deliberately enter the actual password. A clearly labeled account-only save with no password is also safe if the actual password is unavailable. Keep the normal app submission path automatic. Add a focused fallback test proving that a different/unknown password is never silently replaced with the draft value.

### Minor (Nice to Have)

1. **Adding another phone changes the displayed selection even while a draft is locked.**
   - **Locations:** `src/AcceleratorDesk.App/Dialogs/QqRegistrationWindow.cs:142-151,155-162`.
   - **Issue:** The Add button remains enabled after `Prepare`. `AddPhone` calls `RefreshPhones(lastPhoneId)`, which selects the newly added number in the disabled combo. The draft and submission still use the original phone. A later Start click rejects the apparent selection, while Submit silently uses the original selection.
   - **Remedy:** Disable adding/changing the selected number while a draft exists, or add the new record while preserving the draft's selected phone. Test the displayed selection after adding a number during an active draft.

2. **An unreadable saved password prevents opening the editor that would replace it.**
   - **Locations:** `src/AcceleratorDesk.App/Dialogs/CredentialDialog.cs:22`; `src/AcceleratorDesk.App/Services/AccountCredentialService.cs:47-48`.
   - **Issue:** The dialog constructor immediately decrypts; an invalid/cross-user blob throws before the editable UI appears. The error recommends saving a password again, but the only password editor cannot be opened in that state.
   - **Remedy:** Catch the expected decryption error at the dialog boundary, display its recovery explanation, leave the current ciphertext untouched, and allow an explicit replacement password to be entered and saved. Do not interpret decryption failure as an empty stored password or overwrite it merely by opening/closing the window.

## Compliance and validation evidence

- **Reviewed directly:** production wizard lifecycle and handlers, bridge scripts, registration policy/session, draft storage, account credential service, clipboard cleanup, repository migration/transactions, main-window integration and styles, test sources, README, specification and validation notes.
- **Existing evidence inspected:** `artifacts/v0.3-tests.log` reports 83 passes: 16 core, 10 app logic, 27 credential/draft, 9 session, 6 real-WebView2 fixtures, 9 existing WPF and 6 new WPF. The browser fixture installs a wildcard interception handler and returns local markup for every request. Existing tests do not cover the above state-consistency cases. No suite was rerun during this review.
- **Meets the main constraints:** single user-selected phone workflow; explicit SMS/consent actions; no verification bypass or batch loop; encrypted pending draft before external actions; atomic account/secret persistence; no password fields in public account records or CSV; no invented expiry; exact source guards; explicit manual success confirmation; documented real-service uncertainty.
- **Incomplete:** reliable phone association across restored drafts and trustworthy password semantics in the supported manual fallback, as detailed above.
- **Evidence boundary:** no SMS, QQ signup, safety verification, success-page request or external service submission was performed by this reviewer. Published startup verification remains the parent task's evidence. Fixture success cannot establish actual QQ acceptance or success-page compatibility.

## Recommendations

Resolve the two Important findings before delivery and cover their specific restored-state/manual-fallback cases with isolated tests. The two Minor fixes are small and fit the same UI/state correction wave. Preserve the current explicit user action boundary, draft recovery guarantees and candid real-service documentation while correcting these paths.

## Assessment

**Ready to merge? With fixes.**

**Severity counts:** 0 Critical, 2 Important, 2 Minor.

**Reasoning:** Encryption, atomic persistence and browser boundaries are soundly structured and supported by meaningful local tests. The current recovery/manual paths can nevertheless save a wrong phone association or an unverified password, so the two Important consistency gaps should be fixed before treating 0.3.0 as ready for user registration.
