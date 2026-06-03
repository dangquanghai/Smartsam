SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRAN;

IF COL_LENGTH('dbo.SYS_Module', 'TheIcon') IS NULL
BEGIN
    EXEC('ALTER TABLE dbo.SYS_Module ADD TheIcon varchar(50) NULL;');
END;

IF COL_LENGTH('dbo.SYS_Function', 'TheIcon') IS NULL
BEGIN
    EXEC('ALTER TABLE dbo.SYS_Function ADD TheIcon varchar(50) NULL;');
END;

IF COL_LENGTH('dbo.SYS_Function', 'TheOrder') IS NULL
BEGIN
    EXEC('ALTER TABLE dbo.SYS_Function ADD TheOrder int NULL;');
END;

IF COL_LENGTH('dbo.SYS_Function', 'IsActive') IS NULL
BEGIN
    EXEC('ALTER TABLE dbo.SYS_Function ADD IsActive bit NULL;');
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.default_constraints dc
    INNER JOIN sys.columns c ON c.default_object_id = dc.object_id
    INNER JOIN sys.tables t ON t.object_id = c.object_id
    INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
    WHERE s.name = 'dbo'
      AND t.name = 'SYS_Module'
      AND c.name = 'TheIcon'
)
BEGIN
    EXEC('ALTER TABLE dbo.SYS_Module ADD CONSTRAINT DF_SYS_Module_TheIcon DEFAULT ('''') FOR TheIcon;');
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.default_constraints dc
    INNER JOIN sys.columns c ON c.default_object_id = dc.object_id
    INNER JOIN sys.tables t ON t.object_id = c.object_id
    INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
    WHERE s.name = 'dbo'
      AND t.name = 'SYS_Function'
      AND c.name = 'TheIcon'
)
BEGIN
    EXEC('ALTER TABLE dbo.SYS_Function ADD CONSTRAINT DF_SYS_Function_TheIcon DEFAULT ('''') FOR TheIcon;');
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.default_constraints dc
    INNER JOIN sys.columns c ON c.default_object_id = dc.object_id
    INNER JOIN sys.tables t ON t.object_id = c.object_id
    INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
    WHERE s.name = 'dbo'
      AND t.name = 'SYS_Function'
      AND c.name = 'TheOrder'
)
BEGIN
    EXEC('ALTER TABLE dbo.SYS_Function ADD CONSTRAINT DF_SYS_Function_TheOrder DEFAULT (0) FOR TheOrder;');
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.default_constraints dc
    INNER JOIN sys.columns c ON c.default_object_id = dc.object_id
    INNER JOIN sys.tables t ON t.object_id = c.object_id
    INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
    WHERE s.name = 'dbo'
      AND t.name = 'SYS_Function'
      AND c.name = 'IsActive'
)
BEGIN
    EXEC('ALTER TABLE dbo.SYS_Function ADD CONSTRAINT DF_SYS_Function_IsActive DEFAULT (1) FOR IsActive;');
END;

EXEC('UPDATE dbo.SYS_Module SET TheIcon = '''' WHERE TheIcon IS NULL;');
EXEC('UPDATE dbo.SYS_Function SET TheIcon = '''' WHERE TheIcon IS NULL;');
EXEC('UPDATE dbo.SYS_Function SET TheOrder = FunctionID WHERE TheOrder IS NULL;');
EXEC('UPDATE dbo.SYS_Function SET IsActive = 1 WHERE IsActive IS NULL;');

COMMIT;
