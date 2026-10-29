using DirectService.Platform.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Platform.Application.Identity;

/// <summary>Password rules of a tenant (FR-175, revision 5 columns on core.Tenants).</summary>
public sealed record PasswordPolicy(int MinLength, bool RequireLetter, bool RequireDigit)
{
    /// <summary>PRD defaults; also used for provider staff (no tenant).</summary>
    public static readonly PasswordPolicy Default = new(8, true, true);

    public bool IsSatisfiedBy(string? password) =>
        password is not null
        && password.Length >= MinLength
        && (!RequireLetter || password.Any(char.IsLetter))
        && (!RequireDigit || password.Any(char.IsDigit));
}

/// <summary>Identity password validator applying the tenant's policy (letters include Arabic letters).</summary>
public sealed class TenantPasswordValidator(IAppDbContext db) : IPasswordValidator<User>
{
    public const string ErrorCode = "PasswordPolicy";

    public async Task<IdentityResult> ValidateAsync(UserManager<User> manager, User user, string? password)
    {
        ArgumentNullException.ThrowIfNull(user);
        var policy = await GetPolicyAsync(db, user.TenantId, CancellationToken.None).ConfigureAwait(false);
        return policy.IsSatisfiedBy(password)
            ? IdentityResult.Success
            : IdentityResult.Failed(new IdentityError { Code = ErrorCode, Description = "Password does not meet the tenant policy." });
    }

    public static async Task<PasswordPolicy> GetPolicyAsync(IAppDbContext db, long? tenantId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        if (tenantId is null) return PasswordPolicy.Default;
        return await db.Set<Tenant>().IgnoreQueryFilters()
            .Where(t => t.Id == tenantId)
            .Select(t => new PasswordPolicy(t.PasswordMinLength, t.PasswordRequireLetter, t.PasswordRequireDigit))
            .SingleOrDefaultAsync(ct).ConfigureAwait(false) ?? PasswordPolicy.Default;
    }
}
