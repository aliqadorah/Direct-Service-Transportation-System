using System.Globalization;

namespace DirectService.Transportation.Contracts.Errors;

/// <summary>
/// Problem codes (API-06) with Arabic and English message templates. Placeholders are {0}, {1}…
/// The catalogue grows per module; every code used by the server must be listed here
/// (a unit test enforces it).
/// </summary>
public static class ErrorCatalog
{
    private static readonly Dictionary<string, (string Ar, string En)> Messages = new(StringComparer.Ordinal)
    {
        [ErrorCodes.ValidationRequired] = ("الحقل «{0}» مطلوب.", "Field «{0}» is required."),
        [ErrorCodes.ValidationFormat] = ("قيمة الحقل «{0}» غير صحيحة.", "Field «{0}» has an invalid format."),
        [ErrorCodes.ValidationRange] = ("قيمة الحقل «{0}» خارج النطاق المسموح.", "Field «{0}» is out of the allowed range."),
        [ErrorCodes.ValidationFailed] = ("يرجى تصحيح الحقول المشار إليها.", "Please correct the highlighted fields."),
        [ErrorCodes.Unauthenticated] = ("يرجى تسجيل الدخول.", "Please sign in."),
        [ErrorCodes.Forbidden] = ("ليست لديك صلاحية للقيام بهذا الإجراء.", "You do not have permission to do this."),
        [ErrorCodes.NotFound] = ("غير موجود.", "Not found."),
        [ErrorCodes.TenantReadOnly] = ("اشتراككم موقوف؛ يمكنكم العرض والطباعة والتصدير فقط.", "Your subscription is suspended; you can view, print and export only."),
        [ErrorCodes.CompanyArchived] = ("هذه الشركة مؤرشفة وللقراءة فقط.", "This company is archived and read-only."),
        [ErrorCodes.SupportReadOnly] = ("جلسة الدعم للقراءة فقط.", "Support access is read-only."),
        [ErrorCodes.ConcurrencyConflict] = ("عدّل مستخدم آخر هذا السجل. أعد تحميل الصفحة لرؤية آخر نسخة.", "This record was changed by another user. Reload to see the latest version."),
        [ErrorCodes.DuplicateKey] = ("القيمة «{0}» موجودة مسبقاً.", "«{0}» already exists."),
        [ErrorCodes.PlanLimitReached] = ("وصلتم إلى حدّ خطتكم ({0}). قوموا بالترقية لإضافة المزيد.", "Your plan limit has been reached ({0}). Upgrade to add more."),
        [ErrorCodes.LockHeld] = ("{0} يعدّل هذا السجل الآن.", "{0} is editing this record."),
        [ErrorCodes.PeriodClosed] = ("التاريخ {0} يقع في فترة مغلقة ({1}).", "{0} is in the closed period {1}."),
        [ErrorCodes.RateLimited] = ("طلبات كثيرة. حاول مرة أخرى بعد قليل.", "Too many requests. Try again shortly."),
        [ErrorCodes.SignInFailed] = ("البريد الإلكتروني أو كلمة المرور غير صحيحة.", "The email or password is incorrect."),
        [ErrorCodes.SignInLocked] = ("تم قفل الحساب مؤقتاً بسبب محاولات فاشلة. حاول بعد {0} دقيقة.", "The account is locked after failed attempts. Try again in {0} minutes."),
        [ErrorCodes.EmailNotConfirmed] = ("يرجى تأكيد بريدك الإلكتروني أولاً.", "Please verify your email address first."),
        [ErrorCodes.TwoFactorInvalid] = ("رمز التحقق غير صحيح.", "The verification code is incorrect."),
        [ErrorCodes.TwoFactorRequired] = ("يلزم إكمال التحقق بخطوتين.", "Two-factor authentication must be completed."),
        [ErrorCodes.PasswordPolicy] = ("كلمة المرور لا تحقق سياسة جهتكم: {0} أحرف على الأقل، مع الحروف والأرقام المطلوبة.", "The password does not meet your organisation's policy: at least {0} characters, with the required letters and digits."),
        [ErrorCodes.TokenInvalid] = ("الرابط غير صالح أو منتهي الصلاحية.", "The link is invalid or has expired."),
        [ErrorCodes.EmailInUse] = ("هذا البريد الإلكتروني مستخدم لحساب آخر.", "This email address is already used by another account."),
        [ErrorCodes.LastOwner] = ("يجب أن يبقى مالك نشط واحد على الأقل للمشترك.", "The tenant must keep at least one active Owner."),
        [ErrorCodes.LastUserManager] = ("يجب أن يبقى في كل شركة مستخدم نشط واحد على الأقل يملك صلاحية إدارة المستخدمين.", "Each company must keep at least one active user who can manage users."),
        [ErrorCodes.PresetRoleFixed] = ("الأدوار الجاهزة لا يمكن تعديلها أو حذفها.", "Preset roles cannot be changed or deleted."),
        [ErrorCodes.RoleInUse] = ("الدور مستخدم من قبل {0} مستخدم. أعد تعيينهم أولاً.", "The role is used by {0} user(s). Reassign them first."),
        [ErrorCodes.LastActiveCompany] = ("يجب أن تبقى شركة نشطة واحدة على الأقل.", "At least one active company must remain."),
        [ErrorCodes.SequenceBelowIssued] = ("الرقم التالي لا يمكن أن يكون أقل من أو يساوي رقماً صادراً ({0}).", "The next number cannot be at or below an issued number ({0})."),
        [ErrorCodes.MigrationAlreadyComplete] = ("تم تحديد اكتمال ترحيل البيانات مسبقاً.", "Data migration is already marked complete."),
        [ErrorCodes.ConfirmationRequired] = ("يرجى تأكيد الإجراء: {0}", "Please confirm the action: {0}"),
        [ErrorCodes.TenantUnavailable] = ("الاشتراك غير متاح.", "The subscription is not available."),
        [ErrorCodes.InvitationEmailMismatch] = ("سجّل الدخول بالحساب الذي وُجّهت إليه الدعوة.", "Sign in with the account the invitation was sent to."),
        [ErrorCodes.ReasonRequired] = ("يرجى ذكر السبب.", "Please give a reason."),
        [ErrorCodes.Antiforgery] = ("انتهت صلاحية الصفحة. أعد تحميلها وحاول مرة أخرى.", "The page has expired. Reload it and try again."),
        [ErrorCodes.ServerError] = ("حدث خطأ غير متوقع. رقم المتابعة: {0}", "An unexpected error occurred. Reference: {0}"),
    };

    /// <summary>Formats the message of a code in the given language ("ar" or "en").</summary>
    public static string Format(string code, string language, params object?[] args)
    {
        if (!Messages.TryGetValue(code, out var m))
            return code;
        var template = string.Equals(language, "en", StringComparison.OrdinalIgnoreCase) ? m.En : m.Ar;
        return args.Length == 0 ? template : string.Format(CultureInfo.InvariantCulture, template, args);
    }

    public static bool IsKnown(string code) => Messages.ContainsKey(code);

    public static IEnumerable<string> Codes => Messages.Keys;
}

/// <summary>Problem codes returned in the problem-details "code" field.</summary>
public static class ErrorCodes
{
    public const string ValidationRequired = "validation.required";
    public const string ValidationFormat = "validation.format";
    public const string ValidationRange = "validation.range";
    public const string ValidationFailed = "validation.failed";
    public const string Unauthenticated = "auth.unauthenticated";
    public const string Forbidden = "auth.forbidden";
    public const string NotFound = "notFound";
    public const string TenantReadOnly = "tenant.readOnly";
    public const string CompanyArchived = "company.archived";
    public const string SupportReadOnly = "support.readOnly";
    public const string ConcurrencyConflict = "concurrency.conflict";
    public const string DuplicateKey = "duplicate.key";
    public const string PlanLimitReached = "plan.limitReached";
    public const string LockHeld = "lock.held";
    public const string PeriodClosed = "period.closed";
    public const string RateLimited = "rateLimited";
    public const string SignInFailed = "auth.signInFailed";
    public const string SignInLocked = "auth.locked";
    public const string EmailNotConfirmed = "auth.emailNotConfirmed";
    public const string TwoFactorInvalid = "auth.twoFactorInvalid";
    public const string TwoFactorRequired = "auth.twoFactorRequired";
    public const string PasswordPolicy = "auth.passwordPolicy";
    public const string TokenInvalid = "auth.tokenInvalid";
    public const string EmailInUse = "user.emailInUse";
    public const string LastOwner = "user.lastOwner";
    public const string LastUserManager = "user.lastUserManager";
    public const string PresetRoleFixed = "role.presetFixed";
    public const string RoleInUse = "role.inUse";
    public const string LastActiveCompany = "company.lastActive";
    public const string SequenceBelowIssued = "numbering.belowIssued";
    public const string MigrationAlreadyComplete = "settings.migrationComplete";
    public const string ConfirmationRequired = "confirmation.required";
    public const string TenantUnavailable = "tenant.unavailable";
    public const string InvitationEmailMismatch = "invitation.emailMismatch";
    public const string ReasonRequired = "validation.reasonRequired";
    public const string Antiforgery = "auth.antiforgery";
    public const string ServerError = "server.error";
}
