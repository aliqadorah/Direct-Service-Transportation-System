"""Parse docs/inputs/Database_Design.md into a machine-readable model (JSON).

Used by:
  * gen_efcore.py      - one-off generation of entity + configuration source files
  * verify_database.py - design-vs-database verification report (step 3)

Revision 5 changes approved by the owner (docs/step2/02_Owner_Decisions_Step2.md)
are applied on top of revision 4 by apply_revision5().
"""
from __future__ import annotations

import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
DESIGN = ROOT / "docs" / "inputs" / "Database_Design.md"

HEAD_RE = re.compile(r"^#### (core|trn|acc|cfg)\.(\w+)\s*$")
SCOPE_RE = re.compile(r"\*\*Scope:\*\*\s*(.+)$")
PK_RE = re.compile(r"^\*\*Primary key:\*\*\s*`(\w+) \((\w+)\)`")
FK_RE = re.compile(r"^- `(FK_\w+)`: (\w+) → (\w+)\.(\w+)\(Id\) — (required|optional)")
STDFK_RE = re.compile(r"^Standard foreign keys:\s*(.+)$")
IDX_RE = re.compile(r"^- `((?:UX|IX)_\w+)` \(([^)]*)\)(.*)$")


def split_row(line: str) -> list[str]:
    cells = line.strip().strip("|").split("|")
    return [c.strip() for c in cells]


def parse() -> dict:
    lines = DESIGN.read_text(encoding="utf-8").splitlines()
    tables: list[dict] = []
    cur: dict | None = None
    section = None
    for raw in lines:
        line = raw.rstrip()
        m = HEAD_RE.match(line)
        if m:
            cur = {
                "schema": m.group(1), "name": m.group(2), "scope": None,
                "columns": [], "pk": None, "fks": [], "std_fks": [],
                "uniques": [], "indexes": [], "rules": [],
            }
            tables.append(cur)
            section = None
            continue
        if cur is None:
            continue
        if line.startswith("## ") or line.startswith("### "):
            cur = None
            continue
        if (m := SCOPE_RE.search(line)) and cur["scope"] is None:
            cur["scope"] = m.group(1).strip()
            continue
        if line.startswith("| ") and not line.startswith("| Column |") and not line.startswith("|---"):
            c = split_row(line)
            if len(c) >= 7:
                name = c[0].replace("*(std)*", "").strip()
                cur["columns"].append({
                    "name": name, "std": "*(std)*" in c[0], "type": c[1],
                    "nullable": c[2] == "NULL", "default": c[3] or None,
                    "key": c[4] or None, "description": c[5], "rules": c[6],
                })
            continue
        if m := PK_RE.match(line):
            cur["pk"] = {"name": m.group(1), "column": m.group(2)}
            continue
        if line.startswith("**Foreign keys**"):
            section = "fk"; continue
        if line.startswith("**Unique constraints:**"):
            section = "ux"; continue
        if line.startswith("**Indexes:**"):
            section = "ix"; continue
        if line.startswith("**Relationships:**"):
            section = "rel"; continue
        if line.startswith("**Business rules:**"):
            section = "br"; continue
        if m := STDFK_RE.match(line):
            for part in m.group(1).split(","):
                if "→" not in part:
                    continue
                col, ref = [p.strip() for p in part.split("→")]
                sch, tbl = ref.split(".")
                cur["std_fks"].append({"column": col, "ref_schema": sch, "ref_table": tbl})
            continue
        if section == "fk" and (m := FK_RE.match(line)):
            cur["fks"].append({
                "name": m.group(1), "column": m.group(2), "ref_schema": m.group(3),
                "ref_table": m.group(4), "required": m.group(5) == "required",
            })
            continue
        if section in ("ux", "ix") and (m := IDX_RE.match(line)):
            name, cols, rest = m.group(1), m.group(2), m.group(3)
            include = None
            filt = None
            if mi := re.search(r"INCLUDE \(([^)]*)\)", rest):
                include = [x.strip() for x in mi.group(1).split(",")]
            if mw := re.search(r"WHERE (.+?)(?: —|$)", rest):
                filt = mw.group(1).strip()
            columns = []
            for col in cols.split(","):
                col = col.strip()
                desc = col.endswith(" DESC")
                columns.append({"name": col.replace(" DESC", ""), "desc": desc})
            unique = name.startswith("UX_") or "UNIQUE" in rest
            entry = {"name": name, "columns": columns, "include": include,
                     "filter": filt, "unique": unique}
            (cur["uniques"] if unique else cur["indexes"]).append(entry)
            continue
        if section == "br" and line.startswith("- "):
            cur["rules"].append(line[2:])
    return {"source": str(DESIGN.relative_to(ROOT)), "revision": 4, "tables": tables}


def apply_revision5(model: dict) -> dict:
    """Owner-approved revision 5 (D-02, D-08, D-09; step 2 decisions Q-02...Q-05)."""
    t = {f"{x['schema']}.{x['name']}": x for x in model["tables"]}

    inv = t["trn.TRN_Invoices"]
    for c in inv["columns"]:
        if c["name"] == "LayoutType":
            c["default"] = "'Claim'"

    ten = t["core.Tenants"]
    idx = next(i for i, c in enumerate(ten["columns"]) if c["name"] == "RetentionEndsAt") + 1
    ten["columns"][idx:idx] = [
        {"name": "PasswordMinLength", "std": False, "type": "TINYINT", "nullable": False,
         "default": "8", "key": None,
         "description": "Minimum password length for the tenant's users (FR-175).",
         "rules": "CHECK BETWEEN 8 AND 64."},
        {"name": "PasswordRequireLetter", "std": False, "type": "BIT", "nullable": False,
         "default": "1", "key": None,
         "description": "Password must contain at least one letter (FR-175).", "rules": ""},
        {"name": "PasswordRequireDigit", "std": False, "type": "BIT", "nullable": False,
         "default": "1", "key": None,
         "description": "Password must contain at least one digit (FR-175).", "rules": ""},
        {"name": "SessionIdleTimeoutMinutes", "std": False, "type": "SMALLINT", "nullable": False,
         "default": "30", "key": None,
         "description": "Inactive sessions expire after this many minutes (FR-176).",
         "rules": "CHECK BETWEEN 5 AND 1440."},
    ]

    co = t["core.Companies"]
    idx = next(i for i, c in enumerate(co["columns"]) if c["name"] == "MigrationCompletedAt") + 1
    co["columns"].insert(idx, {
        "name": "Status", "std": False, "type": "VARCHAR(10)", "nullable": False,
        "default": "'Active'", "key": None,
        "description": "Record status. An archived company is hidden from the company switcher and read-only; data kept.",
        "rules": "CHECK IN ('Active','Archived')."})
    model["revision"] = 5
    return model


def load() -> dict:
    return apply_revision5(parse())


if __name__ == "__main__":
    m = load()
    out = ROOT / "tools" / "design" / "design_model.json"
    out.write_text(json.dumps(m, ensure_ascii=False, indent=1), encoding="utf-8")
    tabs = m["tables"]
    print(f"tables={len(tabs)} business_fks={sum(len(x['fks']) for x in tabs)} "
          f"std_fks={sum(len(x['std_fks']) for x in tabs)} "
          f"uniques={sum(len(x['uniques']) for x in tabs)} indexes={sum(len(x['indexes']) for x in tabs)} "
          f"columns={sum(len(x['columns']) for x in tabs)}")
    by = {}
    for x in tabs:
        by[x["schema"]] = by.get(x["schema"], 0) + 1
    print(by)
    for x in tabs:
        if not x["pk"] or not x["columns"]:
            print("WARN incomplete", x["schema"], x["name"], file=sys.stderr)
