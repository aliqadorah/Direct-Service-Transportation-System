using DirectService.Shared.Infrastructure.Collaboration;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Collaboration;
using DirectService.Transportation.Application.Configuration;
using DirectService.Platform.Application.Companies;
using DirectService.Transportation.Contracts.Platform;
using DirectService.Platform.Contracts.Tenants;
using DirectService.Transportation.Contracts.Security;
using DirectService.Accounting.Domain.Cfg;
using DirectService.Transportation.Web.Pipeline;

namespace DirectService.Transportation.Web.Endpoints;

internal static class CompanyEndpoints
{
    /// <summary>Company settings (core.settings.manage; FR-168 … FR-171).</summary>
    public static void MapSettingsEndpoints(this RouteGroupBuilder company)
    {
        var s = company.MapGroup("/settings").WithTags("Company settings").RequirePermission(PermissionKeys.SettingsManage);
        s.MapGet("/profile", async (CompanySettingsService svc, CancellationToken ct) => TypedResults.Ok(await svc.GetProfileAsync(ct).ConfigureAwait(false)));
        s.MapPut("/profile", async (CompanyProfileRequest r, CompanySettingsService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.UpdateProfileAsync(r, ct).ConfigureAwait(false)));
        s.MapPut("/logo", async (IFormFile file, CompanySettingsService svc, CancellationToken ct) =>
        {
            if (file.Length > CompanySettingsService.MaxLogoBytes)
                throw new AppException(Contracts.Errors.ErrorCodes.ValidationRange, 400, "logo", new FieldRef("logo"));
            using var ms = new MemoryStream();
            await file.CopyToAsync(ms, ct).ConfigureAwait(false);
            await svc.SetLogoAsync(ms.ToArray(), ct).ConfigureAwait(false);
            return TypedResults.NoContent();
        }).DisableAntiforgery(); // validated by AntiforgeryFilter (header token) like every other write
        s.MapGet("/market", async (CompanySettingsService svc, CancellationToken ct) => TypedResults.Ok(await svc.GetMarketAsync(ct).ConfigureAwait(false)));
        s.MapPut("/market", async (MarketSettingsRequest r, CompanySettingsService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.UpdateMarketAsync(r, ct).ConfigureAwait(false)));
        s.MapGet("/operations", async (OperationSettingsService svc, CancellationToken ct) => TypedResults.Ok(await svc.GetOperationsAsync(ct).ConfigureAwait(false)));
        s.MapPut("/operations", async (OperationSettingsRequest r, OperationSettingsService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.UpdateOperationsAsync(r, ct).ConfigureAwait(false)));
        s.MapPost("/migration-complete", async (MigrationCompleteRequest r, CompanySettingsService svc, CancellationToken ct) =>
        { await svc.MarkMigrationCompleteAsync(r.Confirm, ct).ConfigureAwait(false); return TypedResults.NoContent(); });
        s.MapGet("/number-sequences", async (CompanySettingsService svc, CancellationToken ct) => TypedResults.Ok(await svc.SequencesAsync(ct).ConfigureAwait(false)));
        s.MapPut("/number-sequences/{sequence}", async (string sequence, NumberSequenceRequest r, CompanySettingsService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.UpdateSequenceAsync(sequence, r, ct).ConfigureAwait(false)));

        // The logo is printed on documents and shown in the app shell: any member of the company may read it.
        company.MapGet("/logo", async (CompanySettingsService svc, CancellationToken ct) =>
        {
            var bytes = await svc.GetLogoAsync(ct).ConfigureAwait(false);
            if (bytes is null || bytes.Length == 0) return Results.NotFound();
            var type = bytes[0] == 0x89 ? "image/png" : "image/jpeg";
            return Results.File(bytes, type);
        }).WithTags("Company settings").RequireAuthorization(PermissionPolicyProvider.Member).WithMetadata(new MemberEndpointMetadata());
    }

    /// <summary>Record locks (BR-039, FR-186): any member of the company may lock what they open for editing.</summary>
    public static void MapLockEndpoints(this RouteGroupBuilder company)
    {
        var locks = company.MapGroup("/locks").WithTags("Record locks")
            .RequireAuthorization(PermissionPolicyProvider.Member).WithMetadata(new MemberEndpointMetadata());

        locks.MapPost("/", async (AcquireLockRequest r, RecordLockService svc, IEntityResolver resolver, IRequestContext ctx,
            IRealtimeNotifier live, IUrlTokenService tokens, CancellationToken ct) =>
        {
            var id = await resolver.ResolveIdAsync(r.EntityType, r.Id, ct).ConfigureAwait(false) ?? throw Fail.NotFound();
            var info = await svc.AcquireAsync(r.EntityType, id, ct).ConfigureAwait(false);
            await live.LockChangedAsync(ctx.CompanyId!.Value, r.EntityType, r.Id, info.HolderName, ct).ConfigureAwait(false);
            return TypedResults.Ok(new LockResponse(tokens.Encode<RecordLock>(info.LockPublicId), info.HolderName, info.HeldByMe, info.AcquiredAt));
        });
        locks.MapPut("/{lockId}/heartbeat", async (string lockId, RecordLockService svc, IUrlTokenService tokens, CancellationToken ct) =>
        {
            var publicId = tokens.Decode<RecordLock>(lockId) ?? throw Fail.NotFound();
            await svc.HeartbeatAsync(publicId, ct).ConfigureAwait(false);
            return TypedResults.NoContent();
        });
        locks.MapDelete("/{lockId}", async (string lockId, RecordLockService svc, IUrlTokenService tokens, IRequestContext ctx,
            IRealtimeNotifier live, CancellationToken ct) =>
        {
            var publicId = tokens.Decode<RecordLock>(lockId) ?? throw Fail.NotFound();
            var released = await svc.ReleaseAsync(publicId, ct).ConfigureAwait(false);
            if (released is not null)
                await live.LockChangedAsync(ctx.CompanyId!.Value, released.EntityType, string.Empty, null, ct).ConfigureAwait(false);
            return TypedResults.NoContent();
        });
    }
}

/// <summary>Marks endpoints open to every member of the company (no specific permission key).</summary>
public sealed class MemberEndpointMetadata;
