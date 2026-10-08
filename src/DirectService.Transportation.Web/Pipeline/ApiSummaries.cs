namespace DirectService.Transportation.Web.Pipeline;

/// <summary>
/// One-line summaries of every API operation for the OpenAPI document (API-02), keyed by "METHOD route".
/// A test fails when an endpoint has no summary here, so every new module documents its operations.
/// </summary>
internal static class ApiSummaries
{
    public static readonly IReadOnlyDictionary<string, string> All = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // Authentication (anonymous, rate-limited)
        ["GET /api/v1/auth/antiforgery"] = "Get an antiforgery token for the X-XSRF-TOKEN header",
        ["POST /api/v1/auth/sign-up"] = "Sign up: create the tenant, Owner account and first company (US-31)",
        ["POST /api/v1/auth/resend-verification"] = "Resend the email verification link",
        ["POST /api/v1/auth/verify-email"] = "Verify the email address with the link token",
        ["POST /api/v1/auth/sign-in"] = "Sign in with email and password; returns the required second-factor step",
        ["GET /api/v1/auth/2fa/setup"] = "Get the authenticator key for mandatory two-factor set-up (FR-177)",
        ["POST /api/v1/auth/2fa/setup"] = "Complete two-factor set-up; returns the one-time recovery codes",
        ["POST /api/v1/auth/2fa/verify"] = "Verify the authenticator code or a recovery code and start the session",
        ["POST /api/v1/auth/sign-out"] = "Sign out and end the server session",
        ["POST /api/v1/auth/password-reset/request"] = "Request a password reset email",
        ["POST /api/v1/auth/password-reset/confirm"] = "Set a new password with the reset link token",
        ["GET /api/v1/auth/invitations/{token}"] = "Describe an invitation link",
        ["POST /api/v1/auth/invitations/accept"] = "Accept an invitation and create the account",

        // Me
        ["GET /api/v1/me/"] = "Current user, tenant and the companies for the company switcher",
        ["PUT /api/v1/me/preferences"] = "Update language, default company and date format (FR-178)",
        ["GET /api/v1/me/sessions"] = "List the user's active sessions (FR-176)",
        ["DELETE /api/v1/me/sessions/{session}"] = "Sign out one session",
        ["POST /api/v1/me/sessions/sign-out-others"] = "Sign out all other sessions",
        ["POST /api/v1/me/invitations/accept"] = "Accept an invitation as an existing user of the tenant",

        // Tenant (core.tenant.manage)
        ["GET /api/v1/tenant/"] = "Tenant details, plan and security settings",
        ["PUT /api/v1/tenant/security"] = "Update the tenant password policy and idle timeout (FR-175, FR-176)",
        ["GET /api/v1/tenant/companies"] = "List the tenant's companies",
        ["POST /api/v1/tenant/companies"] = "Create a company within the plan limit",
        ["POST /api/v1/tenant/companies/{companyId}/archive"] = "Archive a company (read-only, data kept)",
        ["POST /api/v1/tenant/companies/{companyId}/restore"] = "Restore an archived company",

        // Users, roles, invitations (core.users.manage)
        ["GET /api/v1/companies/{company}/users/"] = "List the company's users with their role",
        ["PUT /api/v1/companies/{company}/users/{user}/role"] = "Assign the user's role in this company",
        ["POST /api/v1/companies/{company}/users/{user}/remove"] = "Remove the user's access to this company",
        ["POST /api/v1/companies/{company}/users/{user}/disable"] = "Disable a user",
        ["POST /api/v1/companies/{company}/users/{user}/enable"] = "Enable a user",
        ["POST /api/v1/companies/{company}/users/{user}/archive"] = "Archive a user (never deleted, BR-003)",
        ["POST /api/v1/companies/{company}/users/{user}/unlock"] = "Unlock a user locked after failed sign-ins",
        ["POST /api/v1/companies/{company}/users/{user}/password-reset"] = "Email the user a password reset link",
        ["POST /api/v1/companies/{company}/users/{user}/reset-2fa"] = "Reset the user's two-factor authentication",
        ["POST /api/v1/companies/{company}/users/{user}/sign-out"] = "Sign the user out everywhere",
        ["GET /api/v1/companies/{company}/roles/"] = "List the tenant's roles with their permissions",
        ["GET /api/v1/companies/{company}/roles/permissions"] = "List the permission keys",
        ["POST /api/v1/companies/{company}/roles/"] = "Create a custom role",
        ["PUT /api/v1/companies/{company}/roles/{role}"] = "Update a custom role",
        ["POST /api/v1/companies/{company}/roles/{role}/delete"] = "Delete an unused custom role",
        ["GET /api/v1/companies/{company}/invitations/"] = "List invitations to this company",
        ["POST /api/v1/companies/{company}/invitations/"] = "Invite a user by email with a role (FR-172)",
        ["POST /api/v1/companies/{company}/invitations/{invitation}/resend"] = "Resend an invitation with a new link",
        ["POST /api/v1/companies/{company}/invitations/{invitation}/cancel"] = "Cancel an open invitation",

        // Company settings (core.settings.manage)
        ["GET /api/v1/companies/{company}/settings/profile"] = "Company profile",
        ["PUT /api/v1/companies/{company}/settings/profile"] = "Update the company profile",
        ["PUT /api/v1/companies/{company}/settings/logo"] = "Upload the company logo (PNG or JPEG, up to 512 KB)",
        ["GET /api/v1/companies/{company}/settings/market"] = "Market settings: currency, tax, time zone, formats",
        ["PUT /api/v1/companies/{company}/settings/market"] = "Update the market settings",
        ["GET /api/v1/companies/{company}/settings/operations"] = "Transport operation settings",
        ["PUT /api/v1/companies/{company}/settings/operations"] = "Update the transport operation settings (FR-170)",
        ["POST /api/v1/companies/{company}/settings/migration-complete"] = "Mark data migration complete (irreversible, FR-171)",
        ["GET /api/v1/companies/{company}/settings/number-sequences"] = "List document number sequences",
        ["PUT /api/v1/companies/{company}/settings/number-sequences/{sequence}"] = "Update a number sequence (BR-038a)",
        ["GET /api/v1/companies/{company}/logo"] = "Company logo image",

        // Record locks (company members)
        ["POST /api/v1/companies/{company}/locks/"] = "Lock a record for editing (BR-039)",
        ["PUT /api/v1/companies/{company}/locks/{lockId}/heartbeat"] = "Keep a record lock alive",
        ["DELETE /api/v1/companies/{company}/locks/{lockId}"] = "Release a record lock",
    };

    public static string Key(string method, string? route) => $"{method} /{route?.TrimStart('/')}";
}
