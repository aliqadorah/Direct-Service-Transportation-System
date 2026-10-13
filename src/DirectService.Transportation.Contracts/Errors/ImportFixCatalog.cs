namespace DirectService.Transportation.Contracts.Errors;

/// <summary>
/// How to fix a rejected import row, by problem code (owner feedback 13 Oct 2026): shown next to the problem in the import preview, in
/// Arabic and English. <see cref="Date"/> and <see cref="Number"/> are fixes for unreadable dates and numbers; any other code without its
/// own fix gets <see cref="Default"/>.
/// </summary>
public static class ImportFixCatalog
{
    public const string Date = "fix.date";
    public const string Number = "fix.number";
    public const string Default = "fix.default";

    private static readonly Dictionary<string, (string Ar, string En)> Fixes = new(StringComparer.Ordinal)
    {
        [Default] = ("صحّح القيمة كما توضح الرسالة ثم ارفع الملف مرة أخرى.", "Correct the value as the message says, then upload the file again."),
        [Date] = ("اكتب التاريخ بالصيغة yyyy-mm-dd (مثل 2026-10-12) أو dd/mm/yyyy.", "Write the date as yyyy-mm-dd (e.g. 2026-10-12) or dd/mm/yyyy."),
        [Number] = ("اكتب رقماً فقط بنقطة للكسور (مثل 31.850) بلا وحدات أو حروف.", "Write a number only, with a dot for decimals (e.g. 31.850), without units or letters."),
        [ErrorCodes.ImportDuplicate] = ("يجب ألا يتكرر الرقم: احذف الصف المكرر أو غيّر رقمه. الأرقام المسجلة في النظام لا تُستورد مرة أخرى.",
            "Each number may appear only once: remove the repeated row or change its number. Numbers already in the system cannot be imported again."),
        [ErrorCodes.TripReceiptDuplicate] = ("رقم السند مسجل لرحلة أخرى: غيّر الرقم أو احذف الصف.", "This receipt number belongs to another trip: change the number or remove the row."),
        [ErrorCodes.ImportUnknown] = ("اكتب الاسم كما هو في البيانات الأساسية (السجلات النشطة فقط) أو أضفه هناك أولاً. يمكن كتابة السائق برقمه الوطني أو رقم اللوحة.",
            "Write the name exactly as it is in the master data (active records only), or add it there first. A driver can also be given by national ID or plate."),
        [ErrorCodes.ImportInvalid] = ("صحّح القيمة في هذه الخلية بحسب نوع العمود (راجع ورقة الشرح في القالب).", "Correct the value in this cell to match the column's type (see the notes sheet of the template)."),
        [ErrorCodes.ValidationRequired] = ("املأ هذا العمود لهذا الصف.", "Fill in this column for the row."),
        [ErrorCodes.ValidationFormat] = ("اكتب القيمة بالصيغة المطلوبة لهذا العمود.", "Write the value in the format this column needs."),
        [ErrorCodes.ValidationRange] = ("أدخل قيمة ضمن الحدود: الأوزان أكبر من صفر وبثلاث خانات عشرية على الأكثر، والنص ضمن طوله الأقصى، وتاريخ التنزيل ليس قبل التحميل.",
            "Enter a value within the limits: weights above zero with at most 3 decimals, text within its maximum length, unloading not before loading."),
        [ErrorCodes.DateFuture] = ("لا يمكن أن يكون التاريخ بعد اليوم؛ صحّحه.", "The date cannot be after today; correct it."),
        [ErrorCodes.PeriodClosed] = ("التاريخ في فترة محاسبية مغلقة: استخدم تاريخاً في شهر مفتوح أو اطلب من المحاسب إعادة فتح الفترة.",
            "The date is in a closed accounting period: use a date in an open month, or ask an accountant to reopen the period."),
        [ErrorCodes.TariffLocked] = ("سعر العميل يجب أن يساوي سعر التعرفة: اترك خلية السعر فارغة لاستخدام التعرفة.", "The customer price must equal the tariff price: leave the price cell empty to use the tariff."),
        [ErrorCodes.RateMissing] = ("أدخل أجرة السائق في الصف، أو أدخل الأجرة الشهرية لموقع التحميل لذلك الشهر أولاً (البيانات الأساسية ← الأجور الشهرية).",
            "Enter the driver rate in the row, or first set the monthly rate of the loading site for that month (Master data → Monthly rates)."),
        [ErrorCodes.RateManualNotAllowed] = ("اترك أجرة السائق فارغة لاستخدام الأجرة الشهرية؛ إدخال أجرة مختلفة لمن يدير البيانات الأساسية فقط.",
            "Leave the driver rate empty to use the monthly rate; only users who manage master data may enter a different rate."),
        [ErrorCodes.DriverPartnerOwnFleet] = ("احذف الشريك من الصف: شاحنة هذا السائق من أسطول الشركة.", "Remove the partner from the row: this driver's truck is in the company's own fleet."),
    };

    /// <summary>The fix for a problem code (or a fix key) in the given language ("en" or Arabic).</summary>
    public static string For(string code, string language)
    {
        var (ar, en) = Fixes.TryGetValue(code, out var f) ? f : Fixes[Default];
        return language == "en" ? en : ar;
    }

    /// <summary>The codes and keys that have their own fix (for the test that every fix has both languages).</summary>
    public static IReadOnlyDictionary<string, (string Ar, string En)> All => Fixes;
}
