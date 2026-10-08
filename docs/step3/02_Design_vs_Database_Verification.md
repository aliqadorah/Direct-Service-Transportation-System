# Design-vs-Database Verification Report

_Generated 2026-10-08 10:59 UTC by `tools/design/verify_database.py` against `localhost/DirectServiceMaster`._
_Source of truth: `docs/inputs/Database_Design.md` (revision 4) + owner-approved revision 5 (`docs/step2/02_Owner_Decisions_Step2.md`)._

## Result

**NO DIFFERENCES** between the design and the database.

| Object | Verified |
|---|---|
| Tables | 66 |
| Columns (type, size/precision, nullability, default, position, collation) | 1410 |
| Named default constraints (DF_) | 302 |
| Primary keys | 66 |
| Foreign keys (all NO ACTION; 118 business + standard keys) | 418 |
| Unique indexes (filtered as designed) | 111 |
| Non-unique indexes | 141 |
| Check constraints | 246 |

Database collation, the TRN_Tariffs.Specificity persisted computed column and every object name are checked as well. Objects present in the database but absent from the design are reported as differences.

