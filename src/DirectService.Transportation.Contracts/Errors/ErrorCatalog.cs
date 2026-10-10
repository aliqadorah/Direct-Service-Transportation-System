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
        // Master data and field configuration (B3)
        [ErrorCodes.BuiltInFixed] = ("نوع الملكية المدمج «{0}» لا يمكن تغيير اسمه أو تصنيفه أو حذفه.", "The built-in ownership type «{0}» cannot be renamed, reclassified or deleted."),
        [ErrorCodes.FieldCore] = ("الحقل «{0}» حقل أساسي ولا يمكن إخفاؤه.", "Field «{0}» is a core field and cannot be hidden."),
        [ErrorCodes.FieldAlwaysRequired] = ("الحقل «{0}» مطلوب بحكم قواعد العمل ولا يمكن إخفاؤه أو جعله اختيارياً.", "Field «{0}» is required by the business rules and cannot be hidden or made optional."),
        [ErrorCodes.FieldUnknown] = ("الحقل «{0}» غير معروف.", "Unknown field «{0}»."),
        [ErrorCodes.CustomFieldHasData] = ("الحقل «{0}» يحتوي بيانات؛ يمكن إخفاؤه فقط.", "Field «{0}» has data; it can only be hidden."),
        [ErrorCodes.CustomFieldTypeFixed] = ("لا يمكن تغيير نوع الحقل «{0}» لأنه يحتوي بيانات.", "The type of field «{0}» cannot change because it has data."),
        [ErrorCodes.CustomFieldOptions] = ("حقل القائمة «{0}» يحتاج خياراً واحداً على الأقل، دون تكرار.", "List field «{0}» needs at least one option, without duplicates."),
        [ErrorCodes.CustomFieldOptionInUse] = ("الخيار «{0}» مستخدم في سجلات محفوظة ولا يمكن حذفه.", "Option «{0}» is used by saved records and cannot be removed."),
        [ErrorCodes.DriverPartnerOwnFleet] = ("سائق أسطول الشركة الخاص لا يمكن ربطه بشريك ({0}). اختر نوع ملكية خارجياً.", "A driver of the company's own fleet cannot be linked to a partner ({0}). Choose an external ownership type."),
        [ErrorCodes.FieldConditional] = ("الحقل «{0}» مطلوب في بعض الحالات بحكم قواعد العمل ولا يمكن إخفاؤه.", "Field «{0}» is required in some cases by the business rules and cannot be hidden."),
        [ErrorCodes.FieldCannotRequire] = ("الحقل «{0}» لا يمكن جعله مطلوباً دائماً لأن قواعد العمل تمنعه في بعض الحالات.", "Field «{0}» cannot be made always required because the business rules forbid it in some cases."),
        [ErrorCodes.OwnershipFleetInUse] = ("لا يمكن تحويل نوع الملكية إلى أسطول خاص لأن {0} سائقاً عليه مرتبطون بشركاء.", "The ownership type cannot become own fleet because {0} of its drivers are linked to partners."),
        [ErrorCodes.PendingNoHandler] = ("لا يمكن إعادة ترحيل هذا النوع من المستندات في هذا الإصدار.", "This document type cannot be re-posted in this version."),
        // Pricing (B4)
        [ErrorCodes.TariffDuplicate] = ("يوجد بند تعرفة بنفس المفاتيح وتاريخ البدء (بما في ذلك البنود المؤرشفة). عدّل البند الموجود أو استعده.", "A tariff line with the same keys and start date already exists (archived lines included). Edit or restore that line."),
        [ErrorCodes.TariffDuplicateInFile] = ("بند تعرفة مكرر في الملف (نفس المفاتيح وتاريخ البدء).", "Duplicate tariff line in the file (same keys and start date)."),
        [ErrorCodes.RatesInUse] = ("لا يمكن مسح أسعار الموقع «{0}» للشهر {1} لأن عليه {2} رحلة.", "The rates of site «{0}» for {1} cannot be cleared because {2} trip(s) use them."),
        [ErrorCodes.RatesMonthHasTrips] = ("الشهر {0} فيه رحلات محفوظة؛ الأسعار الجديدة تطبق على الرحلات الجديدة فقط.", "Month {0} already has trips; only new trips use the new rates."),
        [ErrorCodes.RateMissing] = ("لا يوجد سعر سائق لموقع التحميل «{0}» للشهر {1} ({2}).", "There is no driver rate for loading site «{0}» in {1} ({2})."),
        [ErrorCodes.RateManualNotAllowed] = ("إدخال سعر سائق يدوي يتطلب صلاحية إدارة البيانات الأساسية.", "Entering a manual driver rate requires the master-data permission."),
        // Trips (B5)
        [ErrorCodes.DateFuture] = ("الحقل «{0}» لا يمكن أن يكون في المستقبل.", "Field «{0}» cannot be in the future."),
        [ErrorCodes.TripReceiptDuplicate] = ("رقم السند «{0}» مستخدم في رحلة أخرى.", "Receipt number «{0}» is already used by another trip."),
        [ErrorCodes.TripInvoiced] = ("الرحلة {0} مفوترة في {1}. أعد الفاتورة أو ألغها أولاً.", "Trip {0} is invoiced on {1}. Return or void the invoice first."),
        [ErrorCodes.TripSettled] = ("أجر الرحلة {0} مسوّى في {1}. ألغِ التسوية أولاً.", "The wage of trip {0} is settled in {1}. Void the settlement first."),
        [ErrorCodes.TripIsVoid] = ("الرحلة {0} ملغاة.", "Trip {0} is void."),
        [ErrorCodes.TariffLocked] = ("قفل التعرفة مفعّل: الانحراف عن سعر التعرفة {0} يحتاج صلاحية إدارة البيانات الأساسية.", "Tariff lock is on: deviating from the tariff price {0} requires the master-data permission."),
        [ErrorCodes.TripWarnCapacity] = ("الوزن المحمّل {0} طن أكبر من حمولة الشاحنة {1} طن.", "Loaded weight {0} t is above the truck capacity of {1} t."),
        [ErrorCodes.TripWarnUnloaded] = ("الوزن المنزّل أكبر من المحمّل × 1.02.", "Unloaded weight is above loaded weight × 1.02."),
        [ErrorCodes.TripWarnDocument] = ("انتهت صلاحية {0} للسائق في {1}.", "The driver's {0} expired on {1}."),
        // Invoicing and credit notes (B6)
        [ErrorCodes.InvoiceNotDraft] = ("الفاتورة {0} معتمدة؛ يمكن تعديل المسودات أو اعتمادها أو حذفها فقط.", "Invoice {0} is approved; only drafts can be edited, approved or deleted."),
        [ErrorCodes.InvoiceIsDraft] = ("المسودة لا تُلغى؛ احذفها بدلاً من ذلك.", "A draft is not voided; delete it instead."),
        [ErrorCodes.InvoiceIsVoid] = ("الفاتورة {0} ملغاة.", "Invoice {0} is void."),
        [ErrorCodes.InvoiceTripUnavailable] = ("الرحلة {0} لم تعد رحلة نشطة غير مفوترة لهذا العميل.", "Trip {0} is no longer an active, unbilled trip of this customer."),
        [ErrorCodes.InvoiceTotalZero] = ("يجب أن يكون الإجمالي أكبر من صفر.", "The total must be greater than zero."),
        [ErrorCodes.InvoiceHasPayments] = ("لا يمكن إلغاء الفاتورة {0}: عليها {1} سند قبض و{2} إشعار دائن نشط. ألغها أولاً.", "Invoice {0} cannot be voided: it has {1} active receipt(s) and {2} active credit note(s). Void them first."),
        [ErrorCodes.InvoiceEInvoiced] = ("قبل نظام الفوترة الإلكترونية الفاتورة {0}؛ تُلغى بإشعار دائن كامل فقط.", "Invoice {0} was accepted by the e-invoicing system; it can only be cancelled by a full credit note."),
        [ErrorCodes.CreditInvoiceNotApproved] = ("الإشعار الدائن يُنشأ من فاتورة معتمدة فقط ({0}).", "A credit note can only be created from an approved invoice ({0})."),
        [ErrorCodes.CreditLineReturned] = ("هذا البند مرتجع بالكامل مسبقاً.", "This line is already fully returned."),
        [ErrorCodes.CreditExceedsRemaining] = ("الإشعار الدائن ({0}) أكبر من المتبقي على الفاتورة ({1}).", "The credit note ({0}) exceeds the invoice's remaining balance ({1})."),
        [ErrorCodes.CreditIsVoid] = ("الإشعار الدائن {0} ملغى.", "Credit note {0} is void."),
        [ErrorCodes.CreditTripReinvoiced] = ("لا يمكن إلغاء الإشعار: الرحلة {0} فُوترت مرة أخرى أو أُلغيت.", "The credit note cannot be voided: trip {0} was invoiced again or voided."),
        [ErrorCodes.CreditTripChanged] = ("لا يمكن إلغاء الإشعار: الرحلة {0} عُدّلت بعد إرجاعها.", "The credit note cannot be voided: trip {0} was changed after it was returned."),
        [ErrorCodes.EInvoiceNotEnabled] = ("الفوترة الإلكترونية غير مفعّلة لهذه الشركة؛ فعّلها من الإعدادات.", "E-invoicing is not enabled for this company; enable it in the settings."),
        [ErrorCodes.EInvoiceSellerTaxNumber] = ("الرقم الضريبي للبائع في إعدادات الفوترة الإلكترونية مفقود أو غير صحيح (أرقام فقط).", "The seller tax number in the e-invoicing settings is missing or invalid (digits only)."),
        [ErrorCodes.EInvoiceIncomeSource] = ("تسلسل مصدر الدخل في إعدادات الفوترة الإلكترونية مفقود أو غير صحيح (أرقام فقط).", "The income-source sequence in the e-invoicing settings is missing or invalid (digits only)."),
        [ErrorCodes.EInvoiceCredentials] = ("بيانات الربط (معرّف العميل والمفتاح السري) غير محفوظة في إعدادات الفوترة الإلكترونية.", "The API credentials (client id and secret key) are not saved in the e-invoicing settings."),
        [ErrorCodes.EInvoiceSellerName] = ("اسم الشركة مطلوب للفوترة الإلكترونية.", "The company name is required for e-invoicing."),
        [ErrorCodes.EInvoiceBuyerName] = ("اسم العميل مطلوب للفوترة الإلكترونية.", "The customer name is required for e-invoicing."),
        [ErrorCodes.EInvoiceBuyerTaxNumber] = ("الرقم الضريبي للعميل غير صحيح (أرقام فقط)؛ صحّحه في بطاقة العميل ثم أعد الإرسال.", "The customer tax number is invalid (digits only); correct it on the customer and submit again."),
        [ErrorCodes.EInvoiceIncomeWithTax] = ("نوع التسجيل «دخل» لا يسمح بفواتير عليها ضريبة مبيعات؛ راجع نوع التسجيل أو نسبة الضريبة.", "The income registration type cannot issue invoices with sales tax; check the registration type or the tax rate."),
        [ErrorCodes.EInvoiceSpecialNotSupported] = ("فواتير الضريبة الخاصة غير مدعومة في هذا الإصدار.", "Special sales tax invoices are not supported in this version."),
        [ErrorCodes.EInvoiceNoLines] = ("لا يحتوي المستند على بنود قابلة للإرسال.", "The document has no lines to submit."),
        [ErrorCodes.EInvoiceTotals] = ("مجاميع المستند لا تطابق بنوده؛ لم يُرسل.", "The document totals do not match its lines; it was not sent."),
        [ErrorCodes.EInvoiceWaitingForInvoice] = ("يُرسل الإشعار الدائن بعد قبول الفاتورة الأصلية في نظام الفوترة الإلكترونية.", "The credit note is sent after the original invoice is accepted by the e-invoicing system."),
        [ErrorCodes.EInvoiceNotSubmittable] = ("لا يمكن إرسال مستند حالته {0}.", "A document with status {0} cannot be submitted."),
        [ErrorCodes.EInvoiceBusy] = ("المستند قيد الإرسال الآن؛ أعد المحاولة بعد قليل.", "The document is being submitted now; try again shortly."),
        [ErrorCodes.EInvoiceConnectorUnavailable] = ("الموصل المختار غير متاح في هذه البيئة.", "The selected connector is not available in this environment."),
        [ErrorCodes.CreditEInvoiced] = ("قبل نظام الفوترة الإلكترونية الإشعار الدائن {0}؛ لا يمكن إلغاؤه.", "Credit note {0} was accepted by the e-invoicing system; it cannot be voided."),
        [ErrorCodes.EInvoiceOutcomeUnknown] = ("قد يكون نظام الفوترة الإلكترونية استلم هذا المستند (انقطاع أو مهلة أثناء الإرسال). أعد الإرسال أولاً لمعرفة النتيجة ثم ألغِ إن لزم.", "The e-invoicing system may have received this document (a failure or timeout during sending). Submit it again first to learn the outcome, then void if needed."),
        [ErrorCodes.EInvoicePendingDocuments] = ("لا يمكن تغيير الموصل أو البيئة ومستندات بانتظار الإرسال أو مرفوضة؛ أرسلها أو ألغها أولاً.", "The connector or environment cannot change while documents are queued or rejected; submit or cancel them first."),
        [ErrorCodes.PrintUnavailable] = ("محرك الطباعة غير متاح الآن. أعد المحاولة؛ لم يُسجَّل أي طباعة.", "The print engine is not available now. Try again; no print was recorded."),
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

    public const string BuiltInFixed = "masters.builtInFixed";
    public const string FieldCore = "field.core";
    public const string FieldAlwaysRequired = "field.alwaysRequired";
    public const string FieldUnknown = "field.unknown";
    public const string CustomFieldHasData = "customField.hasData";
    public const string CustomFieldTypeFixed = "customField.typeFixed";
    public const string CustomFieldOptions = "customField.options";
    public const string CustomFieldOptionInUse = "customField.optionInUse";
    public const string DriverPartnerOwnFleet = "driver.partnerOwnFleet";
    public const string FieldConditional = "field.conditional";
    public const string FieldCannotRequire = "field.cannotRequire";
    public const string OwnershipFleetInUse = "ownership.fleetInUse";

    public const string TariffDuplicate = "tariff.duplicate";
    public const string TariffDuplicateInFile = "tariff.duplicateInFile";
    public const string RatesInUse = "rates.inUse";
    public const string RatesMonthHasTrips = "rates.monthHasTrips";
    public const string RateMissing = "rate.missing";
    public const string RateManualNotAllowed = "rate.manualNotAllowed";

    public const string DateFuture = "validation.futureDate";
    public const string TripReceiptDuplicate = "trip.receiptDuplicate";
    public const string TripInvoiced = "trip.invoiced";
    public const string TripSettled = "trip.settled";
    public const string TripIsVoid = "trip.void";
    public const string TariffLocked = "tariff.locked";
    public const string TripWarnCapacity = "trip.warnCapacity";
    public const string TripWarnUnloaded = "trip.warnUnloaded";
    public const string TripWarnDocument = "trip.warnDocument";

    public const string InvoiceNotDraft = "invoice.notDraft";
    public const string InvoiceIsDraft = "invoice.isDraft";
    public const string InvoiceIsVoid = "invoice.void";
    public const string InvoiceTripUnavailable = "invoice.tripUnavailable";
    public const string InvoiceTotalZero = "invoice.totalZero";
    public const string InvoiceHasPayments = "invoice.hasPayments";
    public const string InvoiceEInvoiced = "invoice.eInvoiced";
    public const string CreditInvoiceNotApproved = "credit.invoiceNotApproved";
    public const string CreditLineReturned = "credit.lineReturned";
    public const string CreditExceedsRemaining = "credit.exceedsRemaining";
    public const string CreditIsVoid = "credit.void";
    public const string CreditTripReinvoiced = "credit.tripReinvoiced";
    public const string CreditTripChanged = "credit.tripChanged";
    public const string EInvoiceNotEnabled = "einvoice.notEnabled";
    public const string EInvoiceSellerTaxNumber = "einvoice.sellerTaxNumber";
    public const string EInvoiceIncomeSource = "einvoice.incomeSource";
    public const string EInvoiceCredentials = "einvoice.credentials";
    public const string EInvoiceSellerName = "einvoice.sellerName";
    public const string EInvoiceBuyerName = "einvoice.buyerName";
    public const string EInvoiceBuyerTaxNumber = "einvoice.buyerTaxNumber";
    public const string EInvoiceIncomeWithTax = "einvoice.incomeWithTax";
    public const string EInvoiceSpecialNotSupported = "einvoice.specialNotSupported";
    public const string EInvoiceNoLines = "einvoice.noLines";
    public const string EInvoiceTotals = "einvoice.totals";
    public const string EInvoiceWaitingForInvoice = "einvoice.waitingForInvoice";
    public const string EInvoiceNotSubmittable = "einvoice.notSubmittable";
    public const string EInvoiceBusy = "einvoice.busy";
    public const string EInvoiceConnectorUnavailable = "einvoice.connectorUnavailable";
    public const string CreditEInvoiced = "credit.eInvoiced";
    public const string EInvoiceOutcomeUnknown = "einvoice.outcomeUnknown";
    public const string EInvoicePendingDocuments = "einvoice.pendingDocuments";
    public const string PrintUnavailable = "print.unavailable";
}
