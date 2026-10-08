using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace DirectService.Transportation.Web.Pipeline;

/// <summary>
/// OpenAPI 3.1 document for the development team (API-02): cookie authentication with the antiforgery header,
/// every route parameter declared (URL tokens are opaque 22-character strings, SEC-22), server URL normalised.
/// </summary>
internal static class OpenApiSetup
{
    public const string CookieScheme = "sessionCookie";

    private static readonly Dictionary<string, string> TagDescriptions = new(StringComparer.Ordinal)
    {
        ["Authentication"] = "Sign-up, email verification, sign-in with mandatory two-factor authentication, password reset, invitations (anonymous, rate-limited).",
        ["Me"] = "The signed-in user's profile, preferences, company switcher and sessions.",
        ["Tenant"] = "Tenant security settings and companies (core.tenant.manage).",
        ["Users"] = "Users of the current company (core.users.manage).",
        ["Roles"] = "Preset and custom roles with their permission keys (core.users.manage).",
        ["Invitations"] = "Email invitations to the current company (core.users.manage).",
        ["Company settings"] = "Company profile, market, operations and numbering settings (core.settings.manage).",
        ["Record locks"] = "Edit locks released on close or after 90 seconds idle (any company member).",
    };

    public static void Configure(OpenApiOptions o)
    {
        o.AddDocumentTransformer((doc, _, _) =>
        {
            doc.Info = new OpenApiInfo
            {
                Title = "Direct Service Transportation API",
                Version = "v1",
                License = new OpenApiLicense { Name = "Proprietary — Direct Service Company" },
                Description = "Internal REST API used only by the platform's own Blazor interface (API-01). All identifiers are opaque URL tokens. " +
                              "State-changing calls need the antiforgery header X-XSRF-TOKEN (GET /api/v1/auth/antiforgery). Errors are RFC 9457 problem details " +
                              "with a 'code' and messages in the user's language.",
            };
            doc.Components ??= new OpenApiComponents();
            doc.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
            doc.Components.SecuritySchemes[CookieScheme] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Cookie,
                Name = "__Host-dst",
                Description = "Session cookie issued after password + two-factor sign-in.",
            };
            foreach (var server in doc.Servers ?? [])
                server.Url = server.Url?.TrimEnd('/');
            foreach (var tag in doc.Tags ?? Enumerable.Empty<OpenApiTag>())
                tag.Description ??= TagDescriptions.TryGetValue(tag.Name ?? string.Empty, out var d) ? d : tag.Name;
            return Task.CompletedTask;
        });

        o.AddOperationTransformer((op, ctx, _) =>
        {
            // Declare route parameters that handlers do not bind themselves (e.g. {company}, resolved by middleware).
            var path = ctx.Description.RelativePath ?? string.Empty;
            op.Parameters ??= [];
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(path, @"\{([^}:]+)[^}]*\}"))
            {
                var name = m.Groups[1].Value;
                if (op.Parameters.Any(p => p.Name == name && p.In == ParameterLocation.Path)) continue;
                op.Parameters.Add(new OpenApiParameter
                {
                    Name = name,
                    In = ParameterLocation.Path,
                    Required = true,
                    Description = "Opaque URL token (22 characters).",
                    Schema = new OpenApiSchema { Type = JsonSchemaType.String },
                });
            }

            if (string.IsNullOrEmpty(op.Summary)
                && ApiSummaries.All.TryGetValue(ApiSummaries.Key(ctx.Description.HttpMethod ?? "GET", path), out var summary))
                op.Summary = summary;

            var method = ctx.Description.HttpMethod ?? "GET";
            op.OperationId ??= OperationId(method, path);

            var anonymous = ctx.Description.ActionDescriptor.EndpointMetadata.OfType<IAllowAnonymous>().Any();
            // Every operation can fail with problem details (API-06); see the error codes in the description.
            op.Responses ??= [];
            AddProblem(op, "400", "Validation failed (field errors in 'errors'), or missing antiforgery token");
            AddProblem(op, "422", "Refused by a business rule ('code' names the rule)");
            AddProblem(op, "429", "Too many requests");
            if (!anonymous)
            {
                AddProblem(op, "401", "Not signed in, or the session has ended");
                AddProblem(op, "403", "Missing permission, or the tenant/company is read-only");
                AddProblem(op, "404", "Not found (unknown, foreign or tampered identifier)");
                AddProblem(op, "409", "Conflict: stale version, duplicate value or plan limit");
            }
            op.Security = anonymous
                ? []
                : [new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference(CookieScheme, ctx.Document)] = [] }];
            return Task.CompletedTask;
        });
    }

    private static void AddProblem(OpenApiOperation op, string status, string description)
    {
        if (op.Responses!.ContainsKey(status)) return;
        op.Responses[status] = new OpenApiResponse
        {
            Description = description,
            Content = new Dictionary<string, OpenApiMediaType>
            {
                ["application/problem+json"] = new() { Schema = new OpenApiSchema { Type = JsonSchemaType.Object } },
            },
        };
    }

    /// <summary>Stable operation id from method and route, e.g. "putCompaniesUsersRole".</summary>
    private static string OperationId(string method, string path)
    {
        var words = path.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Where(s => !s.StartsWith('{') && s is not "api" and not "v1")
            .SelectMany(s => s.Split('-', StringSplitOptions.RemoveEmptyEntries))
            .Select(s => char.ToUpperInvariant(s[0]) + s[1..]);
        var suffix = path.TrimEnd('/').EndsWith('}') ? "ById" : string.Empty;
        return method.ToLowerInvariant() + string.Concat(words) + suffix;
    }
}
