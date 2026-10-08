"""Design-vs-database verification (build prompt, step 3).

Compares docs/inputs/Database_Design.md (rev. 4 + approved rev. 5) with the physical database,
table by table: tables, columns (type, size, precision, nullability, default, order, collation),
primary keys, foreign keys (+ ON DELETE/UPDATE), unique and non-unique indexes (columns, order,
DESC, INCLUDE, filter), check constraints and computed columns. Writes a markdown report.

Usage: python tools/design/verify_database.py [--server localhost] [--database DirectServiceMaster] [--out path]
Exit code 0 = no differences, 1 = differences found.
"""
from __future__ import annotations

import argparse
import json
import re
import subprocess
import sys
import tempfile
from datetime import datetime, timezone
from pathlib import Path

sys.stdout.reconfigure(encoding="utf-8")
sys.path.insert(0, str(Path(__file__).parent))
from parse_design import load, ROOT  # noqa: E402
from checks import derive_checks  # noqa: E402
from gen_efcore import SPECIFICITY_SQL  # noqa: E402

EXPECTED_COLLATION = "Arabic_100_CI_AI_SC_UTF8"


def read_catalog(server: str, database: str) -> dict:
    sql = (Path(__file__).parent / "catalog.sql").read_text(encoding="utf-8")
    with tempfile.TemporaryDirectory() as d:
        out = Path(d) / "catalog.json"
        subprocess.run(["sqlcmd", "-S", server, "-E", "-C", "-d", database, "-y", "0",
                        "-f", "65001", "-Q", sql, "-o", str(out)], check=True)
        text = out.read_text(encoding="utf-8-sig")
        text = text[text.index("{"):text.rindex("}") + 1]
    return json.loads(text)


def norm_sql(s: str | None) -> str:
    """Normalise an SQL expression for comparison (SQL Server stores rewritten forms)."""
    if s is None:
        return ""
    s = s.replace("[", "").replace("]", "")
    s = s.replace("(", "").replace(")", "")
    s = re.sub(r"\s+", "", s).lower()
    return s


def canonical(expr: str | None) -> str:
    """Normalised form in which IN lists and OR-chains of equalities on one column compare equal
    regardless of order (SQL Server rewrites IN as reversed OR chains in checks, keeps IN in filters)."""
    s = norm_sql(expr)
    s = re.sub(r"(\w+)in('[^']*'(?:,'[^']*')*)",
               lambda m: "or".join(f"{m.group(1)}={v}" for v in re.findall(r"'[^']*'", m.group(2))), s)
    s = re.sub(r"(\w+)='[^']*'(?:or\1='[^']*')+",
               lambda m: "or".join(sorted(m.group(0).split("or"))), s)
    return s


def expand(expr: str) -> str:
    """Rewrite IN lists and BETWEEN the way SQL Server stores check/filter definitions."""
    def in_repl(m):
        col, items = m.group(1), [x.strip() for x in m.group(2).split(",")]
        return "(" + " OR ".join(f"{col}={i}" for i in items) + ")"
    expr = re.sub(r"(\[?\w+\]?) IN \(([^)]*)\)", in_repl, expr)
    expr = re.sub(r"(\[?\w+\]?) BETWEEN (\S+) AND (\S+)", r"(\1>=\2 AND \1<=\3)", expr)
    return expr


def type_string(c: dict) -> str:
    t = c["type"].upper()
    if t in ("VARCHAR", "NVARCHAR", "CHAR", "NCHAR", "VARBINARY"):
        if c["max_length"] == -1:
            return f"{t}(MAX)"
        n = c["max_length"] // (2 if t.startswith("N") else 1)
        return f"{t}({n})"
    if t == "DECIMAL":
        return f"DECIMAL({c['precision']},{c['scale']})"
    if t == "DATETIME2":
        return "DATETIME2" if c["scale"] == 7 else f"DATETIME2({c['scale']})"
    if t == "TIMESTAMP":
        return "ROWVERSION"
    if t == "BIGINT" and c["is_identity"]:
        return "BIGINT IDENTITY(1,1)"
    return t


def norm_default(d: str | None) -> str:
    if not d or d.startswith("(automatic"):
        return ""
    return norm_sql(d)


def verify(server: str, database: str) -> tuple[list[str], dict]:
    model = load()
    cat = read_catalog(server, database)
    diffs: list[str] = []
    stats = {k: 0 for k in ("tables", "columns", "pks", "fks", "uniques", "indexes", "checks", "defaults")}

    if cat["database_collation"] != EXPECTED_COLLATION:
        diffs.append(f"Database collation is {cat['database_collation']}, expected {EXPECTED_COLLATION}.")

    db_tables = {(t["schema"], t["table"]) for t in cat["tables"]}
    design_tables = {(t["schema"], t["name"]) for t in model["tables"]}
    for missing in sorted(design_tables - db_tables):
        diffs.append(f"Table {missing[0]}.{missing[1]} is in the design but not in the database.")
    for extra in sorted(db_tables - design_tables):
        diffs.append(f"Table {extra[0]}.{extra[1]} is in the database but not in the design.")

    cols_by = {}
    for c in cat["columns"]:
        cols_by.setdefault((c["schema"], c["table"]), {})[c["column"]] = c
    idx_by = {}
    for i in cat["indexes"]:
        idx_by.setdefault((i["schema"], i["table"]), {})[i["name"]] = i
    fk_by = {}
    for f in cat["fks"]:
        fk_by.setdefault((f["schema"], f["table"]), {})[f["name"]] = f
    ck_by = {}
    for ck in cat["checks"]:
        ck_by.setdefault((ck["schema"], ck["table"]), {})[ck["name"]] = ck

    for t in model["tables"]:
        key = (t["schema"], t["name"])
        if key not in db_tables:
            continue
        stats["tables"] += 1
        fq = f"{t['schema']}.{t['name']}"
        db_cols = cols_by.get(key, {})
        design_cols = [c["name"] for c in t["columns"]]
        for extra in sorted(set(db_cols) - set(design_cols)):
            diffs.append(f"{fq}: column {extra} exists in the database but not in the design.")
        for ordinal, c in enumerate(t["columns"], start=1):
            dc = db_cols.get(c["name"])
            if dc is None:
                diffs.append(f"{fq}: column {c['name']} is missing.")
                continue
            stats["columns"] += 1
            exp_type = c["type"].upper().replace(" ", "")
            act_type = type_string(dc).replace(" ", "")
            if exp_type != act_type:
                diffs.append(f"{fq}.{c['name']}: type {act_type}, design {c['type']}.")
            if bool(dc["is_nullable"]) != c["nullable"]:
                diffs.append(f"{fq}.{c['name']}: nullable={bool(dc['is_nullable'])}, design {'NULL' if c['nullable'] else 'NOT NULL'}.")
            if dc["ordinal"] != ordinal:
                diffs.append(f"{fq}.{c['name']}: column position {dc['ordinal']}, design {ordinal}.")
            exp_def = norm_default(c["default"])
            act_def = norm_default(dc["default_definition"])
            if exp_def != act_def:
                diffs.append(f"{fq}.{c['name']}: default {dc['default_definition']!r}, design {c['default']!r}.")
            if exp_def:
                stats["defaults"] += 1
                exp_name = f"DF_{t['name']}_{c['name']}"
                if dc["default_name"] != exp_name:
                    diffs.append(f"{fq}.{c['name']}: default constraint named {dc['default_name']}, expected {exp_name}.")
            if dc["collation"] not in (None, EXPECTED_COLLATION):
                diffs.append(f"{fq}.{c['name']}: collation {dc['collation']}.")
            if t["name"] == "TRN_Tariffs" and c["name"] == "Specificity":
                if not dc["is_computed"] or not dc["is_persisted"] or norm_sql(dc["computed"]) != norm_sql(SPECIFICITY_SQL):
                    diffs.append(f"{fq}.Specificity: expected persisted computed column {SPECIFICITY_SQL}.")
            elif dc["is_computed"]:
                diffs.append(f"{fq}.{c['name']}: is computed in the database but not in the design.")

        # primary key
        db_idx = idx_by.get(key, {})
        pk = [i for i in db_idx.values() if i["is_primary_key"]]
        if len(pk) != 1 or pk[0]["name"] != t["pk"]["name"] or [x["column"] for x in (pk[0]["cols"] or [])] != [t["pk"]["column"]]:
            diffs.append(f"{fq}: primary key differs (database {[p['name'] for p in pk]}, design {t['pk']['name']} ({t['pk']['column']})).")
        else:
            stats["pks"] += 1

        # indexes
        expected_idx = {i["name"]: i for i in t["uniques"] + t["indexes"]}
        for name, i in db_idx.items():
            if i["is_primary_key"]:
                continue
            if name not in expected_idx:
                diffs.append(f"{fq}: index {name} exists in the database but not in the design.")
        for name, e in expected_idx.items():
            a = db_idx.get(name)
            if a is None:
                diffs.append(f"{fq}: index {name} is missing.")
                continue
            stats["uniques" if e["unique"] else "indexes"] += 1
            acols = a["cols"] or []
            keys = [(x["column"], bool(x["desc"])) for x in acols if not x["included"]]
            incl = sorted(x["column"] for x in acols if x["included"])
            if keys != [(x["name"], x["desc"]) for x in e["columns"]]:
                diffs.append(f"{fq}: index {name} key columns {keys}, design {[(x['name'], x['desc']) for x in e['columns']]}.")
            if incl != sorted(e["include"] or []):
                diffs.append(f"{fq}: index {name} INCLUDE {incl}, design {e['include']}.")
            if bool(a["is_unique"]) != e["unique"]:
                diffs.append(f"{fq}: index {name} unique={bool(a['is_unique'])}, design unique={e['unique']}.")
            if canonical(a["filter_definition"]) != canonical(expand(e["filter"] or "")):
                diffs.append(f"{fq}: index {name} filter {a['filter_definition']!r}, design {e['filter']!r}.")

        # foreign keys (business + standard)
        expected_fk = {f["name"]: (f["column"], f["ref_schema"], f["ref_table"]) for f in t["fks"]}
        for s in t["std_fks"]:
            expected_fk[f"FK_{t['name']}_{s['ref_table']}_{s['column']}"] = (s["column"], s["ref_schema"], s["ref_table"])
        db_fk = fk_by.get(key, {})
        for name in sorted(set(db_fk) - set(expected_fk)):
            diffs.append(f"{fq}: foreign key {name} exists in the database but not in the design.")
        for name, (col, rs, rt) in expected_fk.items():
            a = db_fk.get(name)
            if a is None:
                diffs.append(f"{fq}: foreign key {name} is missing.")
                continue
            stats["fks"] += 1
            if (a["column"], a["ref_schema"], a["ref_table"], a["ref_column"]) != (col, rs, rt, "Id"):
                diffs.append(f"{fq}: foreign key {name} is {a['column']} → {a['ref_schema']}.{a['ref_table']}({a['ref_column']}), design {col} → {rs}.{rt}(Id).")
            if a["on_delete"] != "NO_ACTION" or a["on_update"] != "NO_ACTION":
                diffs.append(f"{fq}: foreign key {name} ON DELETE {a['on_delete']} / ON UPDATE {a['on_update']}, design NO ACTION.")
            if a["is_disabled"] or a["is_not_trusted"]:
                diffs.append(f"{fq}: foreign key {name} is disabled or not trusted.")

        # check constraints
        expected_ck = dict(derive_checks(t))
        db_ck = ck_by.get(key, {})
        for name in sorted(set(db_ck) - set(expected_ck)):
            diffs.append(f"{fq}: check constraint {name} exists in the database but not in the design.")
        for name, sql in expected_ck.items():
            a = db_ck.get(name)
            if a is None:
                diffs.append(f"{fq}: check constraint {name} is missing.")
                continue
            stats["checks"] += 1
            if canonical(a["definition"]) != canonical(expand(sql)):
                diffs.append(f"{fq}: check {name} is {a['definition']}, expected {sql}.")
            if a["is_disabled"] or a["is_not_trusted"]:
                diffs.append(f"{fq}: check {name} is disabled or not trusted.")

    business_fks = sum(len(t["fks"]) for t in model["tables"])
    stats["business_fks_in_design"] = business_fks
    return diffs, stats


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--server", default="localhost")
    ap.add_argument("--database", default="DirectServiceMaster")
    ap.add_argument("--out", default=str(ROOT / "docs" / "step3" / "02_Design_vs_Database_Verification.md"))
    args = ap.parse_args()
    diffs, stats = verify(args.server, args.database)
    now = datetime.now(timezone.utc).strftime("%Y-%m-%d %H:%M UTC")
    lines = [
        "# Design-vs-Database Verification Report",
        "",
        f"_Generated {now} by `tools/design/verify_database.py` against `{args.server}/{args.database}`._",
        "_Source of truth: `docs/inputs/Database_Design.md` (revision 4) + owner-approved revision 5 "
        "(`docs/step2/02_Owner_Decisions_Step2.md`)._",
        "",
        "## Result",
        "",
        f"**{'NO DIFFERENCES' if not diffs else str(len(diffs)) + ' DIFFERENCE(S)'}** between the design and the database.",
        "",
        "| Object | Verified |",
        "|---|---|",
        f"| Tables | {stats['tables']} |",
        f"| Columns (type, size/precision, nullability, default, position, collation) | {stats['columns']} |",
        f"| Named default constraints (DF_) | {stats['defaults']} |",
        f"| Primary keys | {stats['pks']} |",
        f"| Foreign keys (all NO ACTION; {stats['business_fks_in_design']} business + standard keys) | {stats['fks']} |",
        f"| Unique indexes (filtered as designed) | {stats['uniques']} |",
        f"| Non-unique indexes | {stats['indexes']} |",
        f"| Check constraints | {stats['checks']} |",
        "",
        "Database collation, the TRN_Tariffs.Specificity persisted computed column and every "
        "object name are checked as well. Objects present in the database but absent from the "
        "design are reported as differences.",
        "",
    ]
    if diffs:
        lines += ["## Differences", ""] + [f"- {d}" for d in diffs]
    out = Path(args.out)
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text("\n".join(lines) + "\n", encoding="utf-8")
    print("\n".join(lines[6:20]))
    for d in diffs[:80]:
        print(" -", d)
    return 1 if diffs else 0


if __name__ == "__main__":
    sys.exit(main())
