"""Self-test of verify_database.canonical(): order-insensitive IN/OR comparison must still detect real differences."""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from verify_database import canonical, expand  # noqa: E402

cases = [
    ("[S] IN ('A','B')", "([S]='B' OR [S]='A')", True),
    ("[S] IN ('A','B')", "([S]='A' OR [S]='C')", False),
    ("[S] IN ('A','B','C')", "([S]='A' OR [S]='B')", False),
    ("Status IN ('Queued','Rejected')", "([Status] IN ('Queued', 'Rejected'))", True),
    (expand("[D] BETWEEN 3 AND 10"), "([D]>=(3) AND [D]<=(10))", True),
    (expand("[D] BETWEEN 3 AND 10"), "([D]>=(3) AND [D]<=(11))", False),
    ("[Amount] > 0", "([Amount]>=(0))", False),
]
failed = 0
for a, b, expected in cases:
    got = canonical(a) == canonical(b)
    ok = got == expected
    failed += not ok
    print(("PASS" if ok else "FAIL"), a, "|", b, "->", got)
sys.exit(1 if failed else 0)
