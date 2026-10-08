"""Print every check constraint the model will create (for review)."""
import sys
sys.stdout.reconfigure(encoding="utf-8")
from parse_design import load
from checks import derive_checks

m = load()
n = 0
for t in m["tables"]:
    for name, sql in derive_checks(t):
        n += 1
        if "--all" in sys.argv or "DeletedAt_DeletedBy" not in name:
            print(f"{t['schema']}.{t['name']:28} {name:60} {sql}")
print(f"total={n}")
