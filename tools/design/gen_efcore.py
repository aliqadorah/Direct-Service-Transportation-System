"""One-off generator: entity classes + IEntityTypeConfiguration classes from the design model.

Output becomes ordinary, hand-maintained source (src/...). Re-running overwrites the generated
files, so after the first commit any manual change must be made in the source, not here.

Usage:  python tools/design/gen_efcore.py
"""
from __future__ import annotations

import re
import sys
from pathlib import Path

sys.stdout.reconfigure(encoding="utf-8")
sys.path.insert(0, str(Path(__file__).parent))
from parse_design import load, ROOT  # noqa: E402
from checks import derive_checks  # noqa: E402

NS = "DirectService.Transportation"
DOMAIN_DIR = ROOT / "src" / f"{NS}.Domain"
CONF_DIR = ROOT / "src" / f"{NS}.Infrastructure" / "Persistence" / "Configurations"

SCHEMA_FOLDER = {"core": "Core", "trn": "Trn", "acc": "Acc", "cfg": "Cfg"}

CLASS_NAME = {
    "Plans": "Plan", "Tenants": "Tenant", "SubscriptionInvoices": "SubscriptionInvoice",
    "Companies": "Company", "Users": "User", "UserRecoveryCodes": "UserRecoveryCode",
    "UserSessions": "UserSession", "Permissions": "Permission", "Roles": "Role",
    "RolePermissions": "RolePermission", "UserCompanyRoles": "UserCompanyRole",
    "Invitations": "Invitation", "SupportAccessGrants": "SupportAccessGrant",
    "TermsAcceptances": "TermsAcceptance", "AuditLogs": "AuditLog", "Notifications": "Notification",
    "Announcements": "Announcement", "MaintenanceLogs": "MaintenanceLog",
    "TRN_OwnershipTypes": "OwnershipType", "TRN_Partners": "Partner", "TRN_Drivers": "Driver",
    "TRN_Customers": "Customer", "TRN_Materials": "Material", "TRN_LoadingSites": "LoadingSite",
    "TRN_UnloadingSites": "UnloadingSite", "TRN_Tariffs": "Tariff",
    "TRN_MonthlySiteRates": "MonthlySiteRate", "TRN_RateHistory": "RateHistory",
    "TRN_Trips": "Trip", "TRN_Invoices": "Invoice", "TRN_InvoiceLines": "InvoiceLine",
    "TRN_CreditNotes": "CreditNote", "TRN_CreditNoteLines": "CreditNoteLine",
    "TRN_EInvoiceSubmissions": "EInvoiceSubmission", "TRN_Receipts": "Receipt",
    "TRN_Advances": "Advance", "TRN_Settlements": "Settlement",
    "TRN_SettlementDeductions": "SettlementDeduction", "TRN_PartnerPayments": "PartnerPayment",
    "TRN_InvoiceLayouts": "InvoiceLayout",
    "Accounts": "Account", "PostingKeyMappings": "PostingKeyMapping", "CostCentres": "CostCentre",
    "CashBoxes": "CashBox", "BankAccounts": "BankAccount", "Periods": "Period",
    "JournalEntries": "JournalEntry", "EntryLines": "EntryLine", "PendingPostings": "PendingPosting",
    "JournalExportBatches": "JournalExportBatch", "ApArParties": "ApArParty",
    "ApArDocuments": "ApArDocument", "ApArDocumentLines": "ApArDocumentLine",
    "ApArPayments": "ApArPayment", "ApArReturns": "ApArReturn",
    "ApArEInvoiceSubmissions": "ApArEInvoiceSubmission",
    "ImportBatches": "ImportBatch", "CompanySettings": "CompanySetting",
    "NumberSequences": "NumberSequence", "EInvoiceSettings": "EInvoiceSettings",
    "CustomFieldDefinitions": "CustomFieldDefinition", "StandardFieldConfigs": "StandardFieldConfig",
    "TableColumnViews": "TableColumnView", "SavedReports": "SavedReport",
    "DashboardPins": "DashboardPin", "RecordLocks": "RecordLock",
}

# Aggregate children: (child table, fk column) -> collection navigation name on the parent
COLLECTIONS = {
    ("TRN_InvoiceLines", "InvoiceId"): "Lines",
    ("TRN_CreditNoteLines", "CreditNoteId"): "Lines",
    ("TRN_SettlementDeductions", "SettlementId"): "Deductions",
    ("EntryLines", "JournalEntryId"): "Lines",
    ("ApArDocumentLines", "DocumentId"): "Lines",
    ("RolePermissions", "RoleId"): "RolePermissions",
    ("Accounts", "ParentId"): "Children",
}

STD_BASE = ["Id", "PublicId", "CreatedAt", "CreatedBy", "UpdatedAt", "UpdatedBy",
            "IsDeleted", "DeletedAt", "DeletedBy", "DeleteReason", "RowVersion"]

SPECIFICITY_SQL = (
    "ISNULL(CONVERT(tinyint, "
    "CASE WHEN [CustomerId] IS NOT NULL OR [PartnerId] IS NOT NULL THEN 8 ELSE 0 END + "
    "CASE WHEN [MaterialId] IS NOT NULL THEN 4 ELSE 0 END + "
    "CASE WHEN [LoadingSiteId] IS NOT NULL THEN 2 ELSE 0 END + "
    "CASE WHEN [UnloadingSiteId] IS NOT NULL THEN 1 ELSE 0 END), 0)"
)


def clr_type(sqltype: str, nullable: bool) -> str:
    t = sqltype.upper()
    if t.startswith("BIGINT"):
        b = "long"
    elif t.startswith("INT"):
        b = "int"
    elif t.startswith("SMALLINT"):
        b = "short"
    elif t.startswith("TINYINT"):
        b = "byte"
    elif t.startswith("BIT"):
        b = "bool"
    elif t.startswith("DECIMAL"):
        b = "decimal"
    elif t.startswith("UNIQUEIDENTIFIER"):
        b = "Guid"
    elif t == "DATE":
        b = "DateOnly"
    elif t.startswith("DATETIME2"):
        b = "DateTime"
    elif t.startswith("VARBINARY") or t.startswith("ROWVERSION"):
        return "byte[]?" if nullable else "byte[]"
    else:
        return "string?" if nullable else "string"
    return b + "?" if nullable else b


def sql_column_type(sqltype: str) -> str:
    t = sqltype.strip()
    if t.upper().startswith("BIGINT IDENTITY"):
        return "bigint"
    if t.upper() == "DATETIME2":
        return "datetime2"
    return t.lower().replace("max", "max")


def default_initializer(col: dict, clr: str) -> str | None:
    d = col["default"]
    if not d or d.startswith("("):
        if not col["nullable"] and clr == "string":
            return "string.Empty"
        if clr == "byte[]":
            return "[]"
        return None
    if d == "NEWID()":
        return "Guid.NewGuid()"
    if d == "SYSUTCDATETIME()":
        return "DateTime.UtcNow"
    if d.startswith("N'") or d.startswith("'"):
        return '"' + d.split("'")[1] + '"'
    if clr.startswith("bool"):
        return "true" if d == "1" else None  # false is the CLR default (CA1805)
    if clr.startswith("decimal"):
        return None if float(d) == 0 else d + "m"
    return None if d in ("0",) else d


def nav_name(fk_col: str) -> str:
    if fk_col.endswith("Id"):
        return fk_col[:-2]
    return fk_col + "User"


def bracket_filter(f: str | None, cols: set[str]) -> str | None:
    if not f:
        return None
    parts = re.split(r"('[^']*')", f)
    out = []
    for p in parts:
        if p.startswith("'"):
            out.append(p)
        else:
            out.append(re.sub(r"\b([A-Za-z_]\w*)\b",
                              lambda m: f"[{m.group(1)}]" if m.group(1) in cols else m.group(1), p))
    return "".join(out)


def cs_str(s: str) -> str:
    return '"' + s.replace("\\", "\\\\").replace('"', '\\"') + '"'


def xml_escape(s: str) -> str:
    return s.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")


def gen():
    model = load()
    tables = model["tables"]
    by_name = {t["name"]: t for t in tables}
    # incoming collections
    incoming: dict[str, list[tuple[str, str, str]]] = {}
    for t in tables:
        for fk in t["fks"]:
            key = (t["name"], fk["column"])
            if key in COLLECTIONS:
                incoming.setdefault(fk["ref_table"], []).append(
                    (COLLECTIONS[key], CLASS_NAME[t["name"]], fk["column"]))

    for t in tables:
        cls = CLASS_NAME[t["name"]]
        folder = SCHEMA_FOLDER[t["schema"]]
        cols = t["columns"]
        colnames = {c["name"] for c in cols}
        has_std = "IsDeleted" in colnames and "RowVersion" in colnames
        std_company = any(c["name"] == "CompanyId" and c["std"] for c in cols)
        tenant_col = next((c for c in cols if c["name"] == "TenantId"), None)
        company_col = next((c for c in cols if c["name"] == "CompanyId"), None)

        interfaces = []
        if tenant_col is not None:
            interfaces.append("INullableTenantScoped" if tenant_col["nullable"] else "ITenantScoped")
        if company_col is not None and (std_company or t["name"] == "MaintenanceLogs"):
            interfaces.append("ICompanyScoped")
        if not has_std:
            interfaces.append("IAppendOnly")
        base = "AuditableEntity" if has_std else None
        inherits = ", ".join(([base] if base else []) + interfaces)

        # ---------------- entity ----------------
        lines = [
            "// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.",
            "// Hand-maintained from here on.",
            f"namespace {NS}.Domain.{folder};",
            "",
            "/// <summary>",
            f"/// {t['schema']}.{t['name']} â€” {xml_escape(t['scope'] or '')}.",
            "/// </summary>",
            f"public class {cls}" + (f" : {inherits}" if inherits else ""),
            "{",
        ]
        fk_cols = {fk["column"]: fk for fk in t["fks"]}
        for c in cols:
            if has_std and c["name"] in STD_BASE:
                continue
            clr = clr_type(c["type"], c["nullable"])
            if t["name"] == "TRN_Tariffs" and c["name"] == "Specificity":
                lines.append(f"    /// <summary>{xml_escape(c['description'])} Persisted computed column.</summary>")
                lines.append(f"    public {clr} {c['name']} {{ get; private set; }}")
                continue
            init = default_initializer(c, clr)
            if c["name"] == "Id" and not has_std:
                init = None
            lines.append(f"    /// <summary>{xml_escape(c['description'])}</summary>")
            lines.append(f"    public {clr} {c['name']} {{ get; set; }}" + (f" = {init};" if init else ""))
        # navigations
        for fk in t["fks"]:
            ref_cls = CLASS_NAME[fk["ref_table"]]
            nav = nav_name(fk["column"])
            nullable = "?" if not fk["required"] else ""
            bang = "" if nullable else " = null!;"
            lines.append(f"    public {ref_cls}{nullable} {nav} {{ get; set; }}{bang}")
        for coll, child_cls, _ in incoming.get(t["name"], []):
            lines.append(f"    public ICollection<{child_cls}> {coll} {{ get; set; }} = new List<{child_cls}>();")
        lines.append("}")
        out = DOMAIN_DIR / folder / f"{cls}.cs"
        out.parent.mkdir(parents=True, exist_ok=True)
        out.write_text("\n".join(lines) + "\n", encoding="utf-8")

        # ---------------- configuration ----------------
        cl = [
            "// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.",
            "// Hand-maintained from here on. Names, types, defaults and constraints must match the design.",
            "using Microsoft.EntityFrameworkCore;",
            "using Microsoft.EntityFrameworkCore.Metadata.Builders;",
            f"using {NS}.Domain.Core;",
            f"using {NS}.Domain.{folder};" if folder != "Core" else "",
            "",
            f"namespace {NS}.Infrastructure.Persistence.Configurations.{folder};",
            "",
            f"internal sealed class {cls}Configuration : IEntityTypeConfiguration<{cls}>",
            "{",
            f"    public void Configure(EntityTypeBuilder<{cls}> builder)",
            "    {",
        ]
        checks = derive_checks(t)
        if checks:
            cl.append(f"        builder.ToTable({cs_str(t['name'])}, {cs_str(t['schema'])}, t =>")
            cl.append("        {")
            for n, sql in checks:
                cl.append(f"            t.HasCheckConstraint({cs_str(n)}, {cs_str(sql)});")
            cl.append("        });")
        else:
            cl.append(f"        builder.ToTable({cs_str(t['name'])}, {cs_str(t['schema'])});")
        cl.append("")
        cl.append(f"        builder.HasKey(e => e.Id).HasName({cs_str(t['pk']['name'])});")
        cl.append("")
        for order, c in enumerate(cols):
            n = c["name"]
            sqlt = sql_column_type(c["type"])
            parts = [f"builder.Property(e => e.{n})", f".HasColumnName({cs_str(n)})",
                     f".HasColumnType({cs_str(sqlt)})", f".HasColumnOrder({order})"]
            up = c["type"].upper()
            if up.startswith("NVARCHAR") or up.startswith("VARCHAR") or up.startswith("CHAR"):
                parts.append(".IsUnicode(" + ("true" if up.startswith("NVARCHAR") else "false") + ")")
                if "(MAX)" not in up:
                    size = re.search(r"\((\d+)\)", up).group(1)
                    parts.append(f".HasMaxLength({size})")
                if up.startswith("CHAR"):
                    parts.append(".IsFixedLength()")
            if up.startswith("VARBINARY") and "(MAX)" not in up:
                parts.append(f".HasMaxLength({re.search(r'\((\d+)\)', up).group(1)})")
            if up.startswith("DECIMAL"):
                p, s = re.search(r"\((\d+),(\d+)\)", up).groups()
                parts.append(f".HasPrecision({p}, {s})")
            parts.append(".IsRequired(" + ("false" if c["nullable"] else "true") + ")")
            if n == "Id":
                parts.append(".UseIdentityColumn()" if up.startswith("BIGINT IDENTITY") else "")
                if not up.startswith("BIGINT IDENTITY"):
                    pass
            elif up.startswith("ROWVERSION"):
                parts.append(".IsRowVersion()")
            elif t["name"] == "TRN_Tariffs" and n == "Specificity":
                parts.append(f".HasComputedColumnSql({cs_str(SPECIFICITY_SQL)}, stored: true)")
            elif c["default"] and not c["default"].startswith("("):
                parts.append(f".HasDefaultValueSql({cs_str(c['default'])})")
                parts.append(".ValueGeneratedNever()")
            cl.append("        " + "\n            ".join(p for p in parts if p) + ";")
        cl.append("")
        # keys / indexes
        for ix in t["uniques"] + t["indexes"]:
            names = ", ".join(cs_str(c["name"]) for c in ix["columns"])
            s = f"        builder.HasIndex(new[] {{ {names} }}, {cs_str(ix['name'])})"
            if ix["unique"]:
                s += "\n            .IsUnique()"
            if any(c["desc"] for c in ix["columns"]):
                s += "\n            .IsDescending(" + ", ".join("true" if c["desc"] else "false" for c in ix["columns"]) + ")"
            filt = bracket_filter(ix["filter"], colnames)
            if filt:
                s += f"\n            .HasFilter({cs_str(filt)})"
            elif ix["unique"]:
                s += "\n            .HasFilter(null)"
            if ix["include"]:
                s += "\n            .IncludeProperties(" + ", ".join(cs_str(x) for x in ix["include"]) + ")"
            cl.append(s + ";")
        cl.append("")
        # business foreign keys
        for fk in t["fks"]:
            ref_cls = CLASS_NAME[fk["ref_table"]]
            nav = nav_name(fk["column"])
            coll = COLLECTIONS.get((t["name"], fk["column"]))
            with_many = f".WithMany(p => p.{coll})" if coll else ".WithMany()"
            cl.append(f"        builder.HasOne(e => e.{nav})")
            cl.append(f"            {with_many}")
            cl.append(f"            .HasForeignKey(e => e.{fk['column']})")
            cl.append(f"            .HasPrincipalKey(p => p.Id)")
            cl.append(f"            .IsRequired({'true' if fk['required'] else 'false'})")
            cl.append(f"            .HasConstraintName({cs_str(fk['name'])})")
            cl.append(f"            .OnDelete(DeleteBehavior.NoAction);")
        # standard foreign keys (no navigation)
        for sfk in t["std_fks"]:
            ref_cls = CLASS_NAME[sfk["ref_table"]]
            ref_tbl = sfk["ref_table"]
            name = f"FK_{t['name']}_{ref_tbl}_{sfk['column']}"
            cl.append(f"        builder.HasOne<{ref_cls}>()")
            cl.append(f"            .WithMany()")
            cl.append(f"            .HasForeignKey(e => e.{sfk['column']})")
            cl.append(f"            .HasConstraintName({cs_str(name)})")
            cl.append(f"            .OnDelete(DeleteBehavior.NoAction);")
        cl.append("    }")
        cl.append("}")
        out = CONF_DIR / folder / f"{cls}Configuration.cs"
        out.parent.mkdir(parents=True, exist_ok=True)
        out.write_text("\n".join(x for x in cl if x is not None) + "\n", encoding="utf-8")
    print(f"generated {len(tables)} entities + configurations")


if __name__ == "__main__":
    gen()
