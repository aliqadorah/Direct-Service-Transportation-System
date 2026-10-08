/* Direct Service Transportation — application database user (deployment; ADR-08 second line of defence).
   Raw SQL by necessity (permissions are not part of the EF Core model). Listed in BUILD_STATUS.md.

   The web application connects as this user. It can read and write business data but can
   NEVER hard-delete a row in any schema (BR-041 soft delete only) and can never change or delete
   audit/maintenance log rows (FR-189, NFR-10). Run in the DirectServiceMaster database after the
   migration script. Map @LoginName to the hosting identity (SQL login, Windows/managed identity)
   chosen at deployment (DEF-01). */
DECLARE @UserName sysname = N'dst_app';

IF DATABASE_PRINCIPAL_ID(@UserName) IS NULL
BEGIN
    -- Development / verification: a user without login. At deployment replace with
    -- CREATE USER [dst_app] FOR LOGIN [<login>]  or  CREATE USER [<identity>] FROM EXTERNAL PROVIDER.
    EXEC (N'CREATE USER [dst_app] WITHOUT LOGIN;');
END;

EXEC (N'ALTER ROLE db_datareader ADD MEMBER [dst_app];');
EXEC (N'ALTER ROLE db_datawriter ADD MEMBER [dst_app];');

-- Soft delete only: no DELETE on any business schema.
EXEC (N'DENY DELETE ON SCHEMA::core TO [dst_app];');
EXEC (N'DENY DELETE ON SCHEMA::acc  TO [dst_app];');
EXEC (N'DENY DELETE ON SCHEMA::cfg  TO [dst_app];');
EXEC (N'DENY DELETE ON SCHEMA::trn  TO [dst_app];');

-- Append-only logs.
EXEC (N'DENY UPDATE ON OBJECT::core.AuditLogs       TO [dst_app];');
EXEC (N'DENY UPDATE ON OBJECT::core.MaintenanceLogs TO [dst_app];');
EXEC (N'DENY UPDATE ON OBJECT::trn.TRN_RateHistory  TO [dst_app];');

-- Schema changes only through migrations run by a separate, privileged deployment identity.
EXEC (N'DENY ALTER ON SCHEMA::core TO [dst_app];');
EXEC (N'DENY ALTER ON SCHEMA::acc  TO [dst_app];');
EXEC (N'DENY ALTER ON SCHEMA::cfg  TO [dst_app];');
EXEC (N'DENY ALTER ON SCHEMA::trn  TO [dst_app];');
GO
