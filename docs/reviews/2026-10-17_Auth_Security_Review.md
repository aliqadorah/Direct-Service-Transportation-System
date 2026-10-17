# Security review — authentication changes of revision 6 (owner change requests 15 Oct 2026, items 1–2)

_Reviewed 17 Oct 2026. Scope: `AuthService`, `AuthFlow`, `UserAdminService` (create with password, set password), `UserStore` (username look-up), `MeService`/`/me` (own password, 2FA on), `TenantService` (Require 2FA), `SessionService.ValidateAsync`, the account pages (`SignIn`, `ChangePassword`, `TwoFactorSetup`, `ForgotPassword`). Decision recorded as DT-192._

## What was checked
| # | Area | Finding | Result |
|---|---|---|---|
| 1 | Account enumeration | Unknown name and wrong password give the same 401 message, with a password hash computed for unknown names (same cost); "forgot password" answers the same whether or not the address exists, and a username typed there sends nothing | OK |
| 2 | Lockout / rate limits | Username users share the 5-failures → 15-minute lockout (password, code, and the current password in the profile count as failures); `/api/v1/auth/*` and the account pages keep the auth rate limit | OK (test `Username_users_get_the_same_lockout`) |
| 3 | Forced password change | Only reachable with the partial sign-in cookie at stage `password` with an unchanged security stamp; the tenant policy applies and the new password must differ; the code step still follows for enrolled users | OK |
| 4 | Skipping 2FA | "Skip for now" needs stage `offer`, which is issued only at the first sign-in of an email user when 2FA is not required; the requirement is checked again at the moment of the skip (tenant may have turned it on) | OK (tests `Skip_is_refused_without_the_offer_stage`, `Turning_the_requirement_on_after_the_offer_refuses_the_skip`) |
| 5 | Platform staff | `RequiresTwoFactor` is true for staff (and any user without a tenant) whatever the tenant setting; sessions of staff without 2FA are refused by `SessionService` | OK (test `Platform_staff_always_need_2FA_and_cannot_skip`) |
| 6 | Require 2FA for all users | Owner only (`core.tenant.manage`); audited as SettingChange. **Fixed in review:** turning it on now ends the sessions of the tenant's users without 2FA (except the Owner's current session), so "at their next sign-in" starts at once instead of after up to 12 hours | Fixed (test `Turning_the_requirement_on_ends_the_sessions_of_users_without_2FA`) |
| 7 | Privilege escalation | Create-user requires `core.users.manage`; the role must be within the caller's keys (DT-18); set-password goes through the same target guard (Owner-level users only by Owners) and is limited to users created with a username | OK (tests in `UserNameSignInTests`) |
| 8 | Administrator learning a user's password | Set-password exists only for username users (who have no other reset) and asks for a new password at the next sign-in by default; users who sign in with their email keep the emailed link | OK (DT-190) |
| 9 | Session handling | Password set by an administrator ends all sessions; own password change and turning 2FA on change the security stamp (other sessions end) and re-issue this session's cookie | OK |
| 10 | Audit | Create (`CreatedWithPassword`), administrator reset (`PasswordSetByAdmin`), own change (`PasswordChanged`), 2FA on / reset / postponed (`TwoFactorEnabled`, `TwoFactorReset`, `TwoFactorSkipped`), tenant setting (`SettingChange`) | OK |
| 11 | Uniqueness | Username and email unique platform-wide (filtered unique indexes); a username cannot contain "@", so the combined look-up never matches two users | OK |

## Residual risks (accepted, for the owner)
- **Admin-entered email is not verified.** A username user with an email can use "forgot password"; a mistyped email would send the reset link to someone else. Mitigation available: send a verification email when the administrator enters an email (not built).
- **2FA optional by default.** Tenants that leave "Require 2FA for all users" off accept password-only accounts; the sign-up offer, the profile banner and the Users list badge ("No two-step verification") recommend it.
- The profile's `GET /me/2fa/setup` creates the authenticator key (a state change on GET), as the sign-in set-up already did; it needs a signed-in session and changes nothing visible until confirmed with a code.
