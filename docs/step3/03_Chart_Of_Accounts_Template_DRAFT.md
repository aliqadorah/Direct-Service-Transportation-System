# Built-in Transport Chart of Accounts — DRAFT for owner approval (D-10)

_Status: **waiting for owner approval. Not seeded.** Prepared 8 Oct 2026. FR-107 requires a built-in transport chart template for new companies (also offered in onboarding step 4 and REL-09). The PRD does not list its accounts; per D-10 I draft it here. After your approval it is created for a company when the Owner/Administrator picks "Start from the transport template" (it is per-company data, not a migration seed)._

Conventions: numeric codes by level (1 digit class · 2 group · 4 account · 6 sub-account); only leaf accounts are postable (FR-107/108); a child has its parent's type (design rule); Arabic and English names both filled (D-07); control accounts carry the party type their lines must name (FR-106, G-17).

## 1. Accounts

| Code | Arabic name | English name | Type | Postable | Control (party) |
|---|---|---|---|---|---|
| 1 | الأصول | Assets | Asset | No | |
| 11 | الأصول المتداولة | Current assets | Asset | No | |
| 1101 | النقدية في الصناديق | Cash on hand | Asset | No | |
| 110101 | الصندوق الرئيسي | Main cash box | Asset | Yes | |
| 1102 | النقدية لدى البنوك | Cash at banks | Asset | No | |
| 110201 | البنك – الحساب الجاري | Bank – current account | Asset | Yes | |
| 1103 | الذمم المدينة | Receivables | Asset | No | |
| 110301 | ذمم عملاء النقل | Trade receivables – transport customers | Asset | Yes | Customer |
| 110302 | ذمم مدينة أخرى | Other receivables | Asset | Yes | Customer |
| 1104 | سلف السائقين | Driver advances | Asset | Yes | Driver |
| 1105 | ضريبة المبيعات على المشتريات (مدخلات) | Sales tax input | Asset | Yes | |
| 1106 | مصاريف مدفوعة مقدماً | Prepaid expenses | Asset | Yes | |
| 12 | الأصول غير المتداولة | Non-current assets | Asset | No | |
| 1201 | الشاحنات والمركبات | Trucks and vehicles | Asset | Yes | |
| 1202 | الأثاث والأجهزة والمعدات | Furniture and equipment | Asset | Yes | |
| 1203 | مجمع الإهلاك | Accumulated depreciation | Asset | Yes | |
| 2 | الالتزامات | Liabilities | Liability | No | |
| 21 | الالتزامات المتداولة | Current liabilities | Liability | No | |
| 2101 | ذمم الموردين | Suppliers payable | Liability | Yes | Supplier |
| 2102 | ذمم السائقين الدائنة | Driver payable | Liability | Yes | Driver |
| 2103 | ذمم شركات النقل الشريكة | Partner payable | Liability | Yes | Partner |
| 2104 | ضريبة المبيعات المستحقة (مخرجات) | Sales tax output | Liability | Yes | |
| 2105 | مصاريف مستحقة | Accrued expenses | Liability | Yes | |
| 3 | حقوق الملكية | Equity | Equity | No | |
| 3101 | رأس المال | Capital | Equity | Yes | |
| 3102 | الأرباح المحتجزة | Retained earnings | Equity | Yes | |
| 3103 | فرق الأرصدة الافتتاحية | Opening balance difference | Equity | Yes | |
| 4 | الإيرادات | Revenue | Revenue | No | |
| 4101 | إيرادات النقل | Transport revenue | Revenue | Yes | |
| 4102 | إيرادات أخرى (رسوم إضافية) | Other revenue (extra fees) | Revenue | Yes | |
| 4103 | إيرادات خصومات السائقين | Driver deductions income | Revenue | Yes | |
| 4104 | الخصم المسموح به | Discount allowed | Revenue | Yes | |
| 5 | المصروفات | Expenses | Expense | No | |
| 51 | تكاليف النقل المباشرة | Direct transport costs | Expense | No | |
| 5101 | أجور السائقين | Driver wage expense | Expense | Yes | |
| 5102 | تكلفة النقل لدى الشركاء | Partner (subcontracted haulage) cost | Expense | Yes | |
| 5103 | الوقود | Fuel | Expense | Yes | |
| 5104 | صيانة الشاحنات وقطع الغيار | Truck maintenance and spare parts | Expense | Yes | |
| 5105 | تأمين وترخيص الشاحنات | Truck insurance and licensing | Expense | Yes | |
| 52 | المصاريف الإدارية والعمومية | General and administrative expenses | Expense | No | |
| 5201 | الرواتب والأجور الإدارية | Administrative salaries and wages | Expense | Yes | |
| 5202 | الإيجارات | Rent | Expense | Yes | |
| 5203 | الاتصالات والإنترنت | Communications and internet | Expense | Yes | |
| 5204 | الرسوم والاشتراكات | Fees and subscriptions | Expense | Yes | |
| 5205 | مصروف الإهلاك | Depreciation expense | Expense | Yes | |
| 5206 | العمولات والمصاريف البنكية | Bank charges | Expense | Yes | |
| 5299 | مصاريف أخرى | Other expenses | Expense | Yes | |

Also created with the template: cash box **"الصندوق الرئيسي / Main cash box"** linked to 110101 (FR-129). Bank-account records are created by the user (bank name and IBAN are company-specific); 110201 is ready to link to the first one.

## 2. Default posting-key mapping (FR-092, 7.5.1)

Posting-key names (my naming, design convention `MODULE.Key`): transport keys use `TRN.`; keys shared with Accounting documents (sales tax, opening difference) use `ACC.`.

| Posting key (7.5.1) | Key | Account | Required for invoicing/posting |
|---|---|---|---|
| Driver wage expense | TRN.DriverWageExpense | 5101 | Yes |
| Driver payable | TRN.DriverPayable | 2102 | Yes |
| Partner cost expense | TRN.PartnerCostExpense | 5102 | Yes |
| Partner payable | TRN.PartnerPayable | 2103 | Yes |
| Trade receivables | TRN.TradeReceivables | 110301 | Yes |
| Discount allowed | TRN.DiscountAllowed | 4104 | Yes |
| Transport revenue | TRN.TransportRevenue | 4101 | Yes |
| Other revenue (extra fees) | TRN.OtherRevenue | 4102 | Yes |
| Sales tax output | ACC.SalesTaxOutput | 2104 | Yes |
| Sales tax input | ACC.SalesTaxInput | 1105 | Yes |
| Driver advances | TRN.DriverAdvances | 1104 | Yes |
| Driver deductions | TRN.DriverDeductions | 4103 | Yes |
| Opening balance difference (G-04) | ACC.OpeningBalanceDifference | 3103 | Only until data migration is marked complete |
| Cash boxes / bank accounts | (each cash box / bank account's own linked account) | 110101 / 110201 … | — |

## 3. Points for your review
1. **Discount allowed** is placed as a contra-revenue account (4104, debit balance) so the profit & loss shows net revenue. Alternative: an expense account under 52.
2. **Driver deductions** (shortages, fines) are credited to a revenue account (4103). Alternative: credit the driver wage expense 5101 (reduces cost instead of adding income).
3. **Accumulated depreciation** (1203) is an Asset-type account with a credit balance (the design requires a child to have its parent's type).
4. Any account you want added, renamed or renumbered — e.g. separate accounts per truck, VAT settlement account, owner current account.

Please reply "chart approved" or list the changes.
