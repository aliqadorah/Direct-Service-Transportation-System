"""Check constraints derived from docs/inputs/Database_Design.md (rev. 4 + rev. 5).

Only rules the design marks as CHECK become database constraints, plus ISJSON on every JSON
column (design §3.3 and the build prompt). "Required when …" rules without "(CHECK)" are
application rules and are NOT created here.

derive_checks(table) returns [(name, sql)]. Simple patterns are derived from the rule text;
multi-column formulas are listed explicitly in MANUAL.
"""
from __future__ import annotations

import re

# Columns whose rule text contains a CHECK that the simple patterns must NOT handle
# (they are covered by MANUAL below).
SKIP_AUTO = {
    ("TRN_Settlements", "NetPayable"),
    ("TRN_Invoices", "BaseAmount"),
    ("TRN_Invoices", "Total"),
    ("TRN_Invoices", "Discount"),
    ("JournalEntries", "TotalDebit"),
}

# Explicit multi-column / formula constraints: table -> [(name, sql)]
MANUAL: dict[str, list[tuple[str, str]]] = {
    "SubscriptionInvoices": [
        ("CK_SubscriptionInvoices_PeriodEnd", "[PeriodEnd] >= [PeriodStart]"),
        ("CK_SubscriptionInvoices_Total", "[Total] = [Amount] + [Tax]"),
    ],
    "Users": [
        ("CK_Users_IsPlatformStaff_TenantId",
         "([IsPlatformStaff] = 1 AND [TenantId] IS NULL) OR ([IsPlatformStaff] = 0 AND [TenantId] IS NOT NULL)"),
    ],
    "SupportAccessGrants": [
        ("CK_SupportAccessGrants_EndsAt", "[EndsAt] > [StartsAt]"),
    ],
    "Announcements": [
        ("CK_Announcements_EndsAt", "[EndsAt] > [StartsAt]"),
    ],
    "TRN_Tariffs": [
        ("CK_TRN_Tariffs_CustomerId_PartnerId",
         "([PartyType] = 'Customer' AND [PartnerId] IS NULL) OR ([PartyType] = 'Partner' AND [CustomerId] IS NULL)"),
        ("CK_TRN_Tariffs_ValidTo", "[ValidTo] >= [ValidFrom]"),
    ],
    "TRN_MonthlySiteRates": [
        ("CK_TRN_MonthlySiteRates_RateMonth", "[RateMonth] LIKE '[12][0-9][0-9][0-9]-[01][0-9]'"),
        ("CK_TRN_MonthlySiteRates_OwnFleetRate_ExternalFleetRate",
         "[OwnFleetRate] IS NOT NULL OR [ExternalFleetRate] IS NOT NULL"),
    ],
    "TRN_Trips": [
        ("CK_TRN_Trips_UnloadingDate", "[UnloadingDate] >= [LoadingDate]"),
    ],
    "TRN_Invoices": [
        ("CK_TRN_Invoices_DueDate", "[DueDate] >= [InvoiceDate]"),
        ("CK_TRN_Invoices_Discount", "[Discount] >= 0 AND [Discount] <= [Subtotal]"),
        ("CK_TRN_Invoices_BaseAmount", "[BaseAmount] = [Subtotal] - [Discount] + [ExtraFees]"),
        ("CK_TRN_Invoices_Total", "[Total] = [BaseAmount] + [Tax]"),
    ],
    "TRN_EInvoiceSubmissions": [
        ("CK_TRN_EInvoiceSubmissions_InvoiceId_CreditNoteId",
         "([DocumentType] = 'Invoice' AND [InvoiceId] IS NOT NULL AND [CreditNoteId] IS NULL) OR "
         "([DocumentType] = 'CreditNote' AND [CreditNoteId] IS NOT NULL AND [InvoiceId] IS NULL)"),
    ],
    "TRN_Settlements": [
        ("CK_TRN_Settlements_NetPayable",
         "[NetPayable] >= 0 AND [NetPayable] = [GrossWages] - [AdvancesTotal] - [DeductionsTotal]"),
    ],
    "Accounts": [
        ("CK_Accounts_OpeningDebit_OpeningCredit", "NOT ([OpeningDebit] > 0 AND [OpeningCredit] > 0)"),
    ],
    "JournalEntries": [
        ("CK_JournalEntries_TotalDebit_TotalCredit", "[TotalDebit] = [TotalCredit]"),
    ],
    "EntryLines": [
        ("CK_EntryLines_Debit_Credit", "([Debit] > 0 AND [Credit] = 0) OR ([Credit] > 0 AND [Debit] = 0)"),
    ],
    "ApArDocuments": [
        ("CK_ApArDocuments_DueDate", "[DueDate] >= [DocumentDate]"),
    ],
}

IN_RE = re.compile(r"CHECK IN \(([^)]*)\)")
CMP_RE = re.compile(r"CHECK (≥|>) 0")
BETWEEN_RE = re.compile(r"CHECK BETWEEN (\d+) AND (\d+)")


def is_json_column(col: dict) -> bool:
    if not col["type"].upper().startswith("NVARCHAR(MAX)"):
        return False
    text = (col["description"] + " " + col["rules"])
    if "JSON/XML" in text:  # e-invoice payloads may be XML (UBL 2.1)
        return False
    return "JSON" in text or "ISJSON" in text


def derive_checks(table: dict) -> list[tuple[str, str]]:
    name = table["name"]
    cols = {c["name"]: c for c in table["columns"]}
    out: list[tuple[str, str]] = []
    for c in table["columns"]:
        if c["std"] or (name, c["name"]) in SKIP_AUTO:
            continue
        r = c["rules"]
        col = c["name"]
        ck = f"CK_{name}_{col}"
        if m := IN_RE.search(r):
            out.append((ck, f"[{col}] IN ({m.group(1)})"))
        elif m := BETWEEN_RE.search(r):
            out.append((ck, f"[{col}] BETWEEN {m.group(1)} AND {m.group(2)}"))
        elif m := CMP_RE.search(r):
            op = ">=" if m.group(1) == "≥" else ">"
            out.append((ck, f"[{col}] {op} 0"))
        elif is_json_column(c):
            out.append((ck, f"ISJSON([{col}]) = 1"))
    # Void triple: "Required when Status = Void (CHECK)"
    if {"VoidReason", "VoidedBy", "VoidedAt", "Status"} <= cols.keys():
        out.append((f"CK_{name}_VoidReason_VoidedBy_VoidedAt",
                    "[Status] <> 'Void' OR ([VoidReason] IS NOT NULL AND [VoidedBy] IS NOT NULL AND [VoidedAt] IS NOT NULL)"))
    # Cash box / bank account by payment method (CHECK)
    if {"PaymentMethod", "CashBoxId", "BankAccountId"} <= cols.keys():
        out.append((f"CK_{name}_CashBoxId_BankAccountId",
                    "([PaymentMethod] = 'Cash' AND [CashBoxId] IS NOT NULL AND [BankAccountId] IS NULL) OR "
                    "([PaymentMethod] IN ('Bank','CliQ') AND [BankAccountId] IS NOT NULL AND [CashBoxId] IS NULL)"))
    # Standard soft-delete triple: "Required when IsDeleted = 1 (CHECK)"
    if {"IsDeleted", "DeletedAt", "DeletedBy", "DeleteReason"} <= cols.keys():
        out.append((f"CK_{name}_DeletedAt_DeletedBy_DeleteReason",
                    "[IsDeleted] = 0 OR ([DeletedAt] IS NOT NULL AND [DeletedBy] IS NOT NULL AND [DeleteReason] IS NOT NULL)"))
    out.extend(MANUAL.get(name, []))
    return out
