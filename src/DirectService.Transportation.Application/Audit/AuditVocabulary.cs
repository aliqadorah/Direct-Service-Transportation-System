namespace DirectService.Transportation.Application.Audit;

/// <summary>An Arabic / English pair of the vocabulary.</summary>
public readonly record struct Words(string Ar, string En)
{
    public string In(bool en) => en ? En : Ar;
}

/// <summary>
/// The words of the readable history and audit log (owner feedback 14 Oct 2026, DT-188): record types, actions, field labels and stored
/// codes in the reader's language, with the same labels the screens use. Presentation only — what the audit log stores is unchanged.
/// </summary>
public static class AuditVocabulary
{
    // ------------------------------------------------------------ record types

    /// <summary>A record type: its title (record-type column, filter) and the noun used in a sentence ("the trip", "الرحلة").</summary>
    public sealed record EntityWords(Words Title, Words Noun);

    private static EntityWords E(string titleAr, string titleEn, string nounAr, string nounEn) => new(new(titleAr, titleEn), new(nounAr, nounEn));

    public static readonly IReadOnlyDictionary<string, EntityWords> Entities = new Dictionary<string, EntityWords>(StringComparer.Ordinal)
    {
        ["TRN.Trip"] = E("رحلة", "Trip", "الرحلة", "the trip"),
        ["TRN.Invoice"] = E("فاتورة", "Invoice", "الفاتورة", "invoice"),
        ["TRN.InvoiceLine"] = E("سطر فاتورة", "Invoice line", "سطر الفاتورة", "the invoice line"),
        ["TRN.InvoiceLayout"] = E("نموذج الفاتورة", "Invoice layout", "نموذج الفاتورة", "the invoice layout"),
        ["TRN.CreditNote"] = E("إشعار دائن", "Credit note", "الإشعار الدائن", "credit note"),
        ["TRN.CreditNoteLine"] = E("سطر إشعار دائن", "Credit note line", "سطر الإشعار الدائن", "the credit note line"),
        ["TRN.Receipt"] = E("سند قبض", "Receipt", "سند القبض", "receipt"),
        ["TRN.Advance"] = E("سلفة سائق", "Driver advance", "السلفة", "advance"),
        ["TRN.Settlement"] = E("تسوية سائق", "Driver settlement", "تسوية السائق", "driver settlement"),
        ["TRN.SettlementDeduction"] = E("خصم تسوية", "Settlement deduction", "خصم التسوية", "the settlement deduction"),
        ["TRN.PartnerPayment"] = E("دفعة شريك", "Partner payment", "دفعة الشريك", "partner payment"),
        ["TRN.Driver"] = E("سائق", "Driver", "السائق", "driver"),
        ["TRN.Customer"] = E("عميل", "Customer", "العميل", "customer"),
        ["TRN.Partner"] = E("شريك", "Partner", "الشريك", "partner"),
        ["TRN.Material"] = E("مادة", "Material", "المادة", "material"),
        ["TRN.LoadingSite"] = E("موقع تحميل", "Loading site", "موقع التحميل", "loading site"),
        ["TRN.UnloadingSite"] = E("موقع تنزيل", "Unloading site", "موقع التنزيل", "unloading site"),
        ["TRN.OwnershipType"] = E("نوع ملكية", "Ownership type", "نوع الملكية", "ownership type"),
        ["TRN.Tariff"] = E("تعرفة", "Tariff", "التعرفة", "the tariff"),
        ["TRN.MonthlySiteRate"] = E("سعر شهري لموقع", "Monthly site rate", "السعر الشهري", "the monthly rate"),
        ["TRN.EInvoiceSubmission"] = E("إرسال فاتورة إلكترونية", "E-invoice submission", "الإرسال الإلكتروني", "the e-invoice submission"),
        ["ACC.Account"] = E("حساب", "Account", "الحساب", "account"),
        ["ACC.JournalEntry"] = E("قيد يومية", "Journal entry", "القيد", "journal entry"),
        ["TRN.ReceiptAllocation"] = E("تخصيص سند قبض", "Receipt allocation", "التخصيص", "the receipt allocation"),
        ["ACC.JournalDraft"] = E("مسودة قيد", "Journal draft", "مسودة القيد", "journal draft"),
        ["ACC.EntryLine"] = E("سطر قيد", "Journal line", "سطر القيد", "the journal line"),
        ["ACC.CashBox"] = E("صندوق", "Cash box", "الصندوق", "cash box"),
        ["ACC.BankAccount"] = E("حساب بنكي", "Bank account", "الحساب البنكي", "bank account"),
        ["ACC.CostCentre"] = E("مركز تكلفة", "Cost centre", "مركز التكلفة", "cost centre"),
        ["ACC.JournalTemplate"] = E("نموذج قيد", "Journal template", "نموذج القيد", "journal template"),
        ["ACC.Budget"] = E("موازنة", "Budget", "الموازنة", "budget"),
        ["ACC.BudgetLine"] = E("سطر موازنة", "Budget line", "سطر الموازنة", "the budget line"),
        ["ACC.Period"] = E("فترة محاسبية", "Accounting period", "الفترة", "period"),
        ["ACC.PostingKeyMapping"] = E("ربط حساب", "Account mapping", "ربط الحساب", "the account mapping"),
        ["ACC.PendingPosting"] = E("قيد معلّق", "Pending posting", "القيد المعلّق", "the pending posting"),
        ["ACC.JournalExportBatch"] = E("دفعة تصدير قيود", "Journal export", "دفعة التصدير", "journal export"),
        ["ACC.ApArParty"] = E("طرف محاسبي", "Accounting party", "الطرف", "party"),
        ["ACC.ApArDocument"] = E("مستند ذمم", "Payable/receivable document", "المستند", "document"),
        ["ACC.ApArDocumentLine"] = E("سطر مستند ذمم", "Document line", "سطر المستند", "the document line"),
        ["ACC.ApArPayment"] = E("دفعة ذمم", "Payable/receivable payment", "الدفعة", "payment"),
        ["ACC.ApArReturn"] = E("مرتجع ذمم", "Payable/receivable return", "المرتجع", "return"),
        ["ACC.ApArEInvoiceSubmission"] = E("إرسال فاتورة إلكترونية", "E-invoice submission", "الإرسال الإلكتروني", "the e-invoice submission"),
        ["CFG.CompanySetting"] = E("إعداد الشركة", "Company setting", "الإعداد", "the setting"),
        ["CFG.CustomFieldDefinition"] = E("حقل مخصص", "Custom field", "الحقل المخصص", "custom field"),
        ["CFG.StandardFieldConfig"] = E("تهيئة حقل", "Field configuration", "تهيئة الحقل", "the field configuration of"),
        ["CFG.NumberSequence"] = E("ترقيم", "Numbering", "الترقيم", "the numbering of"),
        ["CFG.EInvoiceSettings"] = E("إعدادات الفوترة الإلكترونية", "E-invoicing settings", "إعدادات الفوترة الإلكترونية", "the e-invoicing settings"),
        ["CFG.SavedReport"] = E("تقرير محفوظ", "Saved report", "التقرير", "report"),
        ["CFG.DashboardPin"] = E("تقرير مثبّت", "Pinned report", "التقرير المثبّت", "the pinned report"),
        ["CFG.ImportBatch"] = E("استيراد", "Import", "الاستيراد", "the import"),
        ["CFG.TableColumnView"] = E("أعمدة جدول", "Column view", "أعمدة الجدول", "the column view"),
        ["CORE.User"] = E("مستخدم", "User", "المستخدم", "user"),
        ["CORE.Role"] = E("دور", "Role", "الدور", "role"),
        ["CORE.RolePermission"] = E("صلاحية دور", "Role permission", "صلاحية الدور", "a permission of role"),
        ["CORE.UserCompanyRole"] = E("دور مستخدم", "User role", "دور المستخدم", "the role of"),
        ["CORE.Invitation"] = E("دعوة", "Invitation", "الدعوة", "the invitation of"),
        ["CORE.UserRecoveryCode"] = E("رموز الاسترداد", "Recovery codes", "رمز الاسترداد", "a recovery code of"),
        ["CORE.Company"] = E("شركة", "Company", "الشركة", "company"),
        ["CORE.Tenant"] = E("المشترك", "Tenant", "المشترك", "the tenant"),
        ["CORE.Plan"] = E("خطة", "Plan", "الخطة", "plan"),
        ["CORE.SubscriptionInvoice"] = E("فاتورة اشتراك", "Subscription invoice", "فاتورة الاشتراك", "subscription invoice"),
        ["CORE.SupportAccessGrant"] = E("وصول الدعم", "Support access", "طلب وصول الدعم", "the support access request"),
        ["CORE.Announcement"] = E("إعلان", "Announcement", "الإعلان", "announcement"),
        ["CORE.TermsAcceptance"] = E("قبول الشروط", "Terms acceptance", "قبول الشروط", "the terms acceptance"),
        ["CORE.MaintenanceLog"] = E("منطقة الصيانة", "Maintenance", "عملية الصيانة", "maintenance"),
    };

    /// <summary>The words of a record type; an unknown type shows its last part ("Widget").</summary>
    public static EntityWords Entity(string? type)
    {
        if (type is not null && Entities.TryGetValue(type, out var w)) return w;
        var name = type?.Split('.').LastOrDefault() ?? "";
        return E(name, name, name, name);
    }

    // ------------------------------------------------------------ actions

    /// <summary>
    /// An action: its short label (action column, filter) and the sentence. {r} is the record ("the trip R-1001"), {d} the document the
    /// action names ("draft invoice INV-00001"), {x} the extra detail (a reason, a change of name). The Arabic sentence is a verbal noun, so
    /// it never guesses the gender of the user who acted.
    /// </summary>
    public sealed record ActionWords(Words Label, Words Sentence);

    private static ActionWords A(string labelAr, string labelEn, string ar, string en) => new(new(labelAr, labelEn), new(ar, en));

    public static readonly IReadOnlyDictionary<string, ActionWords> Actions = new Dictionary<string, ActionWords>(StringComparer.Ordinal)
    {
        ["Create"] = A("إنشاء", "Created", "إنشاء {r}", "created {r}"),
        ["Update"] = A("تعديل", "Edited", "تعديل {r}", "edited {r}"),
        ["Delete"] = A("حذف", "Deleted", "حذف {r}", "deleted {r}"),
        ["Archive"] = A("أرشفة", "Archived", "أرشفة {r}", "archived {r}"),
        ["Restore"] = A("استعادة", "Restored", "استعادة {r}", "restored {r}"),
        ["Void"] = A("إلغاء", "Voided", "إلغاء {r}", "voided {r}"),
        ["Print"] = A("طباعة", "Printed", "طباعة {r}", "printed {r}"),
        ["Run"] = A("تشغيل", "Ran", "تشغيل {r}", "ran {r}"),
        ["Export"] = A("تصدير", "Exported", "تصدير {r}", "exported {r}"),
        ["Import"] = A("استيراد", "Imported", "استيراد {r}", "imported {r}"),
        ["Reorder"] = A("إعادة ترتيب", "Reordered", "إعادة ترتيب {r}", "reordered {r}"),
        ["Clear"] = A("مسح", "Cleared", "مسح {r}", "cleared {r}"),
        ["Reviewed"] = A("مراجعة", "Reviewed", "اعتماد مراجعة {r}", "marked {r} as reviewed"),
        // Trips on invoices, settlements and credit notes
        ["AddedToInvoice"] = A("إضافة إلى فاتورة", "Added to invoice", "إضافة {r} إلى {d}", "added {r} to {d}"),
        ["RemovedFromInvoice"] = A("إزالة من فاتورة", "Removed from invoice", "إزالة {r} من {d}", "removed {r} from {d}"),
        ["Invoiced"] = A("فوترة", "Invoiced", "فوترة {r} في {d}", "invoiced {r} on {d}"),
        ["Settled"] = A("تسوية", "Settled", "تسوية {r} ضمن {d}", "settled {r} in {d}"),
        ["SettlementVoided"] = A("إلغاء تسوية", "Settlement voided", "إلغاء {d} التي شملت {r}", "voided {d}, which included {r}"),
        ["ReturnedByCreditNote"] = A("إرجاع بإشعار دائن", "Returned by credit note", "إرجاع {r} بموجب {d}", "returned {r} by {d}"),
        // Invoices and documents
        ["Approve"] = A("اعتماد", "Approved", "اعتماد {r}", "approved {r}"),
        ["CreditNote"] = A("إشعار دائن", "Credit note", "إصدار {d} على {r}", "issued {d} on {r}"),
        ["CreditNoteVoided"] = A("إلغاء إشعار دائن", "Credit note voided", "إلغاء {d} على {r}", "voided {d} on {r}"),
        ["Receipt"] = A("تحصيل", "Payment received", "تحصيل {d} على {r}", "received {d} on {r}"),
        ["ReceiptVoid"] = A("إلغاء تحصيل", "Receipt voided", "إلغاء {d} على {r}", "voided {d} on {r}"),
        ["Payment"] = A("دفعة", "Payment", "تسجيل {d} على {r}", "recorded {d} on {r}"),
        ["PaymentVoid"] = A("إلغاء دفعة", "Payment voided", "إلغاء {d} على {r}", "voided {d} on {r}"),
        ["Return"] = A("مرتجع", "Return", "تسجيل {d} على {r}", "recorded {d} on {r}"),
        ["ReturnVoid"] = A("إلغاء مرتجع", "Return voided", "إلغاء {d} على {r}", "voided {d} on {r}"),
        // Journal entries
        ["Post"] = A("ترحيل", "Posted", "ترحيل {r}{x}", "posted {r}{x}"),
        ["Edit"] = A("تعديل قيد", "Entry edited", "ترحيل {r} بدلاً من {d}", "posted {r} replacing {d}"),
        ["Reverse"] = A("عكس", "Reversed", "عكس {r} بالقيد {d}", "reversed {r} by {d}"),
        ["Reversal"] = A("قيد عكسي", "Reversal", "ترحيل القيد العكسي {r}", "posted reversal {r}"),
        ["OpeningBalance"] = A("رصيد افتتاحي", "Opening balance", "تعديل الرصيد الافتتاحي لـ{r}", "changed the opening balance of {r}"),
        ["Move"] = A("نقل", "Moved", "نقل {r}", "moved {r}"),
        ["Activate"] = A("تفعيل", "Activated", "تفعيل {r}", "activated {r}"),
        ["Deactivate"] = A("إيقاف", "Deactivated", "إيقاف {r}", "deactivated {r}"),
        ["Template"] = A("دليل جاهز", "From template", "إنشاء {r} من الدليل الجاهز", "created {r} from the template"),
        ["MappingChange"] = A("ربط حساب", "Mapping changed", "تغيير {r}", "changed {r}"),
        ["Close"] = A("إغلاق", "Closed", "إغلاق {r}", "closed {r}"),
        ["Reopen"] = A("إعادة فتح", "Reopened", "إعادة فتح {r}", "reopened {r}"),
        ["LinkApArParty"] = A("ربط طرف محاسبي", "Party linked", "ربط {r} بالطرف المحاسبي {d}", "linked {r} to accounting party {d}"),
        // Settings
        ["SettingChange"] = A("تغيير إعداد", "Setting changed", "تغيير {r}", "changed {r}"),
        ["MigrationComplete"] = A("اكتمال الترحيل", "Migration completed", "اكتمال ترحيل بيانات {r}", "completed the data migration of {r}"),
        ["SetupDone"] = A("إتمام خطوة إعداد", "Setup step done", "إتمام خطوة الإعداد {x}", "completed setup step {x}"),
        ["SetupReopened"] = A("إعادة فتح خطوة إعداد", "Setup step reopened", "إعادة فتح خطوة الإعداد {x}", "reopened setup step {x}"),
        // E-invoicing
        ["Cancel"] = A("إلغاء", "Cancelled", "إلغاء {r}", "cancelled {r}"),
        ["Resubmit"] = A("إعادة إرسال", "Resubmitted", "إعادة إرسال {r}", "resubmitted {r}"),
        ["Accepted"] = A("قبول", "Accepted", "قبول {r} بالرقم {x}", "{r} was accepted as {x}"),
        ["Rejected"] = A("رفض", "Rejected", "رفض {r}", "{r} was rejected"),
        ["Failed"] = A("فشل", "Failed", "فشل إرسال {r}", "sending {r} failed"),
        // Users and access
        ["SignIn"] = A("تسجيل دخول", "Signed in", "تسجيل الدخول", "signed in"),
        ["SignInFailed"] = A("محاولة دخول فاشلة", "Failed sign-in", "محاولة دخول فاشلة لـ{r}", "failed sign-in for {r}"),
        ["SignInLocked"] = A("قفل الدخول", "Sign-in locked", "قفل الدخول لـ{r} بعد محاولات فاشلة", "locked sign-in for {r} after failed attempts"),
        ["EmailVerified"] = A("تأكيد البريد", "Email verified", "تأكيد البريد الإلكتروني لـ{r}", "verified the email of {r}"),
        ["PasswordReset"] = A("إعادة تعيين كلمة المرور", "Password reset", "إعادة تعيين كلمة المرور لـ{r}", "reset the password of {r}"),
        ["CreatedWithPassword"] = A("إنشاء مستخدم باسم مستخدم", "User created with a username", "إنشاء {r} باسم مستخدم وكلمة مرور", "created {r} with a username and password"),
        ["PasswordSetByAdmin"] = A("تعيين كلمة المرور من المسؤول", "Password set by an administrator", "تعيين كلمة مرور جديدة لـ{r}", "set a new password for {r}"),
        ["PasswordChanged"] = A("تغيير كلمة المرور", "Password changed", "تغيير كلمة المرور لـ{r}", "changed the password of {r}"),
        ["TwoFactorSkipped"] = A("تأجيل التحقق بخطوتين", "Two-step postponed", "تأجيل إعداد التحقق بخطوتين لـ{r}", "postponed setting up two-step verification for {r}"),
        ["TwoFactorEnabled"] = A("تفعيل التحقق بخطوتين", "Two-step enabled", "تفعيل التحقق بخطوتين لـ{r}", "enabled two-step verification for {r}"),
        // DT-210: managing two-step verification from the profile.
        ["TwoFactorDisabled"] = A("إيقاف التحقق بخطوتين", "Two-step turned off", "إيقاف التحقق بخطوتين لـ{r}", "turned off two-step verification for {r}"),
        ["TwoFactorAppChanged"] = A("تغيير تطبيق المصادقة", "Authenticator app changed", "تغيير تطبيق المصادقة لـ{r}", "changed the authenticator app of {r}"),
        ["RecoveryCodesRenewed"] = A("رموز استرداد جديدة", "New recovery codes", "إصدار رموز استرداد جديدة لـ{r}", "issued new recovery codes for {r}"),
        ["TwoFactorReset"] = A("إعادة تعيين التحقق بخطوتين", "Two-step reset", "إعادة تعيين التحقق بخطوتين لـ{r}", "reset two-step verification of {r}"),
        ["Unlock"] = A("فك القفل", "Unlocked", "فك قفل {r}", "unlocked {r}"),
        ["Disable"] = A("تعطيل", "Disabled", "تعطيل {r}", "disabled {r}"),
        ["Enable"] = A("تمكين", "Enabled", "تمكين {r}", "enabled {r}"),
        ["RoleChange"] = A("تغيير الدور", "Role changed", "تغيير {r}", "changed {r}"),
        ["PermissionChange"] = A("تغيير الصلاحيات", "Permissions changed", "تغيير صلاحيات {r}", "changed the permissions of {r}"),
        ["Resend"] = A("إعادة إرسال الدعوة", "Invitation resent", "إعادة إرسال {r}", "resent {r}"),
        ["Accept"] = A("قبول الدعوة", "Invitation accepted", "قبول {r}", "accepted {r}"),
        ["Anonymise"] = A("محو البيانات الشخصية", "Personal data erased", "محو البيانات الشخصية لـ{r}", "erased the personal data of {r}"),
        // Subscription and platform (tenant records shown in the company's log)
        ["Paid"] = A("دفع", "Paid", "دفع {r}", "paid {r}"),
        ["PaidNotApplied"] = A("دفعة غير مطبّقة", "Payment not applied", "دفعة لم تُطبّق على {r}", "a payment was not applied to {r}"),
        ["Refunded"] = A("استرداد", "Refunded", "استرداد دفعة {r}", "refunded a payment of {r}"),
        ["PaymentFailed"] = A("فشل الدفع", "Payment failed", "فشل دفع {r}", "a payment of {r} failed"),
        ["Credit"] = A("رصيد دائن", "Credited", "منح رصيد على {r}", "credited {r}"),
        ["ExtendTrial"] = A("تمديد التجربة", "Trial extended", "تمديد الفترة التجريبية لـ{r}", "extended the trial of {r}"),
        ["Suspend"] = A("إيقاف", "Suspended", "إيقاف {r}", "suspended {r}"),
        ["Reactivate"] = A("إعادة تفعيل", "Reactivated", "إعادة تفعيل {r}", "reactivated {r}"),
        ["Downgrade"] = A("تغيير الخطة", "Plan changed", "تغيير خطة {r}", "changed the plan of {r}"),
        ["TrialEnded"] = A("انتهاء التجربة", "Trial ended", "انتهاء الفترة التجريبية لـ{r}", "the trial of {r} ended"),
        ["GraceEnded"] = A("انتهاء المهلة", "Grace period ended", "انتهاء مهلة السداد لـ{r}", "the grace period of {r} ended"),
        ["Approve.Support"] = A("موافقة", "Approved", "الموافقة على {r}", "approved {r}"),
        ["Reject"] = A("رفض", "Rejected", "رفض {r}", "rejected {r}"),
        ["End"] = A("إنهاء", "Ended", "إنهاء {r}", "ended {r}"),
        ["SupportRequest"] = A("طلب دعم", "Support request", "اطلاع الدعم على {r}", "support viewed {r}"),
    };

    /// <summary>The words of an action; an unknown one is shown split into words ("Something happened").</summary>
    public static ActionWords Action(string action)
    {
        if (Actions.TryGetValue(action, out var w)) return w;
        var words = Humanize(action);
        return A(words, words, words + " {r}", words.ToLowerInvariant() + " {r}");
    }

    /// <summary>The event as a sentence: "Ali Ali added the trip…" / "علي علي: إضافة الرحلة…" (the Arabic verbal noun needs no gender).</summary>
    public static string Sentence(string? user, string predicate, bool en) => en ? $"{user ?? "The system"} {predicate}" : $"{user ?? "النظام"}: {predicate}";

    /// <summary>"SomeCode" → "Some code".</summary>
    public static string Humanize(string code)
    {
        if (string.IsNullOrEmpty(code)) return code;
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < code.Length; i++)
        {
            var c = code[i];
            if (i > 0 && char.IsUpper(c) && !char.IsUpper(code[i - 1])) sb.Append(' ').Append(char.ToLowerInvariant(c));
            else sb.Append(c);
        }
        return sb.ToString();
    }

    // ------------------------------------------------------------ fields

    /// <summary>How a field's value is shown.</summary>
    public enum Kind { Auto, Hidden, Text, Code, Money, Percent, Number, Date, DateTime, Bool, Ref, Custom, Masked }

    public sealed record FieldWords(Words Label, Kind Kind = Kind.Auto, string? Target = null);

    private static FieldWords F(string ar, string en, Kind kind = Kind.Auto, string? target = null) => new(new(ar, en), kind, target);

    private static FieldWords R(string ar, string en, string target) => new(new(ar, en), Kind.Ref, target);

    private static readonly FieldWords HiddenField = new(new("", ""), Kind.Hidden);

    /// <summary>Technical columns never shown to readers: keys, stamps, row versions, internal counters, secrets (item 5).</summary>
    public static readonly IReadOnlySet<string> Hidden = new HashSet<string>(StringComparer.Ordinal)
    {
        "Id", "TenantId", "CompanyId", "PublicId", "CreatedAt", "CreatedBy", "UpdatedAt", "UpdatedBy", "RowVersion", "IsDeleted", "DeletedAt",
        "DeletedBy", "DeleteReason", "LineNo", "SortOrder", "ReceiptNoKey", "ReceiptNoSearch", "TwoFactorLastStep", "NormalizedEmail", "NormalizedUserName", "PasswordHash", "SecurityStamp", "TokenHash", "CodeHash",
        "VoidedBy", "VoidedAt", "ApprovedAt", "ClosedBy", "ClosedAt", "ExportedBy", "ExportedAt", "ImportedBy", "ImportedAt", "ImportBatchId",
        "ExportBatchId", "JournalEntryId", "PeriodMonth", "SourceModule", "SourceType", "SourceId", "IsManual", "IcvCounter", "Icv", "UniqueId",
        "DocumentVersion", "Attempts", "LastAttemptAt", "LastSignInAt", "LastSeenAt", "LastActivityAt", "FailedSignInCount", "RequestPayload",
        "ResponsePayload", "QrCode", "IncomeSourceSequence", "TruckPlateAtSave", "RateMonth", "InvoiceLineId", "DocumentId", "CreditNoteId",
        "AttachmentsCount", "AcquiredAt", "Definition", "Columns", "Labels", "Signatures", "AnnexColumns", "Options", "Lines", "EmailConfirmedAt",
        "TwoFactorEnabledAt", "OccurredAt", "Grouping", "IsPlatformStaff", "UserId", "OwnerUserId", "Specificity", "ResolvedAt",
    };

    /// <summary>Field labels by column name (the labels of the screens); <see cref="EntityFields"/> refines some per record type.</summary>
    public static readonly IReadOnlyDictionary<string, FieldWords> Fields = new Dictionary<string, FieldWords>(StringComparer.Ordinal)
    {
        ["Name"] = F("الاسم", "Name"),
        ["NameAr"] = F("الاسم بالعربية", "Arabic name"),
        ["NameEn"] = F("الاسم بالإنجليزية", "English name"),
        ["FullName"] = F("الاسم الكامل", "Full name"),
        ["Code"] = F("الرمز", "Code"),
        ["Number"] = F("الرقم", "Number"),
        ["Status"] = F("الحالة", "Status", Kind.Code),
        ["Notes"] = F("ملاحظات", "Notes"),
        ["Note"] = F("ملاحظة", "Note"),
        ["Description"] = F("البيان", "Description"),
        ["Reason"] = F("السبب", "Reason"),
        ["VoidReason"] = F("سبب الإلغاء", "Void reason"),
        ["Reference"] = F("المرجع", "Reference"),
        ["Amount"] = F("المبلغ", "Amount", Kind.Money),
        ["Total"] = F("الإجمالي", "Total", Kind.Money),
        ["Subtotal"] = F("المجموع قبل الضريبة", "Subtotal", Kind.Money),
        ["Tax"] = F("الضريبة", "Tax", Kind.Money),
        ["Deduction"] = F("الخصم", "Deduction", Kind.Money),
        ["TaxRate"] = F("نسبة الضريبة", "Tax rate", Kind.Percent),
        ["Discount"] = F("الخصم", "Discount", Kind.Money),
        ["ExtraFees"] = F("رسوم إضافية", "Extra fees", Kind.Money),
        ["ExtraFeesDescription"] = F("بيان الرسوم الإضافية", "Extra fees description"),
        ["BaseAmount"] = F("المبلغ الخاضع للضريبة", "Taxable amount", Kind.Money),
        ["PaidAmount"] = F("المدفوع", "Paid", Kind.Money),
        ["ReturnedAmount"] = F("المرتجع", "Returned", Kind.Money),
        ["RemainingAmount"] = F("المتبقي", "Remaining", Kind.Money),
        ["DiscountShare"] = F("حصة الخصم", "Discount share", Kind.Money),
        ["FeesShare"] = F("حصة الرسوم", "Fees share", Kind.Money),
        ["Price"] = F("السعر", "Price", Kind.Money),
        ["UnitPrice"] = F("سعر الوحدة", "Unit price", Kind.Money),
        ["Quantity"] = F("الكمية", "Quantity", Kind.Number),
        ["Debit"] = F("مدين", "Debit", Kind.Money),
        ["Credit"] = F("دائن", "Credit", Kind.Money),
        ["TotalDebit"] = F("إجمالي المدين", "Total debit", Kind.Money),
        ["TotalCredit"] = F("إجمالي الدائن", "Total credit", Kind.Money),
        ["OpeningDebit"] = F("رصيد افتتاحي مدين", "Opening debit", Kind.Money),
        ["OpeningCredit"] = F("رصيد افتتاحي دائن", "Opening credit", Kind.Money),
        ["PaymentMethod"] = F("طريقة الدفع", "Payment method", Kind.Code),
        ["CashBoxId"] = R("الصندوق", "Cash box", "ACC.CashBox"),
        ["BankAccountId"] = R("الحساب البنكي", "Bank account", "ACC.BankAccount"),
        ["AccountId"] = R("الحساب", "Account", "ACC.Account"),
        ["ControlAccountId"] = R("حساب المراقبة", "Control account", "ACC.Account"),
        ["CostCentreId"] = R("مركز التكلفة", "Cost centre", "ACC.CostCentre"),
        ["DriverId"] = R("السائق", "Driver", "TRN.Driver"),
        ["CustomerId"] = R("العميل", "Customer", "TRN.Customer"),
        ["PartnerId"] = R("الشريك", "Partner", "TRN.Partner"),
        ["MaterialId"] = R("المادة", "Material", "TRN.Material"),
        ["LoadingSiteId"] = R("موقع التحميل", "Loading site", "TRN.LoadingSite"),
        ["UnloadingSiteId"] = R("موقع التنزيل", "Unloading site", "TRN.UnloadingSite"),
        ["OwnershipTypeId"] = R("نوع الملكية", "Ownership type", "TRN.OwnershipType"),
        ["InvoiceId"] = R("الفاتورة", "Invoice", "TRN.Invoice"),
        ["SettlementId"] = R("التسوية", "Settlement", "TRN.Settlement"),
        ["TripId"] = R("الرحلة", "Trip", "TRN.Trip"),
        ["ApArPartyId"] = R("الطرف المحاسبي", "Accounting party", "ACC.ApArParty"),
        ["ApArDocumentId"] = R("المستند", "Document", "ACC.ApArDocument"),
        ["ApArReturnId"] = R("المرتجع", "Return", "ACC.ApArReturn"),
        ["PartyId"] = R("الطرف", "Party", "ACC.ApArParty"),
        ["ParentId"] = R("الحساب الأب", "Parent account", "ACC.Account"),
        ["ReversesEntryId"] = R("يعكس القيد", "Reverses entry", "ACC.JournalEntry"),
        ["ReversedByEntryId"] = R("معكوس بالقيد", "Reversed by entry", "ACC.JournalEntry"),
        ["RoleId"] = R("الدور", "Role", "CORE.Role"),
        ["PermissionId"] = R("الصلاحية", "Permission", "CORE.Permission"),
        ["DefaultCompanyId"] = R("الشركة الافتراضية", "Default company", "CORE.Company"),
        ["PlanId"] = R("الخطة", "Plan", "CORE.Plan"),
        ["SavedReportId"] = R("التقرير", "Report", "CFG.SavedReport"),
        ["RequestedBy"] = R("طلبه", "Requested by", "CORE.User"),
        ["ApprovedBy"] = R("اعتمده", "Approved by", "CORE.User"),
        ["InvitedBy"] = R("دعاه", "Invited by", "CORE.User"),
        // Trips (defaults; the company's labels from Field configuration come first)
        ["ReceiptNo"] = F("رقم الإيصال", "Receipt number"),
        ["TripDate"] = F("تاريخ الرحلة", "Trip date"),
        ["LoadingDate"] = F("تاريخ التحميل", "Loading date"),
        ["UnloadingDate"] = F("تاريخ التنزيل", "Unloading date"),
        ["LoadedWeight"] = F("الوزن المحمّل (طن)", "Loaded weight (t)", Kind.Number),
        ["UnloadedWeight"] = F("الوزن المنزّل (طن)", "Unloaded weight (t)", Kind.Number),
        ["BillingQty"] = F("الكمية المفوترة (طن)", "Billed quantity (t)", Kind.Number),
        ["WageQty"] = F("كمية الأجرة (طن)", "Wage quantity (t)", Kind.Number),
        ["CustomerPrice"] = F("سعر العميل (للطن)", "Customer price (per ton)", Kind.Money),
        ["TariffPrice"] = F("سعر التعرفة", "Tariff price", Kind.Money),
        ["DriverRate"] = F("أجرة السائق (للطن)", "Driver rate (per ton)", Kind.Money),
        ["PartnerRate"] = F("سعر الشريك (للطن)", "Partner rate (per ton)", Kind.Money),
        ["RateSource"] = F("مصدر السعر", "Rate source", Kind.Code),
        ["Revenue"] = F("الإيراد", "Revenue", Kind.Money),
        ["DriverWage"] = F("أجرة السائق", "Driver wage", Kind.Money),
        ["PartnerCost"] = F("تكلفة الشريك", "Partner cost", Kind.Money),
        ["ShortageWeight"] = F("وزن النقص (طن)", "Shortage (t)", Kind.Number),
        ["ShortagePct"] = F("نسبة النقص", "Shortage %", Kind.Percent),
        ["IsShortageFlagged"] = F("نقص يتجاوز الحد", "Shortage over tolerance"),
        ["IsTariffBreach"] = F("مخالفة تعرفة", "Tariff breach"),
        ["IsManualRate"] = F("سعر يدوي", "Manual price"),
        ["NeedsReview"] = F("تحتاج مراجعة", "Needs review"),
        ["ReviewNote"] = F("ملاحظة المراجعة", "Review note"),
        ["FleetClass"] = F("فئة الأسطول", "Fleet", Kind.Code),
        ["CustomValues"] = F("حقول مخصصة", "Custom fields", Kind.Custom),
        // Invoices and documents
        ["InvoiceType"] = F("نوع الفاتورة", "Invoice type", Kind.Code),
        ["InvoiceDate"] = F("تاريخ الفاتورة", "Invoice date"),
        ["DueDate"] = F("تاريخ الاستحقاق", "Due date"),
        ["LayoutType"] = F("نوع النموذج", "Layout", Kind.Code),
        ["CreditDate"] = F("تاريخ الإشعار", "Credit date"),
        ["ReceiptDate"] = F("تاريخ القبض", "Receipt date"),
        ["AdvanceDate"] = F("تاريخ السلفة", "Advance date"),
        ["SettlementDate"] = F("تاريخ التسوية", "Settlement date"),
        ["PaymentDate"] = F("تاريخ الدفع", "Payment date"),
        ["ReturnDate"] = F("تاريخ المرتجع", "Return date"),
        ["DocumentDate"] = F("تاريخ المستند", "Document date"),
        ["EntryDate"] = F("تاريخ القيد", "Entry date"),
        ["GrossWages"] = F("إجمالي الأجور", "Gross wages", Kind.Money),
        ["AdvancesTotal"] = F("إجمالي السلف", "Advances", Kind.Money),
        ["DeductionsTotal"] = F("إجمالي الخصومات", "Deductions", Kind.Money),
        ["NetPayable"] = F("الصافي المستحق", "Net payable", Kind.Money),
        ["DeductionType"] = F("نوع الخصم", "Deduction type", Kind.Code),
        ["DocumentKind"] = F("نوع المستند", "Document type", Kind.Code),
        ["DocumentType"] = F("نوع المستند", "Document type", Kind.Code),
        ["SystemNumber"] = F("رقم النظام", "System number"),
        ["LastError"] = F("آخر خطأ", "Last error"),
        ["SubmittedAt"] = F("وقت الإرسال", "Submitted at", Kind.DateTime),
        ["PartyType"] = F("نوع الطرف", "Party type", Kind.Code),
        ["PartyRole"] = F("دور الطرف", "Party role", Kind.Code),
        ["PartyModule"] = HiddenField,
        // Master data
        ["NationalId"] = F("الرقم الوطني", "National ID"),
        ["Phone"] = F("الهاتف", "Phone"),
        ["Phone1"] = F("الهاتف 1", "Phone 1"),
        ["Phone2"] = F("الهاتف 2", "Phone 2"),
        ["Email"] = F("البريد الإلكتروني", "Email"),
        ["Address"] = F("العنوان", "Address"),
        ["City"] = F("المدينة", "City"),
        ["Area"] = F("المنطقة", "Area"),
        ["ContactPerson"] = F("الشخص المسؤول", "Contact person"),
        ["TaxNumber"] = F("الرقم الضريبي", "Tax number"),
        ["RegistrationNo"] = F("السجل التجاري / الرقم الوطني", "Commercial registration / national ID"),
        ["CommercialRegNo"] = F("السجل التجاري", "Commercial registration"),
        ["PaymentTermsDays"] = F("مدة السداد (أيام)", "Payment terms (days)", Kind.Number),
        ["LicenceNo"] = F("رقم الرخصة", "Licence number"),
        ["LicenceType"] = F("فئة الرخصة", "Licence type"),
        ["LicenceExpiry"] = F("تاريخ انتهاء الرخصة", "Licence expiry"),
        ["BankName"] = F("اسم البنك", "Bank name"),
        ["Iban"] = F("رقم الآيبان", "IBAN"),
        ["CliqAlias"] = F("اسم CliQ المستعار", "CliQ alias"),
        ["AccountNumber"] = F("رقم الحساب", "Account number"),
        ["CurrencyCode"] = F("العملة", "Currency"),
        ["PlateNumber"] = F("رقم اللوحة", "Plate number"),
        ["TruckType"] = F("نوع الشاحنة", "Truck type"),
        ["TrailerNo"] = F("رقم المقطورة", "Trailer number"),
        ["CapacityTons"] = F("الحمولة (طن)", "Capacity (t)", Kind.Number),
        ["RegistrationExpiry"] = F("تاريخ انتهاء الترخيص", "Registration expiry"),
        ["InsuranceExpiry"] = F("تاريخ انتهاء التأمين", "Insurance expiry"),
        ["InspectionExpiry"] = F("تاريخ انتهاء الفحص", "Inspection expiry"),
        ["Unit"] = F("الوحدة", "Unit"),
        ["IsBuiltIn"] = F("مدمج", "Built-in"),
        ["ValidFrom"] = F("ساري من", "Valid from"),
        ["ValidTo"] = F("ساري إلى", "Valid to"),
        ["OwnFleetRate"] = F("سعر الأسطول الخاص", "Own fleet rate", Kind.Money),
        ["ExternalFleetRate"] = F("سعر الأسطول الخارجي", "External fleet rate", Kind.Money),
        // Accounting
        ["AccountType"] = F("نوع الحساب", "Account type", Kind.Code),
        ["IsPostable"] = F("يقبل الترحيل", "Postable"),
        ["IsControlAccount"] = F("حساب مراقبة", "Control account"),
        ["ControlPartyType"] = F("نوع أطراف المراقبة", "Control party type", Kind.Code),
        ["PostingKey"] = F("مفتاح الترحيل", "Posting key", Kind.Code),
        ["BatchNo"] = F("رقم الدفعة", "Batch number"),
        ["PeriodFrom"] = F("من فترة", "From period"),
        ["PeriodTo"] = F("إلى فترة", "To period"),
        ["Format"] = F("الصيغة", "Format", Kind.Code),
        ["EntryCount"] = F("عدد القيود", "Entries", Kind.Number),
        // Settings and configuration
        ["SettingKey"] = F("الإعداد", "Setting", Kind.Code),
        ["SettingValue"] = F("القيمة", "Value"),
        ["FieldKey"] = F("الحقل", "Field"),
        ["Label"] = F("التسمية", "Label"),
        ["LabelAr"] = F("التسمية بالعربية", "Arabic label"),
        ["LabelEn"] = F("التسمية بالإنجليزية", "English label"),
        ["FieldType"] = F("نوع الحقل", "Field type", Kind.Code),
        ["DefaultValue"] = F("القيمة الافتراضية", "Default value"),
        ["HelpText"] = F("نص المساعدة", "Help text"),
        ["IsRequired"] = F("إلزامي", "Required"),
        ["IsHidden"] = F("مخفي", "Hidden"),
        ["ShowAsColumn"] = F("عمود في القائمة", "Show as column"),
        ["EntityType"] = F("نوع السجل", "Record type", Kind.Code),
        ["Prefix"] = F("البادئة", "Prefix"),
        ["Digits"] = F("عدد الخانات", "Digits", Kind.Number),
        ["NextNumber"] = F("الرقم التالي", "Next number", Kind.Number),
        ["IsEnabled"] = F("مفعّل", "Enabled"),
        ["Connector"] = F("الربط", "Connector", Kind.Code),
        ["SellerTaxNumber"] = F("الرقم الضريبي للبائع", "Seller tax number"),
        ["RegistrationType"] = F("نوع التسجيل", "Registration type", Kind.Code),
        ["Environment"] = F("البيئة", "Environment", Kind.Code),
        ["Title"] = F("العنوان", "Title"),
        ["BandText"] = F("نص الشريط", "Band text"),
        ["FooterText"] = F("نص التذييل", "Footer text"),
        ["ShowLogo"] = F("إظهار الشعار", "Show logo"),
        ["ShowQr"] = F("إظهار رمز QR", "Show QR code"),
        ["Source"] = F("مصدر البيانات", "Data source", Kind.Code),
        ["IsShared"] = F("مشترك", "Shared"),
        ["Measure"] = F("المقياس", "Measure"),
        ["WarningThreshold"] = F("حد التنبيه", "Warning threshold", Kind.Money),
        ["CriticalThreshold"] = F("الحد الحرج", "Critical threshold", Kind.Money),
        ["ViewKey"] = F("الجدول", "Table"),
        ["FileName"] = F("الملف", "File"),
        ["Mode"] = F("الوضع", "Mode", Kind.Code),
        ["TotalRows"] = F("عدد الصفوف", "Rows", Kind.Number),
        ["ImportedRows"] = F("الصفوف المستوردة", "Imported rows", Kind.Number),
        ["RejectedRows"] = F("الصفوف المرفوضة", "Rejected rows", Kind.Number),
        // Company, tenant, users
        ["CountryCode"] = F("الدولة", "Country"),
        ["CurrencyDecimals"] = F("منازل العملة", "Currency decimals", Kind.Number),
        ["TaxInclusive"] = F("الأسعار شاملة الضريبة", "Prices include tax"),
        ["TimeZone"] = F("المنطقة الزمنية", "Time zone"),
        ["MigrationCompletedAt"] = F("اكتمال الترحيل", "Migration completed", Kind.DateTime),
        ["LanguageCode"] = F("اللغة", "Language", Kind.Code),
        ["DateFormat"] = F("صيغة التاريخ", "Date format"),
        ["LockedUntil"] = F("مقفل حتى", "Locked until", Kind.DateTime),
        ["IsPreset"] = F("دور جاهز", "Preset role"),
        ["ExpiresAt"] = F("تنتهي في", "Expires", Kind.DateTime),
        ["AcceptedAt"] = F("قُبلت في", "Accepted", Kind.DateTime),
        ["UsedAt"] = F("استُخدم في", "Used", Kind.DateTime),
        ["BillingCycle"] = F("دورة الفوترة", "Billing cycle", Kind.Code),
        ["TrialEndsAt"] = F("نهاية التجربة", "Trial ends", Kind.DateTime),
        ["CurrentPeriodEnd"] = F("نهاية الفترة الحالية", "Current period ends", Kind.DateTime),
        ["GraceEndsAt"] = F("نهاية المهلة", "Grace period ends", Kind.DateTime),
        ["CancelledAt"] = F("أُلغي في", "Cancelled", Kind.DateTime),
        ["RetentionEndsAt"] = F("نهاية الاحتفاظ", "Retention ends", Kind.DateTime),
        ["PasswordMinLength"] = F("أقل طول لكلمة المرور", "Minimum password length", Kind.Number),
        ["RequireTwoFactor"] = F("إلزام كل المستخدمين بالتحقق بخطوتين", "Require two-step verification for all users"),
        ["UserName"] = F("اسم المستخدم", "Username"),
        ["MustChangePassword"] = F("تغيير كلمة المرور عند الدخول التالي", "Must change password at next sign-in"),
        ["PasswordRequireLetter"] = F("كلمة المرور تتطلب حرفاً", "Password needs a letter"),
        ["PasswordRequireDigit"] = F("كلمة المرور تتطلب رقماً", "Password needs a digit"),
        ["SessionIdleTimeoutMinutes"] = F("مهلة الخمول (دقائق)", "Idle timeout (minutes)", Kind.Number),
        ["PeriodStart"] = F("بداية الفترة", "Period start"),
        ["PeriodEnd"] = F("نهاية الفترة", "Period end"),
        ["PaidAt"] = F("دُفعت في", "Paid", Kind.DateTime),
        ["GatewayReference"] = F("مرجع بوابة الدفع", "Gateway reference"),
        ["StartsAt"] = F("يبدأ في", "Starts", Kind.DateTime),
        ["EndsAt"] = F("ينتهي في", "Ends", Kind.DateTime),
        ["Message"] = F("الرسالة", "Message"),
        ["IsActive"] = F("فعّالة", "Active"),
        ["MaxCompanies"] = F("الحد الأقصى للشركات", "Max companies", Kind.Number),
        ["MaxActiveUsers"] = F("الحد الأقصى للمستخدمين", "Max active users", Kind.Number),
        ["MaxTripsPerMonth"] = F("الحد الأقصى للرحلات شهرياً", "Max trips per month", Kind.Number),
        ["MaxStorageMb"] = F("مساحة التخزين (م.ب)", "Storage (MB)", Kind.Number),
        ["GroupName"] = F("المجموعة", "Group"),
        ["PermissionKey"] = F("الصلاحية", "Permission"),
        ["RecordCounts"] = F("عدد السجلات", "Records"),
        ["Action"] = F("الإجراء", "Action"),
    };

    /// <summary>Labels that differ by record type (e.g. the name of a customer is "اسم العميل" on its form).</summary>
    public static readonly IReadOnlyDictionary<string, FieldWords> EntityFields = new Dictionary<string, FieldWords>(StringComparer.Ordinal)
    {
        ["TRN.Customer.Name"] = F("اسم العميل", "Name"),
        ["TRN.Partner.Name"] = F("اسم الشريك", "Name"),
        ["TRN.Driver.PartnerId"] = R("الشركة الشريكة", "Partner company", "TRN.Partner"),
        ["TRN.Tariff.PartyType"] = F("نوع التعرفة", "Tariff type", Kind.Code),
        ["TRN.Tariff.Price"] = F("السعر (للطن)", "Price (per ton)", Kind.Money),
        ["TRN.Receipt.InvoiceId"] = R("الفاتورة", "Invoice", "TRN.Invoice"),
        ["TRN.ReceiptAllocation.ReceiptId"] = R("سند القبض", "Receipt", "TRN.Receipt"),
        ["ACC.ApArDocument.PartyId"] = R("الطرف", "Party", "ACC.ApArParty"),
        ["ACC.Account.Code"] = F("رقم الحساب", "Account code"),
        ["ACC.Period.PeriodMonth"] = F("الفترة", "Period"),
        ["ACC.JournalEntry.Description"] = F("البيان", "Description"),
        ["ACC.JournalEntry.Status"] = F("الحالة", "Status", Kind.Code),
        ["CORE.Company.TaxRate"] = F("نسبة الضريبة", "Tax rate", Kind.Percent),
        ["CORE.Plan.Code"] = F("رمز الخطة", "Plan code"),
        ["CORE.SupportAccessGrant.Reason"] = F("سبب الطلب", "Reason"),
        ["CFG.CompanySetting.SettingValue"] = F("القيمة", "Value"),
        // Revision 6 part B: budgets (month names as in the budget screen and its Excel template).
        ["ACC.Budget.FiscalYear"] = F("السنة المالية", "Fiscal year", Kind.Number),
        ["ACC.BudgetLine.BudgetId"] = R("الموازنة", "Budget", "ACC.Budget"),
        ["ACC.BudgetLine.Total"] = F("الإجمالي السنوي", "Yearly total", Kind.Money),
        ["ACC.BudgetLine.Month01"] = F("كانون الثاني", "January", Kind.Money),
        ["ACC.BudgetLine.Month02"] = F("شباط", "February", Kind.Money),
        ["ACC.BudgetLine.Month03"] = F("آذار", "March", Kind.Money),
        ["ACC.BudgetLine.Month04"] = F("نيسان", "April", Kind.Money),
        ["ACC.BudgetLine.Month05"] = F("أيار", "May", Kind.Money),
        ["ACC.BudgetLine.Month06"] = F("حزيران", "June", Kind.Money),
        ["ACC.BudgetLine.Month07"] = F("تموز", "July", Kind.Money),
        ["ACC.BudgetLine.Month08"] = F("آب", "August", Kind.Money),
        ["ACC.BudgetLine.Month09"] = F("أيلول", "September", Kind.Money),
        ["ACC.BudgetLine.Month10"] = F("تشرين الأول", "October", Kind.Money),
        ["ACC.BudgetLine.Month11"] = F("تشرين الثاني", "November", Kind.Money),
        ["ACC.BudgetLine.Month12"] = F("كانون الأول", "December", Kind.Money),
    };

    public static FieldWords? Field(string? entityType, string column)
    {
        if (entityType is not null && EntityFields.TryGetValue(entityType + "." + column, out var specific)) return specific;
        return Fields.GetValueOrDefault(column);
    }

    // ------------------------------------------------------------ stored codes

    /// <summary>Stored codes shown in words (statuses, payment methods, types…); the labels of the screens' tags and choices.</summary>
    public static readonly IReadOnlyDictionary<string, Words> Codes = new Dictionary<string, Words>(StringComparer.Ordinal)
    {
        ["Active"] = new("نشط", "Active"),
        ["Archived"] = new("مؤرشف", "Archived"),
        ["Void"] = new("ملغاة", "Void"),
        ["Draft"] = new("مسودة", "Draft"),
        ["Approved"] = new("معتمدة", "Approved"),
        ["Unpaid"] = new("معتمدة – غير مدفوعة", "Approved – unpaid"),
        ["PartiallyPaid"] = new("مدفوعة جزئياً", "Partly paid"),
        ["PartiallyReturned"] = new("مرتجعة جزئياً", "Partly returned"),
        ["Paid"] = new("مدفوعة", "Paid"),
        ["Closed"] = new("مغلقة", "Closed"),
        ["Open"] = new("مفتوحة", "Open"),
        ["Partial"] = new("مدفوعة جزئياً", "Partly paid"),
        ["Posted"] = new("مرحّل", "Posted"),
        ["Pending"] = new("معلّق", "Pending"),
        ["Reversed"] = new("معكوس", "Reversed"),
        ["Disabled"] = new("معطّل", "Disabled"),
        ["Invited"] = new("مدعو", "Invited"),
        ["Trial"] = new("تجريبي", "Trial"),
        ["PastDue"] = new("متأخر الدفع", "Past due"),
        ["Suspended"] = new("موقوف", "Suspended"),
        ["Cancelled"] = new("ملغى", "Cancelled"),
        ["Deleted"] = new("محذوف", "Deleted"),
        ["Failed"] = new("فشل", "Failed"),
        ["Due"] = new("مستحقة", "Due"),
        ["Requested"] = new("مطلوب", "Requested"),
        ["Expired"] = new("منتهٍ", "Expired"),
        ["Credited"] = new("مُسترد برصيد", "Credited"),
        ["Queued"] = new("في الطابور", "Queued"),
        ["Submitted"] = new("مقبولة", "Accepted"),
        ["Rejected"] = new("مرفوضة", "Rejected"),
        ["NotRequired"] = new("غير مطلوبة", "Not required"),
        // Payment methods
        ["Cash"] = new("نقد", "Cash"),
        ["Bank"] = new("تحويل بنكي", "Bank transfer"),
        ["CliQ"] = new("CliQ", "CliQ"),
        // Invoices
        ["Trip"] = new("فاتورة رحلات", "Trip invoice"),
        ["Manual"] = new("يدوي", "Manual"),
        ["Claim"] = new("كشف مطالبة", "Claim statement"),
        ["TaxInvoice"] = new("فاتورة ضريبية", "Tax invoice"),
        ["Copy"] = new("نسخة", "Copy"),
        // Trips
        ["Own"] = new("أسطول خاص", "Own fleet"),
        ["External"] = new("أسطول خارجي", "External fleet"),
        ["Tariff"] = new("التعرفة", "Tariff"),
        ["Monthly"] = new("السعر الشهري", "Monthly rate"),
        ["None"] = new("بدون", "None"),
        ["Complete"] = new("مكتمل", "Complete"),
        // Parties and documents
        ["Customer"] = new("عميل", "Customer"),
        ["Supplier"] = new("مورد", "Supplier"),
        ["Driver"] = new("سائق", "Driver"),
        ["Partner"] = new("شريك", "Partner"),
        ["ApArParty"] = new("طرف محاسبي", "Accounting party"),
        ["SupplierBill"] = new("فاتورة مورد", "Supplier bill"),
        ["Receivable"] = new("مستند ذمم مدينة", "Receivable"),
        ["Invoice"] = new("فاتورة", "Invoice"),
        ["CreditNote"] = new("إشعار دائن", "Credit note"),
        ["Shortage"] = new("نقص", "Shortage"),
        ["Fine"] = new("غرامة", "Fine"),
        ["Other"] = new("أخرى", "Other"),
        // Accounts
        ["Asset"] = new("أصول", "Asset"),
        ["Liability"] = new("خصوم", "Liability"),
        ["Equity"] = new("حقوق ملكية", "Equity"),
        ["Revenue"] = new("إيرادات", "Revenue"),
        ["Expense"] = new("مصروفات", "Expense"),
        ["CashBox"] = new("صندوق", "Cash box"),
        ["BankAccount"] = new("حساب بنكي", "Bank account"),
        // Custom fields
        ["Text"] = new("نص", "Text"),
        ["LongText"] = new("نص طويل", "Long text"),
        ["Number"] = new("رقم", "Number"),
        ["Date"] = new("تاريخ", "Date"),
        ["List"] = new("قائمة", "List"),
        ["Checkbox"] = new("مربع اختيار", "Checkbox"),
        // E-invoicing
        ["JoFotara"] = new("جو فوترة", "JoFotara"),
        ["Simulator"] = new("المحاكي", "Simulator"),
        ["Test"] = new("تجريبية", "Test"),
        ["Production"] = new("الإنتاج", "Production"),
        ["Income"] = new("دخل", "Income"),
        ["Sales"] = new("مبيعات", "Sales"),
        ["Special"] = new("خاص", "Special"),
        // Languages, roles, cycles
        ["ar"] = new("العربية", "العربية"),
        ["en"] = new("English", "English"),
        ["Owner"] = new("المالك", "Owner"),
        ["Administrator"] = new("مدير النظام", "Administrator"),
        ["Accountant"] = new("محاسب", "Accountant"),
        ["Dispatcher"] = new("منسق الرحلات", "Dispatcher"),
        ["Viewer"] = new("مشاهد", "Viewer"),
        ["Annual"] = new("سنوي", "Annual"),
        ["Terms"] = new("شروط الاستخدام", "Terms of use"),
        ["Privacy"] = new("سياسة الخصوصية", "Privacy policy"),
        // Company settings (Settings › Company)
        ["TRN.BillOnUnloadedWeight"] = new("الفوترة على الوزن المنزّل", "Bill on unloaded weight"),
        ["TRN.PayDriverOnLoadedWeight"] = new("أجرة السائق على الوزن المحمّل", "Pay drivers on loaded weight"),
        ["TRN.ShortageTolerancePct"] = new("نسبة النقص المسموح بها", "Shortage tolerance"),
        ["TRN.LockTariffs"] = new("قفل التعرفة", "Lock tariffs"),
        ["TRN.InvoiceApproval"] = new("اعتماد الفواتير", "Invoice approval"),
        ["TRN.AccrueDriverWage"] = new("استحقاق أجرة السائق عند الرحلة", "Accrue driver wages per trip"),
        ["CORE.PhonePattern"] = new("صيغة الهاتف", "Phone format"),
        ["CORE.IbanPattern"] = new("صيغة الآيبان", "IBAN format"),
        ["CORE.IsDemo"] = new("شركة تجريبية", "Demo company"),
    };

    /// <summary>A stored code in words; an unknown code is shown as stored.</summary>
    public static string Code(string? code, bool en) => code is null ? "" : Codes.TryGetValue(code, out var w) ? w.In(en) : code;

    /// <summary>Whether a code has words (used to translate tokens of a reason such as "Claim · Copy").</summary>
    public static bool IsCode(string code) => Codes.ContainsKey(code);

    // ------------------------------------------------------------ links

    /// <summary>The screen kind of a record type for links (the client's <c>Links.ForSource</c> names), null when it has no screen.</summary>
    public static string? LinkKind(string? entityType) => entityType switch
    {
        "TRN.Trip" => "Trip",
        "TRN.Invoice" => "Invoice",
        "TRN.CreditNote" => "CreditNote",
        "TRN.Receipt" => "Receipt",
        "TRN.Advance" => "Advance",
        "TRN.Settlement" => "Settlement",
        "TRN.PartnerPayment" => "PartnerPayment",
        "ACC.JournalEntry" => "JournalEntry",
        "ACC.ApArPayment" => "ApArPayment",
        "ACC.ApArReturn" => "ApArReturn",
        "TRN.Driver" => "Driver",
        "TRN.Customer" => "Customer",
        "TRN.Partner" => "Partner",
        _ => null,
    };

    /// <summary>Record type of a journal entry's source (its SourceType) — the documents that post (FR-096).</summary>
    public static string? SourceEntity(string? sourceType) => sourceType switch
    {
        "Trip" => "TRN.Trip",
        "Invoice" => "TRN.Invoice",
        "CreditNote" => "TRN.CreditNote",
        "Receipt" => "TRN.Receipt",
        "Advance" => "TRN.Advance",
        "Settlement" => "TRN.Settlement",
        "PartnerPayment" => "TRN.PartnerPayment",
        "SupplierBill" or "Receivable" => "ACC.ApArDocument",
        "ApArPayment" => "ACC.ApArPayment",
        "ApArReturn" => "ACC.ApArReturn",
        _ => null,
    };

    /// <summary>Record type of the party of a journal line (its PartyType).</summary>
    public static string? PartyEntity(string? partyType) => partyType switch
    {
        "Customer" => "TRN.Customer",
        "Driver" => "TRN.Driver",
        "Partner" => "TRN.Partner",
        "ApArParty" => "ACC.ApArParty",
        _ => null,
    };
}
