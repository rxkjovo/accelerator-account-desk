# QQ registration scoped re-review

**Verdict: Yes — the four original findings are resolved.**

Reviewed only `c7e2024146c89dee313ed9cd2c8fa5fa55e6eae0..daea95ea9973e03ae643c06b6e3061d75f18d865`, continuing `artifacts/qq-registration-final-review.md`. HEAD was verified as `daea95ea9973e03ae643c06b6e3061d75f18d865`. This was a source/test/documentation review of the single correction wave, not a new full-feature review. No product files, tests, Git state or user data were modified; this report is the only write.

## Strengths

- The phone check is enforced in the repository transaction as well as in the UI/session. This closes the original restored-draft path and protects against a stale pre-save read.
- Manual fallback now requires an explicit choice of generated or actual password. Ordinary automatic submission keeps its original workflow, and manually supplied passwords retain their exact characters.
- The decryption recovery change catches a narrow typed exception. A database failure is not mistaken for an absent password, and opening/cancelling the editor does not replace existing ciphertext.
- Regression checks exercise actual SQLite rollback, Windows credential operations and native WPF controls using isolated data. Documentation retains the real-service validation boundary.

## Original finding dispositions

| Original finding | Disposition | Source evidence and reasoning |
| --- | --- | --- |
| Important 1: restored draft can link a registration to an edited phone number | **Resolved** | `QqRegistrationSession.cs:82` validates current stored identity/normalized number against the draft. It is called before marking submission (`:43`) and confirmed saving (`:100`); the native submission path also checks before form actions (`QqRegistrationWindow.cs:266`). `AccountCredentialService.cs:15` routes registration persistence through the expected-phone overload, and `DeskRepository.cs:179,211,222` checks before mutation and again before committing the same account/secret transaction. A mismatch retains the draft and rolls back writes. Restored selection is rendered from the draft number (`QqRegistrationWindow.cs:143`) with recovery guidance (`:167`). |
| Important 2: manual fallback silently saves a generated password that may not have been used | **Resolved** | `QqRegistrationSession.cs:72-79` rejects absent password provenance and ambiguous dual input. The original two-argument call is safe by rejecting unknown provenance. `QqRegistrationWindow.cs:25,97-101,294` supplies an unchecked-by-default generated-password confirmation or deliberate hidden actual-password input. Exact actual text is saved through the existing encrypted boundary. The new input clears after successful save, reset and close. |
| Minor 1: Add changes the displayed selection during a locked draft | **Resolved** | `QqRegistrationWindow.cs:175` disables Add while a draft exists; `:153` independently guards the handler; `:143-151` gives the draft priority over a newly supplied selection. These changes keep the visible locked selection consistent with the registration draft. |
| Minor 2: unreadable credentials prevent opening their replacement editor | **Resolved** | `AccountCredentialService.cs:57,66` exposes a typed expected decryption failure. `CredentialDialog.cs:22-23` catches only that failure, opens with an empty hidden field and recovery guidance, and leaves existing ciphertext alone. Empty replacement is still rejected by the credential service. Only deliberate nonempty saving replaces the secret. |

Paths in the table are under `src/AcceleratorDesk.App/Services`, `src/AcceleratorDesk.App/Dialogs`, or `src/AcceleratorDesk.Core` as applicable.

## Issues

### Critical (Must Fix)

None remaining in the scoped review.

### Important (Should Fix)

None remaining in the scoped review.

### Minor (Nice to Have)

None remaining from the original review; no new actionable defect found in the correction diff.

## Regression and evidence assessment

- Read the actual production diff and added/modified tests, including `RegistrationPhoneChecks.cs`, registration session cases and native registration/recovery form cases. The five repository cases check stale edits from another repository, equivalent number formatting, wrong phone identity, trigger-induced in-transaction changes and rollback, and preservation of existing account/ciphertext.
- The six added session cases cover restored submission/manual/automatic rejection with draft retention, missing manual password provenance, an actual differing password, and failed-save retention. The five added native cases cover restored phone display/recovery, locked Add/selection, manual password choice, and both tampered and cross-account ciphertext recovery.
- Inspected `artifacts/qq-registration-fix-report.md` and the independent full output `artifacts/qq-registration-fix-full-tests.log`. The output reports **99/99**: 16 core + 10 app logic + 32 credential/draft/phone checks + 15 session + 6 WebView2 fixtures + 9 original forms + 11 registration/recovery forms. Sixteen focused regressions supplement the prior 83 checks.
- No suite was rerun in this re-review; no new unresolved concern justified repeating the supplied complete run. The RED/GREEN history is evidence recorded by the implementation task, not an execution claim by this reviewer.
- No new database migration or public account/password field was introduced. Ordinary account/credential APIs keep their prior persistence semantics. Source guards, explicit consent submission, draft-before-external-action ordering and unknown expiry behavior were not weakened by this diff.

## Recommendations

Proceed with the parent task's documentation and republish steps. Preserve the explicit limits already documented: passing local fixtures and native form tests do not establish actual QQ registration acceptance or real success-page compatibility.

## Assessment

**Ready to merge? Yes, for this scoped correction wave.**

**Remaining severity counts:** 0 Critical, 0 Important, 0 Minor.

**Reasoning:** All four original findings are addressed at the relevant production boundaries and backed by focused regression cases. No new breaking change was found in the reviewed diff.

**Evidence boundary:** This reviewer did not send SMS, accept QQ agreements, submit registration, operate safety verification, inspect a real signup success page, run the published executable or modify default user data. Browser fixtures intercept requests and cannot be described as a real successful registration. Live QQ acceptance, actual success-page parsing, and release/startup validation remain separate from this source review.
