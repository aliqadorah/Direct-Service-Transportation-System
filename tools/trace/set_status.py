"""Update the status column (last cell) of rows in docs/step1/02_Traceability_Matrix.md by requirement ID.

Usage: python tools/trace/set_status.py <status> <ID> [<ID> ...]
  status: todo | partial | done   (☐ / ◐ / ☑)
"""
import re
import sys
from pathlib import Path

sys.stdout.reconfigure(encoding="utf-8")
MATRIX = Path(__file__).resolve().parents[2] / "docs" / "step1" / "02_Traceability_Matrix.md"
SYMBOL = {"todo": "☐", "partial": "◐", "done": "☑"}

status = SYMBOL[sys.argv[1]]
ids = set(sys.argv[2:])
lines = MATRIX.read_text(encoding="utf-8").splitlines()
changed = set()
for i, line in enumerate(lines):
    m = re.match(r"^\| ([A-Z]+-[0-9a-z]+) \|", line)
    if m and m.group(1) in ids:
        lines[i] = re.sub(r"\| [☐◐☑] \|\s*$", f"| {status} |", line)
        changed.add(m.group(1))
MATRIX.write_text("\n".join(lines) + "\n", encoding="utf-8")
missing = ids - changed
print(f"updated {len(changed)} rows to {status}" + (f"; not found: {sorted(missing)}" if missing else ""))
