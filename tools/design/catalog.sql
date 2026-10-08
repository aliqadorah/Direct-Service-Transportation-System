-- Reads the physical schema of the Master Database as one JSON document (used by verify_database.py).
SET NOCOUNT ON;
SELECT (
SELECT
  (SELECT s.name AS [schema], t.name AS [table]
     FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id
    WHERE s.name IN ('core','acc','cfg','trn')
    FOR JSON PATH) AS tables,
  (SELECT s.name AS [schema], t.name AS [table], c.name AS [column], c.column_id AS ordinal,
          ty.name AS [type], c.max_length, c.precision, c.scale, c.is_nullable, c.is_identity,
          c.is_computed, cc.definition AS computed, cc.is_persisted, c.collation_name AS collation,
          dc.name AS default_name, dc.definition AS default_definition
     FROM sys.columns c
     JOIN sys.tables t ON t.object_id = c.object_id
     JOIN sys.schemas s ON s.schema_id = t.schema_id
     JOIN sys.types ty ON ty.user_type_id = c.user_type_id
     LEFT JOIN sys.computed_columns cc ON cc.object_id = c.object_id AND cc.column_id = c.column_id
     LEFT JOIN sys.default_constraints dc ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
    WHERE s.name IN ('core','acc','cfg','trn')
    FOR JSON PATH, INCLUDE_NULL_VALUES) AS columns,
  (SELECT s.name AS [schema], t.name AS [table], i.name, i.is_unique, i.is_primary_key,
          i.has_filter, i.filter_definition, i.type_desc,
          (SELECT c.name AS [column], ic.is_descending_key AS [desc], ic.is_included_column AS included, ic.key_ordinal
             FROM sys.index_columns ic JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id
            ORDER BY ic.is_included_column, ic.key_ordinal, c.name
            FOR JSON PATH) AS cols
     FROM sys.indexes i
     JOIN sys.tables t ON t.object_id = i.object_id
     JOIN sys.schemas s ON s.schema_id = t.schema_id
    WHERE s.name IN ('core','acc','cfg','trn') AND i.type > 0
    FOR JSON PATH, INCLUDE_NULL_VALUES) AS indexes,
  (SELECT s.name AS [schema], t.name AS [table], fk.name, pc.name AS [column],
          rs.name AS ref_schema, rt.name AS ref_table, rc.name AS ref_column,
          fk.delete_referential_action_desc AS on_delete, fk.update_referential_action_desc AS on_update,
          fk.is_disabled, fk.is_not_trusted
     FROM sys.foreign_keys fk
     JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
     JOIN sys.tables t ON t.object_id = fk.parent_object_id
     JOIN sys.schemas s ON s.schema_id = t.schema_id
     JOIN sys.columns pc ON pc.object_id = fkc.parent_object_id AND pc.column_id = fkc.parent_column_id
     JOIN sys.tables rt ON rt.object_id = fk.referenced_object_id
     JOIN sys.schemas rs ON rs.schema_id = rt.schema_id
     JOIN sys.columns rc ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id
    WHERE s.name IN ('core','acc','cfg','trn')
    FOR JSON PATH) AS fks,
  (SELECT s.name AS [schema], t.name AS [table], ck.name, ck.definition, ck.is_disabled, ck.is_not_trusted
     FROM sys.check_constraints ck
     JOIN sys.tables t ON t.object_id = ck.parent_object_id
     JOIN sys.schemas s ON s.schema_id = t.schema_id
    WHERE s.name IN ('core','acc','cfg','trn')
    FOR JSON PATH) AS checks,
  (SELECT s.name AS [schema], t.name AS [table], kc.name, kc.type AS kind
     FROM sys.key_constraints kc
     JOIN sys.tables t ON t.object_id = kc.parent_object_id
     JOIN sys.schemas s ON s.schema_id = t.schema_id
    WHERE s.name IN ('core','acc','cfg','trn')
    FOR JSON PATH) AS keys,
  CAST(DATABASEPROPERTYEX(DB_NAME(), 'Collation') AS nvarchar(128)) AS database_collation
FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
) AS catalog_json;
