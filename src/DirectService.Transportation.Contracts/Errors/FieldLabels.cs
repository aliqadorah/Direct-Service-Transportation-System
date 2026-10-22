using System.Globalization;
using System.Text.RegularExpressions;

namespace DirectService.Transportation.Contracts.Errors;

/// <summary>
/// Display labels of the request fields that error messages name (DT-241): a message never shows an internal field name
/// ("entryIds", "lines[2].accountId") or a raw code value ("ApArParty") but the field's label in the reader's language
/// ("Entries", "Line 3 · Account"). <see cref="ErrorCatalog.Format"/> applies it to every message argument, so the API's problem details,
/// the account pages and import problems all go through this one place. A test requires a label for every field name the services use.
/// </summary>
public static partial class FieldLabels
{
    private static readonly Dictionary<string, (string Ar, string En)> Names = new(StringComparer.Ordinal)
    {
        ["acceptTerms"] = ("الموافقة على الشروط", "Accept the terms"),
        ["account"] = ("الحساب", "Account"),
        ["accountId"] = ("الحساب", "Account"),
        ["accountNumber"] = ("رقم الحساب", "Account number"),
        ["accountType"] = ("نوع الحساب", "Account type"),
        ["accounts"] = ("الحسابات", "Accounts"),
        ["address"] = ("العنوان", "Address"),
        ["advanceIds"] = ("السلف", "Advances"),
        ["allocations"] = ("التوزيع على الفواتير", "Invoice allocations"),
        ["amount"] = ("المبلغ", "Amount"),
        ["annexColumns"] = ("أعمدة الملحق", "Annex columns"),
        ["apArPartyId"] = ("الطرف", "Party"),
        ["applied"] = ("المطبَّق", "Applied"),
        ["area"] = ("المنطقة", "Area"),
        ["attachmentsCount"] = ("عدد المرفقات", "Number of attachments"),
        ["bandText"] = ("نص الشريط", "Band text"),
        ["bankAccount"] = ("الحساب البنكي", "Bank account"),
        ["bankAccountId"] = ("الحساب البنكي", "Bank account"),
        ["bankName"] = ("اسم البنك", "Bank name"),
        ["billing"] = ("الفوترة", "Billing"),
        ["billingCycle"] = ("دورة الفوترة", "Billing cycle"),
        ["cashBox"] = ("الصندوق", "Cash box"),
        ["cashBoxId"] = ("الصندوق", "Cash box"),
        ["chartSource"] = ("مصدر دليل الحسابات", "Chart of accounts source"),
        ["clientId"] = ("معرّف الطلب", "Request reference"),
        ["cliqAlias"] = ("اسم كليك المستعار", "CliQ alias"),
        ["code"] = ("الرمز", "Code"),
        ["codeFrom"] = ("من رمز", "Code from"),
        ["columns"] = ("الأعمدة", "Columns"),
        ["connector"] = ("الربط بين الشروط", "Condition joining"),
        ["commercialRegNo"] = ("رقم السجل التجاري", "Commercial registration number"),
        ["compareFrom"] = ("المقارنة من", "Compare from"),
        ["confirmation"] = ("كلمة التأكيد", "Confirmation word"),
        ["contactPerson"] = ("الشخص المسؤول", "Contact person"),
        ["controlAccountId"] = ("الحساب الرقابي", "Control account"),
        ["controlPartyType"] = ("نوع الطرف الرقابي", "Control party type"),
        ["costCentre"] = ("مركز التكلفة", "Cost centre"),
        ["costCentreId"] = ("مركز التكلفة", "Cost centre"),
        ["countryCode"] = ("الدولة", "Country"),
        ["credit"] = ("الدائن", "Credit"),
        ["creditDate"] = ("تاريخ الإشعار", "Credit note date"),
        ["currencyCode"] = ("العملة", "Currency"),
        ["currencyDecimals"] = ("منازل العملة العشرية", "Currency decimals"),
        ["currentPassword"] = ("كلمة المرور الحالية", "Current password"),
        ["customValues"] = ("الحقول الإضافية", "Additional fields"),
        ["customer"] = ("العميل", "Customer"),
        ["customerId"] = ("العميل", "Customer"),
        ["customerPrice"] = ("سعر العميل", "Customer price"),
        ["dateFormat"] = ("صيغة التاريخ", "Date format"),
        ["days"] = ("الأيام", "Days"),
        ["debit"] = ("المدين", "Debit"),
        ["deduction"] = ("الخصم", "Deduction"),
        ["deductions"] = ("الخصومات", "Deductions"),
        ["defaultCompany"] = ("الشركة الافتراضية", "Default company"),
        ["defaultValue"] = ("القيمة الافتراضية", "Default value"),
        ["definition"] = ("تعريف التقرير", "Report definition"),
        ["description"] = ("البيان", "Description"),
        ["digits"] = ("عدد الخانات", "Digits"),
        ["discount"] = ("الخصم", "Discount"),
        ["document"] = ("المستند", "Document"),
        ["documentId"] = ("المستند", "Document"),
        ["documentType"] = ("نوع المستند", "Document type"),
        ["driver"] = ("السائق", "Driver"),
        ["driverId"] = ("السائق", "Driver"),
        ["driverRate"] = ("أجرة السائق", "Driver rate"),
        ["dueDate"] = ("تاريخ الاستحقاق", "Due date"),
        ["email"] = ("البريد الإلكتروني", "Email"),
        ["endsAt"] = ("ينتهي في", "Ends at"),
        ["entryDate"] = ("تاريخ القيد", "Entry date"),
        ["entryIds"] = ("القيود المختارة", "Selected entries"),
        ["environment"] = ("البيئة", "Environment"),
        ["expiry"] = ("تاريخ الانتهاء", "Expiry"),
        ["externalFleetRate"] = ("سعر الأسطول الخارجي", "External fleet rate"),
        ["extraFees"] = ("الرسوم الإضافية", "Extra fees"),
        ["extraFeesDescription"] = ("بيان الرسوم الإضافية", "Extra fees description"),
        ["field"] = ("الحقل", "Field"),
        ["fieldKey"] = ("الحقل", "Field"),
        ["fieldType"] = ("نوع الحقل", "Field type"),
        ["fields"] = ("الحقول", "Fields"),
        ["fiscalYear"] = ("السنة المالية", "Fiscal year"),
        ["fleet"] = ("الأسطول", "Fleet"),
        ["fleetClass"] = ("فئة الأسطول", "Fleet class"),
        ["footerText"] = ("نص التذييل", "Footer text"),
        ["forTrip"] = ("للرحلة", "For trip"),
        ["format"] = ("الصيغة", "Format"),
        ["from"] = ("من", "From"),
        ["fullName"] = ("الاسم الكامل", "Full name"),
        ["groupBy"] = ("التجميع حسب", "Group by"),
        ["grouping"] = ("التجميع", "Grouping"),
        ["helpText"] = ("نص المساعدة", "Help text"),
        ["iban"] = ("رقم الآيبان", "IBAN"),
        ["ibanPattern"] = ("نمط رقم الآيبان", "IBAN pattern"),
        ["id"] = ("الاختيار", "Selection"),
        ["incomeSourceSequence"] = ("تسلسل مصدر الدخل", "Income source sequence"),
        ["increasePercent"] = ("نسبة الزيادة", "Increase percentage"),
        ["invoice"] = ("الفاتورة", "Invoice"),
        ["invoiceDate"] = ("تاريخ الفاتورة", "Invoice date"),
        ["invoiceId"] = ("الفاتورة", "Invoice"),
        ["invoiceLineId"] = ("سطر الفاتورة", "Invoice line"),
        ["invoiceType"] = ("نوع الفاتورة", "Invoice type"),
        ["isPostable"] = ("قابل للترحيل", "Postable"),
        ["kind"] = ("النوع", "Kind"),
        ["labelAr"] = ("التسمية بالعربية", "Arabic label"),
        ["labelEn"] = ("التسمية بالإنجليزية", "English label"),
        ["labels"] = ("التسميات", "Labels"),
        ["languageCode"] = ("اللغة", "Language"),
        ["layoutType"] = ("نوع النموذج", "Layout type"),
        ["level"] = ("المستوى", "Level"),
        ["lines"] = ("السطور", "Lines"),
        ["loadingDate"] = ("تاريخ التحميل", "Loading date"),
        ["loadingSite"] = ("موقع التحميل", "Loading site"),
        ["loadingSiteId"] = ("موقع التحميل", "Loading site"),
        ["logo"] = ("الشعار", "Logo"),
        ["mapping"] = ("ربط الحسابات", "Account mapping"),
        ["material"] = ("المادة", "Material"),
        ["maxActiveUsers"] = ("أقصى عدد للمستخدمين النشطين", "Maximum active users"),
        ["maxCompanies"] = ("أقصى عدد للشركات", "Maximum companies"),
        ["maxStorageMb"] = ("أقصى مساحة تخزين", "Maximum storage"),
        ["maxTripsPerMonth"] = ("أقصى عدد للرحلات شهرياً", "Maximum trips per month"),
        ["measure"] = ("المقياس", "Measure"),
        ["message"] = ("الرسالة", "Message"),
        ["method"] = ("الطريقة", "Method"),
        ["mode"] = ("الوضع", "Mode"),
        ["month"] = ("الشهر", "Month"),
        ["months"] = ("مبالغ الأشهر", "Monthly amounts"),
        ["name"] = ("الاسم", "Name"),
        ["nameAr"] = ("الاسم بالعربية", "Arabic name"),
        ["nameEn"] = ("الاسم بالإنجليزية", "English name"),
        ["newEmail"] = ("البريد الإلكتروني الجديد", "New email"),
        ["nextNumber"] = ("الرقم التالي", "Next number"),
        ["note"] = ("الملاحظة", "Note"),
        ["notes"] = ("الملاحظات", "Notes"),
        ["operator"] = ("الشرط", "Condition"),
        ["options"] = ("الخيارات", "Options"),
        ["organisationName"] = ("اسم الجهة", "Organisation name"),
        ["ownFleetRate"] = ("سعر الأسطول الخاص", "Own fleet rate"),
        ["paid"] = ("المدفوع", "Paid"),
        ["partner"] = ("الشريك", "Partner"),
        ["partnerId"] = ("الشريك", "Partner"),
        ["partnerRate"] = ("أجرة الشريك", "Partner rate"),
        ["party"] = ("الطرف", "Party"),
        ["partyId"] = ("الطرف", "Party"),
        ["partyType"] = ("نوع الطرف", "Party type"),
        ["password"] = ("كلمة المرور", "Password"),
        ["passwordMinLength"] = ("أقل طول لكلمة المرور", "Minimum password length"),
        ["payment"] = ("الدفعة", "Payment"),
        ["paymentDate"] = ("تاريخ الدفع", "Payment date"),
        ["paymentMethod"] = ("طريقة الدفع", "Payment method"),
        ["paymentTermsDays"] = ("مدة السداد بالأيام", "Payment terms (days)"),
        ["period"] = ("الفترة", "Period"),
        ["periodFrom"] = ("الفترة من", "Period from"),
        ["periodMonth"] = ("شهر الفترة", "Period month"),
        ["periodTo"] = ("الفترة إلى", "Period to"),
        ["permissions"] = ("الصلاحيات", "Permissions"),
        ["phone"] = ("الهاتف", "Phone"),
        ["phonePattern"] = ("نمط رقم الهاتف", "Phone pattern"),
        ["pinIds"] = ("المؤشرات المثبّتة", "Pinned indicators"),
        ["planId"] = ("الخطة", "Plan"),
        ["prefix"] = ("البادئة", "Prefix"),
        ["price"] = ("السعر", "Price"),
        ["q"] = ("نص البحث", "Search text"),
        ["quantity"] = ("الكمية", "Quantity"),
        ["reason"] = ("السبب", "Reason"),
        ["receiptNo"] = ("رقم الإيصال", "Receipt number"),
        ["reference"] = ("المرجع", "Reference"),
        ["refundedOn"] = ("تاريخ الاسترداد", "Refunded on"),
        ["registrationType"] = ("نوع التسجيل", "Registration type"),
        ["repaidAmount"] = ("المبلغ المسدَّد", "Repaid amount"),
        ["report"] = ("التقرير", "Report"),
        ["returnDate"] = ("تاريخ الإرجاع", "Return date"),
        ["role"] = ("الدور", "Role"),
        ["rows"] = ("الصفوف", "Rows"),
        ["rowVersion"] = ("نسخة السجل", "Record version"),
        ["savedReportId"] = ("التقرير المحفوظ", "Saved report"),
        ["scope"] = ("النطاق", "Scope"),
        ["secretKey"] = ("المفتاح السري", "Secret key"),
        ["sellerTaxNumber"] = ("الرقم الضريبي للبائع", "Seller tax number"),
        ["sessionIdleTimeoutMinutes"] = ("مهلة الخمول بالدقائق", "Idle timeout (minutes)"),
        ["setTaxRateToZero"] = ("جعل نسبة الضريبة صفراً", "Set the tax rate to zero"),
        ["settled"] = ("المسوّى", "Settled"),
        ["shortageTolerancePct"] = ("نسبة السماح بالنقص", "Shortage tolerance"),
        ["side"] = ("الجانب", "Side"),
        ["signatures"] = ("التواقيع", "Signatures"),
        ["sites"] = ("المواقع", "Sites"),
        ["sort"] = ("الترتيب", "Sort"),
        ["source"] = ("المصدر", "Source"),
        ["status"] = ("الحالة", "Status"),
        ["supplier"] = ("المورد", "Supplier"),
        ["taxNumber"] = ("الرقم الضريبي", "Tax number"),
        ["taxRate"] = ("نسبة الضريبة", "Tax rate"),
        ["tenantId"] = ("المشترك", "Tenant"),
        ["timeZone"] = ("المنطقة الزمنية", "Time zone"),
        ["title"] = ("العنوان", "Title"),
        ["to"] = ("إلى", "To"),
        ["tripDate"] = ("تاريخ الرحلة", "Trip date"),
        ["tripIds"] = ("الرحلات", "Trips"),
        ["type"] = ("النوع", "Type"),
        ["unit"] = ("الوحدة", "Unit"),
        ["unitPrice"] = ("سعر الوحدة", "Unit price"),
        ["unloadingSite"] = ("موقع التنزيل", "Unloading site"),
        ["user"] = ("المستخدم", "User"),
        ["userName"] = ("اسم المستخدم", "Username"),
        ["validTo"] = ("صالح حتى", "Valid to"),
        ["values"] = ("القيم", "Values"),
        ["viewKey"] = ("العرض", "View"),
        ["warningThreshold"] = ("حد التنبيه", "Warning threshold"),
        ["year"] = ("السنة", "Year"),
        // Party types (code values a refusal may name).
        ["Customer"] = ("العميل", "Customer"),
        ["Driver"] = ("السائق", "Driver"),
        ["Partner"] = ("الشريك", "Partner"),
        ["ApArParty"] = ("الطرف المحاسبي", "Accounting party"),
    };

    /// <summary>One item of a list field ("lines[2]" → "Line 3").</summary>
    private static readonly Dictionary<string, (string Ar, string En)> Items = new(StringComparer.Ordinal)
    {
        ["lines"] = ("السطر {0}", "Line {0}"),
        ["deductions"] = ("الخصم {0}", "Deduction {0}"),
        ["sites"] = ("الموقع {0}", "Site {0}"),
        ["allocations"] = ("التوزيع {0}", "Allocation {0}"),
        ["tripIds"] = ("الرحلة {0}", "Trip {0}"),
        ["advanceIds"] = ("السلفة {0}", "Advance {0}"),
        ["entryIds"] = ("القيد {0}", "Entry {0}"),
        ["rows"] = ("الصف {0}", "Row {0}"),
        ["columns"] = ("العمود {0}", "Column {0}"),
        ["filters"] = ("الشرط {0}", "Condition {0}"),
    };

    [GeneratedRegex(@"^(?<name>[A-Za-z][A-Za-z0-9]*)(?:\[(?<index>\d{1,6})\])?$", RegexOptions.CultureInvariant)]
    private static partial Regex Segment();

    /// <summary>Whether a field name or path has a label (every segment known).</summary>
    public static bool Has(string field) => TryLabel(field, "en", out _);

    /// <summary>The label of a field name or path ("lines[2].accountId" → "Line 3 · Account"); false when any part is unknown.</summary>
    public static bool TryLabel(string? field, string language, out string label)
    {
        label = "";
        if (string.IsNullOrEmpty(field) || field.Length > 120) return false;
        var en = string.Equals(language, "en", StringComparison.OrdinalIgnoreCase);
        if (Names.TryGetValue(field, out var whole)) { label = en ? whole.En : whole.Ar; return true; }
        if (!field.Contains('.', StringComparison.Ordinal) && !field.Contains('[', StringComparison.Ordinal)) return false;
        var parts = new List<string>();
        var segments = field.Split('.');
        for (var i = 0; i < segments.Length; i++)
        {
            var m = Segment().Match(segments[i]);
            if (!m.Success) return false;
            var name = m.Groups["name"].Value;
            if (m.Groups["index"].Success)
            {
                if (!Items.TryGetValue(name, out var item)) return false;
                var n = int.Parse(m.Groups["index"].Value, CultureInfo.InvariantCulture) + 1;
                parts.Add(string.Format(CultureInfo.InvariantCulture, en ? item.En : item.Ar, n));
            }
            else if (Names.TryGetValue(name, out var l)) parts.Add(en ? l.En : l.Ar);
            else return false;
        }
        label = string.Join(" · ", parts);
        return true;
    }

    /// <summary>Every field name with a label (tests).</summary>
    public static IEnumerable<string> Known => Names.Keys;
}
