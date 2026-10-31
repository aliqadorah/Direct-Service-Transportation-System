using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Platform;
using DirectService.Transportation.Contracts.Accounting;
using DirectService.Accounting.Contracts.Accounting;
using DirectService.Platform.Contracts.Identity;
using DirectService.Platform.Contracts.Tenants;
using DirectService.Transportation.Contracts.Security;
using DirectService.Transportation.Web.Pipeline;
using Microsoft.AspNetCore.Http.HttpResults;
using DirectService.Platform.Application.Identity;
using DirectService.Platform.Application.Staff;
using DirectService.Platform.Application.Email;
using DirectService.Platform.Application.Tenants;

namespace DirectService.Transportation.Web.Endpoints;

/// <summary>
/// Platform (B12): the provider console for platform staff (5.5, D-13; others get 404), the tenant's subscription and support-access decisions
/// (tenant.manage), current announcements (any signed-in user) and the payment-gateway webhook (anonymous, signature-verified, outside the
/// antiforgery group).
/// </summary>
internal static class PlatformEndpoints
{
    public static void MapPlatformEndpoints(this RouteGroupBuilder api)
    {
        var p = api.MapGroup("/platform").WithTags("Provider console").RequireAuthorization().AddEndpointFilter<PlatformStaffFilter>();
        p.MapGet("/plans", async (ConsoleService s, CancellationToken ct) => TypedResults.Ok(await s.PlansAsync(ct).ConfigureAwait(false)));
        p.MapPost("/plans", async (PlanRequest r, ConsoleService s, CancellationToken ct) => TypedResults.Ok(await s.SavePlanAsync(null, r, ct).ConfigureAwait(false)));
        p.MapPut("/plans/{plan}", async (string plan, PlanRequest r, ConsoleService s, CancellationToken ct) => TypedResults.Ok(await s.SavePlanAsync(plan, r, ct).ConfigureAwait(false)));
        p.MapPost("/plans/{plan}/delete", async (string plan, DeleteRequest r, ConsoleService s, CancellationToken ct) =>
        {
            await s.DeletePlanAsync(plan, r.Reason, ct).ConfigureAwait(false);
            return TypedResults.NoContent();
        });
        p.MapGet("/search", async (string? q, ConsoleService s, CancellationToken ct) => TypedResults.Ok(await s.SearchAsync(q, ct).ConfigureAwait(false)));
        p.MapGet("/tenants", async ([AsParameters] ConsoleTenantQuery q, ConsoleService s, CancellationToken ct) => TypedResults.Ok(await s.TenantsAsync(q, ct).ConfigureAwait(false)));
        p.MapGet("/tenants/{tenant}", async (string tenant, ConsoleService s, CancellationToken ct) => TypedResults.Ok(await s.TenantDetailsAsync(tenant, ct).ConfigureAwait(false)));
        p.MapPost("/tenants/{tenant}/extend-trial", async (string tenant, ExtendTrialRequest r, ConsoleService s, CancellationToken ct) =>
            TypedResults.Ok(await s.ExtendTrialAsync(tenant, r, ct).ConfigureAwait(false)));
        p.MapPost("/tenants/{tenant}/credit", async (string tenant, ApplyCreditRequest r, ConsoleService s, CancellationToken ct) =>
            TypedResults.Ok(await s.CreditAsync(tenant, r, ct).ConfigureAwait(false)));
        p.MapPost("/tenants/{tenant}/invoices/{invoice}/cancel", async (string tenant, string invoice, ReasonRequest r, ConsoleService s, CancellationToken ct) =>
            TypedResults.Ok(await s.CancelCheckoutAsync(tenant, invoice, r, ct).ConfigureAwait(false)));
        p.MapPost("/tenants/{tenant}/suspend", async (string tenant, TenantStatusRequest r, ConsoleService s, CancellationToken ct) =>
            TypedResults.Ok(await s.SuspendAsync(tenant, r, ct).ConfigureAwait(false)));
        p.MapPost("/tenants/{tenant}/reactivate", async (string tenant, TenantStatusRequest r, ConsoleService s, CancellationToken ct) =>
            TypedResults.Ok(await s.ReactivateAsync(tenant, r, ct).ConfigureAwait(false)));
        p.MapGet("/support-access", async (ConsoleService s, CancellationToken ct) => TypedResults.Ok(await s.MySupportAsync(ct).ConfigureAwait(false)));
        p.MapPost("/support-access", async (SupportAccessRequest r, ConsoleService s, CancellationToken ct) => TypedResults.Ok(await s.RequestSupportAsync(r, ct).ConfigureAwait(false)));
        p.MapGet("/support-access/tenants", async (string? q, ConsoleService s, CancellationToken ct) => TypedResults.Ok(await s.SupportTenantsAsync(q, ct).ConfigureAwait(false)));
        p.MapGet("/support-access/companies",async (ConsoleService s, CancellationToken ct) => TypedResults.Ok(await s.SupportCompaniesAsync(ct).ConfigureAwait(false)));
        p.MapGet("/announcements", async (ConsoleService s, CancellationToken ct) => TypedResults.Ok(await s.AnnouncementsAsync(false, ct).ConfigureAwait(false)));
        p.MapPost("/announcements", async (AnnouncementRequest r, ConsoleService s, CancellationToken ct) =>
            TypedResults.Ok(await s.SaveAnnouncementAsync(null, r, ct).ConfigureAwait(false)));
        p.MapPut("/announcements/{announcement}", async (string announcement, AnnouncementRequest r, ConsoleService s, CancellationToken ct) =>
            TypedResults.Ok(await s.SaveAnnouncementAsync(announcement, r, ct).ConfigureAwait(false)));
        p.MapPost("/announcements/{announcement}/delete", async (string announcement, ConsoleService s, CancellationToken ct) =>
        {
            await s.DeleteAnnouncementAsync(announcement, ct).ConfigureAwait(false);
            return TypedResults.NoContent();
        });
        p.MapGet("/health", async (ConsoleService s, CancellationToken ct) => TypedResults.Ok(await s.HealthAsync(ct).ConfigureAwait(false)));
        p.MapGet("/refunds", async (ConsoleService s, CancellationToken ct) => TypedResults.Ok(await s.RefundsAsync(ct).ConfigureAwait(false)));
        p.MapPost("/refunds/{invoice}/refunded", async (string invoice, MarkRefundedRequest r, ConsoleService s, CancellationToken ct) =>
            TypedResults.Ok(await s.MarkRefundedAsync(invoice, r, ct).ConfigureAwait(false)));
        // System email (DT-250…DT-252): settings (the secret is write-only), test email, status, delivery log, resend.
        p.MapGet("/system-email", async (SystemEmailService s, CancellationToken ct) => TypedResults.Ok(await s.GetAsync(ct).ConfigureAwait(false)));
        p.MapPut("/system-email", async (SaveSystemEmailRequest r, SystemEmailService s, CancellationToken ct) => TypedResults.Ok(await s.SaveAsync(r, ct).ConfigureAwait(false)));
        p.MapPost("/system-email/test", async (SystemEmailTestRequest r, SystemEmailService s, CancellationToken ct) => TypedResults.Ok(await s.TestAsync(r, ct).ConfigureAwait(false)));
        p.MapGet("/system-email/alert", async (SystemEmailService s, CancellationToken ct) => TypedResults.Ok(await s.AlertAsync(ct).ConfigureAwait(false)));
        p.MapGet("/system-email/log", async ([AsParameters] EmailLogQuery q, SystemEmailService s, CancellationToken ct) => TypedResults.Ok(await s.LogAsync(q, ct).ConfigureAwait(false)));
        p.MapPost("/system-email/log/{id}/resend", async (string id, SystemEmailService s, CancellationToken ct) =>
            TypedResults.Ok(await s.ResendAsync(id, ct).ConfigureAwait(false)));
        // Platform staff (owner feedback 13 Oct 2026, item 8b): list, invitations, deactivation.
        p.MapGet("/staff", async (StaffService s, CancellationToken ct) => TypedResults.Ok(await s.ListAsync(ct).ConfigureAwait(false)));
        p.MapPost("/staff/invitations", async (StaffInviteRequest r, StaffService s, CancellationToken ct) => TypedResults.Ok(await s.InviteAsync(r, ct).ConfigureAwait(false)));
        p.MapPost("/staff/{staff}/resend", async (string staff, StaffService s, CancellationToken ct) => TypedResults.Ok(await s.ResendAsync(staff, ct).ConfigureAwait(false)));
        p.MapPost("/staff/{staff}/cancel", async (string staff, StaffService s, CancellationToken ct) =>
        {
            await s.CancelAsync(staff, ct).ConfigureAwait(false);
            return TypedResults.NoContent();
        });
        p.MapPost("/staff/{staff}/deactivate", async (string staff, StaffStatusRequest r, StaffService s, CancellationToken ct) =>
            TypedResults.Ok(await s.DeactivateAsync(staff, r, ct).ConfigureAwait(false)));
        p.MapPost("/staff/{staff}/reactivate", async (string staff, StaffService s, CancellationToken ct) => TypedResults.Ok(await s.ReactivateAsync(staff, ct).ConfigureAwait(false)));

        api.MapGet("/announcements", async (ConsoleService s, CancellationToken ct) => TypedResults.Ok(await s.AnnouncementsAsync(true, ct).ConfigureAwait(false)))
            .WithTags("Announcements").RequireAuthorization();

        var t = api.MapGroup("/tenant").WithTags("Subscription").AddEndpointFilter<WriteGuardFilter>();
        // Paying, cancelling and deciding on support stay possible while suspended (BR-011 reactivation by payment).
        t.MapGet("/subscription", async (SubscriptionService s, CancellationToken ct) => TypedResults.Ok(await s.GetAsync(ct).ConfigureAwait(false)))
            .RequireTenantPermission(PermissionKeys.TenantManage);
        t.MapGet("/subscription/invoices", async (SubscriptionService s, CancellationToken ct) => TypedResults.Ok(await s.InvoicesAsync(ct).ConfigureAwait(false)))
            .RequireTenantPermission(PermissionKeys.TenantManage);
        t.MapGet("/subscription/invoices/{invoice}/print", async (string invoice, string? format, SubscriptionService s, CancellationToken ct) =>
            File(await s.PrintAsync(invoice, format, ct).ConfigureAwait(false))).RequireTenantPermission(PermissionKeys.TenantManage);
        t.MapPost("/subscription/checkout", async (CheckoutRequest r, SubscriptionService s, CancellationToken ct) =>
            TypedResults.Ok(await s.CheckoutAsync(r, ct).ConfigureAwait(false))).RequireTenantPermission(PermissionKeys.TenantManage).AllowWhenReadOnly();
        t.MapPost("/subscription/downgrade", async (PlanChangeRequest r, SubscriptionService s, CancellationToken ct) =>
            TypedResults.Ok(await s.DowngradeAsync(r, ct).ConfigureAwait(false))).RequireTenantPermission(PermissionKeys.TenantManage);
        t.MapPost("/subscription/cancel", async (ReasonRequest r, SubscriptionService s, CancellationToken ct) =>
            TypedResults.Ok(await s.CancelAsync(r.Reason, ct).ConfigureAwait(false))).RequireTenantPermission(PermissionKeys.TenantManage).AllowWhenReadOnly();
        t.MapPost("/demo-company", async (DemoCompanyService s, CancellationToken ct) => TypedResults.Ok(await s.CreateAsync(ct).ConfigureAwait(false)))
            .RequireTenantPermission(PermissionKeys.TenantManage);
        t.MapGet("/support-access", async (SupportAccessService s, CancellationToken ct) => TypedResults.Ok(await s.ListAsync(ct).ConfigureAwait(false)))
            .RequireTenantPermission(PermissionKeys.TenantManage);
        t.MapPost("/support-access/{grant}/approve", async (string grant, SupportApprovalRequest r, SupportAccessService s, CancellationToken ct) =>
            TypedResults.Ok(await s.ApproveAsync(grant, r, ct).ConfigureAwait(false))).RequireTenantPermission(PermissionKeys.TenantManage).AllowWhenReadOnly();
        t.MapPost("/support-access/{grant}/reject", async (string grant, ReasonRequest r, SupportAccessService s, CancellationToken ct) =>
            TypedResults.Ok(await s.RejectAsync(grant, r.Reason, ct).ConfigureAwait(false))).RequireTenantPermission(PermissionKeys.TenantManage).AllowWhenReadOnly();
        t.MapPost("/support-access/{grant}/end", async (string grant, SupportAccessService s, CancellationToken ct) =>
            TypedResults.Ok(await s.EndAsync(grant, ct).ConfigureAwait(false))).RequireTenantPermission(PermissionKeys.TenantManage).AllowWhenReadOnly();
    }

    /// <summary>Onboarding and personal data inside a company (5.4, SEC-19).</summary>
    public static void MapOnboardingEndpoints(this RouteGroupBuilder company)
    {
        var s = company.MapGroup("/setup-checklist").WithTags("Setup checklist");
        s.MapGet("/", async (SetupChecklistService svc, CancellationToken ct) => TypedResults.Ok(await svc.GetAsync(ct).ConfigureAwait(false))).RequireMember();
        s.MapPost("/{step}", async (string step, SetupMarkRequest r, SetupChecklistService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.MarkAsync(step, r.Done, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.SettingsManage);
        company.MapPost("/drivers/{driver}/anonymise", async (string driver, ReasonRequest r, DriverPersonalDataService svc, CancellationToken ct) =>
        {
            await svc.AnonymiseDriverAsync(driver, r.Reason, ct).ConfigureAwait(false);
            return TypedResults.NoContent();
        }).WithTags("Personal data").RequirePermission(PermissionKeys.MastersManage);
        company.MapPost("/users/{user}/anonymise", async (string user, ReasonRequest r, PersonalDataService svc, CancellationToken ct) =>
        {
            await svc.AnonymiseUserAsync(user, r.Reason, ct).ConfigureAwait(false);
            return TypedResults.NoContent();
        }).WithTags("Personal data").RequirePermission(PermissionKeys.UsersManage);
    }

    /// <summary>The gateway's webhook: outside the antiforgery group, anonymous, verified by its signature (P-04, SEC-10).</summary>
    public static void MapBillingWebhook(this WebApplication app)
    {
        app.MapPost("/api/v1/billing/webhook", async (HttpRequest request, SubscriptionService s, CancellationToken ct) =>
        {
            using var reader = new StreamReader(request.Body);
            var body = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
            var ok = await s.WebhookAsync(body, request.Headers["X-Signature"].ToString(), ct).ConfigureAwait(false);
            return ok ? Results.Ok() : Results.Unauthorized();
        }).AllowAnonymous().WithTags("Billing").ExcludeFromDescription();
    }

    private static FileContentHttpResult File(GeneratedFile f) => TypedResults.File(f.Content, f.ContentType, f.FileName);
}

/// <summary>Provider console: platform staff only; everyone else sees 404 (the console does not exist for them, SEC-22 style).</summary>
internal sealed class PlatformStaffFilter(IRequestContext ctx) : IEndpointFilter
{
    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(next);
        if (!ctx.IsPlatformStaff || !ctx.IsAuthenticated) throw new AppException(Contracts.Errors.ErrorCodes.NotFound, StatusCodes.Status404NotFound);
        return next(context);
    }
}
