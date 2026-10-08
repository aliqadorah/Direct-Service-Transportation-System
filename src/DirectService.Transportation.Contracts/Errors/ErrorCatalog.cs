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

        // Accounting core and posting engine (B2)
        [ErrorCodes.RecordInUse] = ("لا يمكن الحذف لأن السجل مستخدم في: {0}. يمكنك أرشفته أو إيقافه بدلاً من ذلك.", "It cannot be deleted because it is used by: {0}. Archive or deactivate it instead."),
        [ErrorCodes.RecordArchived] = ("«{0}» مؤرشف ولا يمكن اختياره في عمليات جديدة.", "«{0}» is archived and cannot be chosen on new entries."),
        [ErrorCodes.AccountHasChildren] = ("الحساب «{0}» له حسابات فرعية؛ الحسابات الفرعية فقط تقبل القيود.", "Account «{0}» has sub-accounts; only leaf accounts accept entries."),
        [ErrorCodes.AccountParentInUse] = ("لا يمكن إضافة حساب فرعي تحت «{0}» لأنه يحمل قيوداً أو أرصدة افتتاحية أو ربطاً.", "A sub-account cannot be added under «{0}» because it carries entries, opening balances or links."),
        [ErrorCodes.AccountTypeMismatch] = ("نوع الحساب يجب أن يطابق نوع الحساب الأب ({0}).", "The account type must match its parent's type ({0})."),
        [ErrorCodes.AccountCycle] = ("لا يمكن نقل الحساب تحت أحد حساباته الفرعية.", "An account cannot be moved under one of its own sub-accounts."),
        [ErrorCodes.AccountMapped] = ("الحساب «{0}» مربوط بمفتاح ترحيل أو صندوق أو حساب بنكي ولا يمكن إيقافه.", "Account «{0}» is linked to a posting key, cash box or bank account and cannot be deactivated."),
        [ErrorCodes.AccountActiveChildren] = ("الحساب «{0}» له حسابات فرعية نشطة؛ أوقفها أولاً.", "Account «{0}» has active sub-accounts; deactivate them first."),
        [ErrorCodes.AccountNotPostable] = ("الحساب «{0}» غير قابل للترحيل أو غير نشط.", "Account «{0}» is not postable or not active."),
        [ErrorCodes.AccountNotAsset] = ("يجب أن يكون حساب الصندوق أو البنك حساب أصول قابلاً للترحيل.", "A cash box or bank account must use a postable asset account."),
        [ErrorCodes.AccountLinkTaken] = ("الحساب «{0}» مربوط مسبقاً بصندوق أو حساب بنكي آخر.", "Account «{0}» is already linked to another cash box or bank account."),
        [ErrorCodes.ControlPartyRequired] = ("السطر {0}: الحساب «{1}» حساب رقابة ويجب تحديد طرف من نوع {2}.", "Line {0}: account «{1}» is a control account and needs a party of type {2}."),
        [ErrorCodes.ControlPartyMismatch] = ("السطر {0}: نوع الطرف لا يطابق نوع طرف حساب الرقابة «{1}» ({2}).", "Line {0}: the party does not match the control account «{1}» party type ({2})."),
        [ErrorCodes.ChartNotEmpty] = ("يمكن إنشاء الدليل من النموذج فقط عندما يكون دليل الحسابات فارغاً.", "The template can only be used while the chart of accounts is empty."),
        [ErrorCodes.JournalUnbalanced] = ("القيد غير متوازن: مدين {0} ودائن {1}.", "The entry does not balance: debit {0}, credit {1}."),
        [ErrorCodes.JournalLineAmount] = ("السطر {0}: أدخل مبلغاً مديناً أو دائناً واحداً أكبر من صفر.", "Line {0}: enter one debit or one credit amount greater than zero."),
        [ErrorCodes.JournalMinLines] = ("القيد يحتاج سطرين على الأقل.", "An entry needs at least two lines."),
        [ErrorCodes.JournalNotManual] = ("القيود الآلية تتغير فقط من خلال المستند المصدر.", "Automatic entries change only through their source document."),
        [ErrorCodes.JournalNotPosted] = ("القيد {0} معكوس أو ملغى مسبقاً.", "Entry {0} is already reversed or void."),
        [ErrorCodes.PostingKeysMissing] = ("الترحيل والفوترة معطّلان حتى تُربط مفاتيح الترحيل: {0}.", "Posting and invoicing are disabled until these posting keys are mapped: {0}."),
        [ErrorCodes.OpeningUnbalanced] = ("الأرصدة الافتتاحية غير متوازنة (الفرق {0}). بعد انتهاء الإعداد يجب أن تتوازن.", "The opening balances do not balance (difference {0}). After setup they must balance."),
        [ErrorCodes.OpeningEmpty] = ("لا توجد أرصدة افتتاحية للترحيل.", "There are no opening balances to post."),
        [ErrorCodes.PeriodAlreadyClosed] = ("الفترة {0} مغلقة مسبقاً.", "Period {0} is already closed."),
        [ErrorCodes.PeriodNotClosed] = ("الفترة {0} مفتوحة.", "Period {0} is open."),
        [ErrorCodes.PeriodChecksPending] = ("فحص ما قبل الإغلاق: {0} ترحيل معلّق، {1} مسودة غير معتمدة، {2} حساب رقابة غير مطابق. أكّد للإغلاق.", "Pre-close check: {0} pending posting(s), {1} unapproved draft(s), {2} unreconciled control account(s). Confirm to close."),
        [ErrorCodes.ImportFileType] = ("نوع الملف غير مدعوم. استخدم ملف Excel (.xlsx) أو CSV.", "Unsupported file type. Use an Excel (.xlsx) or CSV file."),
        [ErrorCodes.ImportTooLarge] = ("الملف كبير جداً (الحد {0}).", "The file is too large (limit {0})."),
        [ErrorCodes.ImportEmpty] = ("الملف لا يحتوي على صفوف.", "The file has no rows."),
        [ErrorCodes.ImportMissingColumn] = ("العمود «{0}» مطلوب في الملف.", "Column «{0}» is required in the file."),
        [ErrorCodes.ImportHasErrors] = ("في الملف {0} صف مرفوض. صحّحها ثم أعد الاستيراد.", "{0} row(s) of the file are rejected. Correct them and import again."),
        [ErrorCodes.ImportFormula] = ("الخلية {0} تحتوي معادلة؛ أدخل القيم فقط.", "Cell {0} contains a formula; enter values only."),
        [ErrorCodes.ImportDuplicate] = ("القيمة «{0}» مكررة.", "«{0}» is duplicated."),
        [ErrorCodes.ImportUnknown] = ("«{0}» غير معروف: {1}.", "Unknown {0}: «{1}»."),
        [ErrorCodes.ImportInvalid] = ("قيمة «{0}» غير صحيحة: {1}.", "Invalid {0}: «{1}»."),
        [ErrorCodes.JournalIsReversal] = ("القيد {0} قيد عكسي لقيد آخر ولا يمكن تعديله أو إلغاؤه.", "Entry {0} reverses another entry and cannot be edited or voided."),
        [ErrorCodes.PendingNoHandler] = ("لا يمكن إعادة ترحيل هذا النوع من المستندات في هذا الإصدار.", "This document type cannot be re-posted in this version."),
    };

    /// <summary>Formats the message of a code in the given language ("ar" or "en").</summary>
    public static string Format(string code, string language, params object?[] args)
    {
        if (!Messages.TryGetValue(code, out var m))
            return code;
        var template = string.Equals(language, "en", StringComparison.OrdinalIgnoreCase) ? m.En : m.Ar;
        if (args.Length == 0) return template;
        // Missing arguments render as empty text instead of failing (messages take up to 5 placeholders).
        var padded = args.Length >= 5 ? args : [.. args, .. Enumerable.Repeat<object?>(string.Empty, 5 - args.Length)];
        return string.Format(CultureInfo.InvariantCulture, template, padded);
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

    public const string RecordInUse = "record.inUse";
    public const string RecordArchived = "record.archived";
    public const string AccountHasChildren = "account.hasChildren";
    public const string AccountParentInUse = "account.parentInUse";
    public const string AccountTypeMismatch = "account.typeMismatch";
    public const string AccountCycle = "account.cycle";
    public const string AccountMapped = "account.linked";
    public const string AccountActiveChildren = "account.activeChildren";
    public const string AccountNotPostable = "account.notPostable";
    public const string AccountNotAsset = "account.notAsset";
    public const string AccountLinkTaken = "account.linkTaken";
    public const string ControlPartyRequired = "journal.partyRequired";
    public const string ControlPartyMismatch = "journal.partyMismatch";
    public const string ChartNotEmpty = "chart.notEmpty";
    public const string JournalUnbalanced = "journal.unbalanced";
    public const string JournalLineAmount = "journal.lineAmount";
    public const string JournalMinLines = "journal.minLines";
    public const string JournalNotManual = "journal.notManual";
    public const string JournalNotPosted = "journal.notPosted";
    public const string PostingKeysMissing = "posting.keysMissing";
    public const string OpeningUnbalanced = "opening.unbalanced";
    public const string OpeningEmpty = "opening.empty";
    public const string PeriodAlreadyClosed = "period.alreadyClosed";
    public const string PeriodNotClosed = "period.notClosed";
    public const string PeriodChecksPending = "period.checksPending";
    public const string ImportFileType = "import.fileType";
    public const string ImportTooLarge = "import.tooLarge";
    public const string ImportEmpty = "import.empty";
    public const string ImportMissingColumn = "import.missingColumn";
    public const string ImportHasErrors = "import.hasErrors";
    public const string ImportFormula = "import.formula";
    public const string ImportDuplicate = "import.duplicate";
    public const string ImportUnknown = "import.unknown";
    public const string ImportInvalid = "import.invalid";
    public const string PendingNoHandler = "pending.noHandler";
    public const string JournalIsReversal = "journal.isReversal";
}
