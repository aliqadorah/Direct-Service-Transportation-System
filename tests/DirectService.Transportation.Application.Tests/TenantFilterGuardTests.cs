using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DirectService.Transportation.Application.Tests;

/// <summary>
/// Build-time guard of tenant isolation (DT-217, after the support-access leak SA-1 of 19 Oct 2026): every place in the server code that
/// lifts the tenant or company query filter — <c>IgnoreQueryFilters()</c> without names or naming "Tenant"/"Company", raw SQL
/// (<c>SqlQuery</c>, <c>FromSql…</c>, <c>ExecuteSql…</c>), or a change of the data scope (<c>DataScope.Set/Use</c>) — must be on the allow-list
/// below with a one-line justification. Lifting only the soft-delete filter (<c>IgnoreQueryFilters([SoftDelete])</c>) is allowed anywhere.
/// A lifted filter inside a query whose own root keeps its filters (the SA-1 pattern: in EF Core the lift applies to the whole query) is
/// refused everywhere, allow-list or not. Stale allow-list entries fail too, so the list stays exact.
/// </summary>
public sealed class TenantFilterGuardTests
{
    /// <summary>"Type" (every member) or "Type.Member" → why every query there scopes by explicit tenant/company ids (or is not tenant data).</summary>
    internal static readonly IReadOnlyDictionary<string, string> AllowList = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // Provider console and platform staff (tenant users get 404 before any query: StaffId / staff-only routes).
        ["ConsoleService"] = "Provider console, platform staff only; every tenant query names the tenant id or token explicitly.",
        ["StaffService"] = "Platform staff accounts (no tenant): staff rows by IsPlatformStaff, scope set to the acting staff member.",
        ["StaffAccountService"] = "Operations command creating a staff account: system scope, no tenant data.",
        ["ProviderSetupService"] = "First provider set-up (operations): staff rows by IsPlatformStaff under the system account.",
        // Background jobs: each row is processed in a scope set to its own tenant and company.
        ["TenantLifecycleService"] = "Daily lifecycle job (system account): every query by the explicit tenant id being processed.",
        ["EInvoiceWorker"] = "Background job: queued submissions of all companies, each processed with the scope set to its own tenant/company.",
        ["PendingPostingWorker"] = "Background job: open postings of all companies, each retried with the scope set to its own tenant/company.",
        ["LockExpiryWorker"] = "Background job: expired record locks, each released with the scope set to the lock's own tenant/company.",
        ["ExportWorker"] = "Background exports: each job runs with the scope of the tenant/company/user that queued it (data export).",
        // Request pipeline and identity plumbing (before a tenant scope exists, or setting it).
        ["RequestContextMiddleware"] = "Sets the data scope from the validated session (the only place a request gets its tenant).",
        ["CompanyContextMiddleware"] = "Sets the company scope after checking the user's role (or an approved support window by explicit tenant/staff ids).",
        ["SessionService"] = "Sessions by unique token hash or the user's own id, before a tenant is known; scope set to the session's own user.",
        ["AuthService"] = "Sign-in by unique email/username before a tenant is known; scope set to that user's own tenant.",
        ["AccountService"] = "Sign-up, email verification, password reset by unique single-use tokens; scope set to that user's own tenant.",
        // DT-229: the signed-in user's own row (by the user's id, staff have no tenant) and the email-change link (user + purpose-bound token,
        // confirmed only by that user signed in; the save runs in that user's own tenant scope).
        ["EmailChangeService"] = "Own email change by the user's id; the confirmation link by a single-use, purpose-bound token for the signed-in user only.",
        ["InvitationService"] = "Invitation acceptance by unique single-use link token; scope set to the invitation's own tenant.",
        ["TwoFactorSettingsService.ConfirmChangeAsync|DisableAsync|NewRecoveryCodesAsync"] = "The signed-in user's own 2FA settings; scope set to that user's own tenant.",
        ["UserStore"] = "ASP.NET Identity store: users by id/unique name and their own recovery codes (users of all tenants share one table).",
        ["ReplaySafeAuthenticatorTokenProvider"] = "Raw SQL claiming the signed-in user's own last code step by user id (DT-212).",
        ["AccessService.GrantsAsync"] = "The signed-in user's own company grants (by user id) across the user's tenant, before a company is chosen.",
        ["TenantPasswordValidator"] = "The password policy of the user's own tenant (explicit tenant id), also before sign-in.",
        ["MeService.GetAsync|RevokeSessionAsync|SessionsAsync|UpdatePreferencesAsync"] = "The signed-in user's own profile, preferences and sessions by the user's id (staff have no tenant).",
        ["LiveHub.CompanyIdAsync"] = "Realtime connection: the signed-in user by own public id; the company is then checked against the user's grants.",
        // Tenant administration that crosses companies of the signed-in tenant only (the tenant filter stays).
        ["TenantService.DemoCompanyIdsAsync"] = "Lifts only the company filter to find the signed-in tenant's demo company; the tenant filter stays.",
        ["DemoCompanyService"] = "Lifts only the company filter of the signed-in tenant; scope set to the new demo company of that tenant.",
        ["CompanyProvisioningService"] = "Sets the scope to the company just created in the signed-in tenant while provisioning it.",
        ["PersonalDataService.AnonymiseUserAsync"] = "Invitations by explicit TenantId, sessions by the id of a user of this tenant (checked by GrantGuard).",
        // Billing and plan limits: always by an explicit tenant id.
        ["PlanLimits.ActiveUsersAsync|CountTripsAsync|EnsureTripsAsync|EnsureUserAsync|UsageAsync"] = "Plan usage and limits of one tenant: every query by the explicit tenant id passed in.",
        ["BillingService.ApplyPaymentAsync|SuspendedByStaffAsync"] = "Subscription invoices of one tenant: queries by the invoice's explicit tenant id.",
        ["SubscriptionService.WebhookAsync"] = "Signed payment webhook (no session): the invoice by gateway reference, scope set to that invoice's tenant.",
        ["PlatformNumbers"] = "Raw SQL: the platform-wide subscription invoice number sequence (provider data, no tenant rows).",
        // Notification holders and names of the people who acted.
        ["NotificationService.HoldersAsync|NotifyAsync|PushAsync|ReportSetupErrorAsync|ClearSetupErrorAsync"] = "Notification holders/recipients by explicit company or user ids (users of the company; staff have no tenant).",
        ["RecordLockService.AcquireAsync|HolderNameAsync"] = "Record locks: scope set to the lock's own tenant/company; the holder's name by user id.",
        ["AuditDescriber.LoadAsync|PrepareAsync"] = "Names of the users found in this tenant's own audit rows (support staff have no tenant): by those ids only.",
        ["AuditLogService.FacetsAsync"] = "Users who have a row in this company's log: explicit CompanyId condition, names only.",
        ["RateHistoryLog.ReadAsync"] = "Names of the users who changed this company's rates (ids from the company's own rows), names only.",
        ["SupportAccessService.ListAsync|RefusalReasonsAsync|TellStaffAsync|EndsAsync|AnnounceEndAsync|WhyClosedAsync|ExpireDueAsync"] = "Names of staff/Owners by ids from this tenant's own grants; audit rows by explicit TenantId (DT-213); a staff member's own window by explicit tenant/staff ids; the window job closes each grant in a scope set to its own tenant (DT-220).",
        ["MasterDataSupport.SaveCreateAsync"] = "Only whether a client id (random GUID) is already taken anywhere, to refuse its reuse; returns no data.",
        // Raw SQL locks and numbering (the filters do not apply to raw SQL).
        ["NumberingService"] = "Raw SQL row lock on the current company's own sequence row (explicit CompanyId).",
        ["EInvoiceStore"] = "Raw SQL locks/claims of e-invoice rows by explicit document ids already found in the company scope.",
        ["MonthlyRateLock"] = "Raw SQL lock of the current company's monthly rate rows (explicit CompanyId).",
    };

    private static readonly string[] RawSql = ["SqlQuery", "SqlQueryRaw", "FromSql", "FromSqlRaw", "FromSqlInterpolated", "ExecuteSql", "ExecuteSqlRaw",
        "ExecuteSqlInterpolated", "ExecuteSqlAsync", "ExecuteSqlRawAsync", "ExecuteSqlInterpolatedAsync"];

    internal sealed record Finding(string File, int Line, string Key, string Kind, bool Nested);

    [Fact]
    public void Tenant_and_company_filters_are_lifted_only_where_allowed()
    {
        var findings = Scan(SourceRoot());
        Assert.NotEmpty(findings);   // the scanner works (the pipeline itself sets the scope)
        var problems = new List<string>();
        var nested = findings.Where(f => f.Nested).Select(Describe).ToList();
        if (nested.Count > 0) problems.Add("Filters lifted inside a query that keeps its own filters (lifts the whole query, SA-1):\n" + string.Join('\n', nested));
        var missing = findings.Where(f => !f.Nested && !Allowed(f.Key)).Select(Describe).Distinct().ToList();
        if (missing.Count > 0) problems.Add("Tenant/company filter lifted outside the allow-list (TenantFilterGuardTests.AllowList):\n" + string.Join('\n', missing));
        // Every entry (each member of a "Type.A|B" entry) must still allow something.
        var stale = AllowList.Keys.SelectMany(k => k.IndexOf('.', StringComparison.Ordinal) is var dot and > 0
                ? k[(dot + 1)..].Split('|').Select(m => k[..dot] + "." + m) : [k])
            .Where(k => !findings.Any(f => Covers(k, f.Key))).ToList();
        if (stale.Count > 0) problems.Add("Allow-list entries with nothing to allow (remove them): " + string.Join(", ", stale));
        problems.AddRange(AllowList.Where(e => string.IsNullOrWhiteSpace(e.Value)).Select(e => e.Key + " needs a justification"));
        Assert.True(problems.Count == 0, string.Join("\n\n", problems));
    }

    [Fact]
    public void The_guard_recognises_each_way_of_lifting_a_filter()
    {
        const string code = """
            class Svc
            {
                private static readonly string[] Soft = [QueryFilterNames.SoftDelete];
                private static readonly string[] Ten = ["Tenant"];
                void A() { db.Set<X>().IgnoreQueryFilters([QueryFilterNames.SoftDelete]).ToList(); db.Set<X>().IgnoreQueryFilters(Soft).ToList(); }
                void B() { db.Set<X>().IgnoreQueryFilters().ToList(); }
                void C() { db.Set<X>().IgnoreQueryFilters(Ten).ToList(); }
                void D() { db.Set<X>().Where(x => db.Set<U>().IgnoreQueryFilters().Any(u => u.Id == x.U)).ToList(); }
                void E() { db.Database.SqlQuery<long>($"select 1"); scope.Use(1, 2, 3); }
                void F() { db.Set<X>().IgnoreQueryFilters().Where(x => db.Set<U>().IgnoreQueryFilters().Any()).ToList(); }
            }
            """;
        var found = ScanText("Svc.cs", code);
        Assert.DoesNotContain(found, f => f.Key == "Svc.A");
        Assert.Contains(found, f => f.Key == "Svc.B" && !f.Nested);
        Assert.Contains(found, f => f.Key == "Svc.C");
        Assert.Contains(found, f => f.Key == "Svc.D" && f.Nested);
        Assert.Equal(2, found.Count(f => f.Key == "Svc.E"));
        Assert.DoesNotContain(found, f => f.Key == "Svc.F" && f.Nested);
    }

    private static string Describe(Finding f) => $"{f.File}:{f.Line} {f.Key} — {f.Kind}{(f.Nested ? " (nested)" : "")}";

    private static bool Allowed(string key) => AllowList.Keys.Any(entry => Covers(entry, key));

    /// <summary>An entry "Type" covers every member of the type; "Type.A|B" covers members A and B only.</summary>
    private static bool Covers(string entry, string key)
    {
        var dot = entry.IndexOf('.', StringComparison.Ordinal);
        if (dot < 0) return key.StartsWith(entry + ".", StringComparison.Ordinal);
        var type = entry[..dot];
        return entry[(dot + 1)..].Split('|').Any(m => key == type + "." + m);
    }

    internal static string SourceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DirectService.Transportation.slnx"))) dir = dir.Parent;
        return Path.Combine(dir?.FullName ?? throw new InvalidOperationException("Repository root not found."), "src");
    }

    /// <summary>The server code: everything under src except the browser client, migrations and build output.</summary>
    internal static List<Finding> Scan(string src)
    {
        var findings = new List<Finding>();
        foreach (var file in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(src, file).Replace('\\', '/');
            if (rel.Contains("/obj/", StringComparison.Ordinal) || rel.Contains("/bin/", StringComparison.Ordinal) || rel.Contains("/Migrations/", StringComparison.Ordinal)
                || rel.StartsWith("DirectService.Transportation.Web.Client/", StringComparison.Ordinal))
                continue;
            findings.AddRange(ScanText(rel, File.ReadAllText(file)));
        }
        return findings;
    }

    internal static List<Finding> ScanText(string file, string code)
    {
        var root = CSharpSyntaxTree.ParseText(code).GetRoot();
        var result = new List<Finding>();
        foreach (var call in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var name = Name(call.Expression);
            string? kind = null;
            if (name == "IgnoreQueryFilters")
            {
                var lifted = Lifted(call, root);
                if (lifted.SetEquals(["SoftDelete"])) continue;
                kind = "IgnoreQueryFilters(" + string.Join(',', lifted.Order(StringComparer.Ordinal)) + ")";
                result.Add(new Finding(file, Line(call), Key(call), kind, IsNestedLift(call, lifted, root)));
                continue;
            }
            if (name is not null && RawSql.Contains(name)) kind = "raw SQL " + name;
            else if (name is "Set" or "Use" && call.Expression is MemberAccessExpressionSyntax m && IsDataScope(m.Expression)) kind = "data scope " + name;
            if (kind is not null) result.Add(new Finding(file, Line(call), Key(call), kind, false));
        }
        return result;
    }

    private static bool IsDataScope(ExpressionSyntax receiver) => receiver switch
    {
        IdentifierNameSyntax id => id.Identifier.Text.Contains("scope", StringComparison.OrdinalIgnoreCase) && !id.Identifier.Text.Contains("scopes", StringComparison.OrdinalIgnoreCase),
        MemberAccessExpressionSyntax m => m.Name.Identifier.Text.Contains("Scope", StringComparison.Ordinal),
        InvocationExpressionSyntax i => i.ToString().Contains("<DataScope>", StringComparison.Ordinal),
        _ => false,
    };

    private static string? Name(ExpressionSyntax e) => e switch
    {
        MemberAccessExpressionSyntax m => m.Name.Identifier.Text,
        IdentifierNameSyntax i => i.Identifier.Text,
        GenericNameSyntax g => g.Identifier.Text,
        _ => null,
    };

    private static int Line(SyntaxNode n) => n.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

    /// <summary>"Type.Member" of the outermost type declaring the code (records/classes) and the member containing it.</summary>
    private static string Key(SyntaxNode n)
    {
        var type = n.Ancestors().OfType<BaseTypeDeclarationSyntax>().LastOrDefault()?.Identifier.Text ?? "?";
        var member = n.Ancestors().FirstOrDefault(a => a is MemberDeclarationSyntax and not BaseTypeDeclarationSyntax) switch
        {
            MethodDeclarationSyntax m => m.Identifier.Text,
            PropertyDeclarationSyntax p => p.Identifier.Text,
            ConstructorDeclarationSyntax => "ctor",
            FieldDeclarationSyntax f => f.Declaration.Variables[0].Identifier.Text,
            _ => "?",
        };
        return type + "." + member;
    }

    /// <summary>The filter names an IgnoreQueryFilters call lifts: none named = all ("*"); names it cannot resolve count as all.</summary>
    private static HashSet<string> Lifted(InvocationExpressionSyntax call, SyntaxNode root)
    {
        if (call.ArgumentList.Arguments.Count == 0) return ["*"];
        var names = Names(call.ArgumentList.Arguments[0].Expression, root, 0);
        return names ?? ["*"];
    }

    private static HashSet<string>? Names(ExpressionSyntax e, SyntaxNode root, int depth)
    {
        IEnumerable<ExpressionSyntax>? items = e switch
        {
            CollectionExpressionSyntax c => c.Elements.OfType<ExpressionElementSyntax>().Select(x => x.Expression),
            ArrayCreationExpressionSyntax a => a.Initializer?.Expressions,
            ImplicitArrayCreationExpressionSyntax a => a.Initializer.Expressions,
            _ => null,
        };
        if (items is not null)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in items)
            {
                var one = One(item);
                if (one is null) return null;
                set.Add(one);
            }
            return set;
        }
        // A field or local of the same file holding the names (e.g. private static readonly string[] TenantFilter = ["Tenant"]).
        if (e is IdentifierNameSyntax id && depth < 3)
        {
            var init = root.DescendantNodes().OfType<VariableDeclaratorSyntax>().Where(v => v.Identifier.Text == id.Identifier.Text)
                .Select(v => v.Initializer?.Value).FirstOrDefault(v => v is not null);
            return init is null ? null : Names(init, root, depth + 1);
        }
        return null;
    }

    private static string? One(ExpressionSyntax e) => e switch
    {
        LiteralExpressionSyntax l when l.IsKind(SyntaxKind.StringLiteralExpression) => l.Token.ValueText,
        MemberAccessExpressionSyntax m when m.Name.Identifier.Text is "SoftDelete" or "Tenant" or "Company" => m.Name.Identifier.Text,
        _ => null,
    };

    /// <summary>
    /// True when the call sits inside an argument or lambda of another query whose own chain does not lift at least the same filters: EF Core
    /// applies IgnoreQueryFilters to the whole query, so the outer query silently loses its filters (SA-1).
    /// </summary>
    private static bool IsNestedLift(InvocationExpressionSyntax call, HashSet<string> lifted, SyntaxNode root)
    {
        for (SyntaxNode? n = call.Parent; n is not null; n = n.Parent)
        {
            if (n is StatementSyntax or MemberDeclarationSyntax) return false;
            if (n is not ArgumentSyntax arg) continue;
            if (arg.Parent?.Parent is not InvocationExpressionSyntax outer) continue;
            // The outer chain: walk down from the invocation owning the argument to its root.
            var chain = new List<InvocationExpressionSyntax>();
            ExpressionSyntax? e = outer;
            while (e is not null)
            {
                if (e is InvocationExpressionSyntax inv) { chain.Add(inv); e = inv.Expression; }
                else if (e is MemberAccessExpressionSyntax ma) e = ma.Expression;
                else if (e is ParenthesizedExpressionSyntax p) e = p.Expression;
                else if (e is AwaitExpressionSyntax aw) e = aw.Expression;
                else break;
            }
            if (!chain.Any(c => Name(c.Expression) == "Set")) continue;   // not a query (e.g. a dictionary or Task call): look further out
            var outerLift = chain.Where(c => Name(c.Expression) == "IgnoreQueryFilters").SelectMany(c => Lifted(c, root)).ToHashSet(StringComparer.Ordinal);
            return !(outerLift.Contains("*") || (!lifted.Contains("*") && lifted.IsSubsetOf(outerLift)));
        }
        return false;
    }
}
