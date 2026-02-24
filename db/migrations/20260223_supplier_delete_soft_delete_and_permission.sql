/* Supplier delete support
   - Hard delete for Draft suppliers (handled in app)
   - Soft delete for non-Draft suppliers (IsDeleted/DeletedBy/DeletedDate)
   - Add permission 6 = Delete for Supplier FunctionID = 71
*/

IF COL_LENGTH('dbo.PC_Suppliers', 'IsDeleted') IS NULL
BEGIN
    ALTER TABLE dbo.PC_Suppliers
    ADD IsDeleted bit NOT NULL
        CONSTRAINT DF_PC_Suppliers_IsDeleted DEFAULT (0);
END;
GO

IF COL_LENGTH('dbo.PC_Suppliers', 'DeletedBy') IS NULL
BEGIN
    ALTER TABLE dbo.PC_Suppliers
    ADD DeletedBy nvarchar(50) NULL;
END;
GO

IF COL_LENGTH('dbo.PC_Suppliers', 'DeletedDate') IS NULL
BEGIN
    ALTER TABLE dbo.PC_Suppliers
    ADD DeletedDate datetime NULL;
END;
GO

IF NOT EXISTS (
    SELECT 1
    FROM dbo.SYS_FuncPermission
    WHERE FunctionID = 71
      AND PermissionNo = 6
)
BEGIN
    INSERT INTO dbo.SYS_FuncPermission (FunctionID, PermissionNo, PermissionName)
    VALUES (71, 6, N'Delete');
END;
GO

