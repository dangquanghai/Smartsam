/*
    Convert dbo.INV_ItemDel.ItemCode from UNIQUE CONSTRAINT to a normal index.
    This keeps lookup performance on ItemCode while allowing multiple delete-log
    rows for the same ItemCode across different times.
*/

SET NOCOUNT ON;

IF EXISTS
(
    SELECT 1
    FROM sys.key_constraints kc
    INNER JOIN sys.tables t ON t.object_id = kc.parent_object_id
    INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
    WHERE s.name = 'dbo'
      AND t.name = 'INV_ItemDel'
      AND kc.name = 'IX_INV_ItemDel'
      AND kc.type = 'UQ'
)
BEGIN
    ALTER TABLE dbo.INV_ItemDel DROP CONSTRAINT IX_INV_ItemDel;
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes i
    INNER JOIN sys.tables t ON t.object_id = i.object_id
    INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
    WHERE s.name = 'dbo'
      AND t.name = 'INV_ItemDel'
      AND i.name = 'IX_INV_ItemDel'
)
BEGIN
    CREATE INDEX IX_INV_ItemDel
        ON dbo.INV_ItemDel(ItemCode);
END;

SELECT
    i.name AS IndexName,
    i.is_unique,
    c.name AS ColumnName,
    ic.key_ordinal
FROM sys.indexes i
INNER JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
INNER JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
INNER JOIN sys.tables t ON t.object_id = i.object_id
INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
WHERE s.name = 'dbo'
  AND t.name = 'INV_ItemDel'
  AND i.name = 'IX_INV_ItemDel'
ORDER BY ic.key_ordinal;
