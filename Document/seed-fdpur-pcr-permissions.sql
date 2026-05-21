SET NOCOUNT ON;

BEGIN TRY
    BEGIN TRAN;

    DECLARE @RoleID int = (
        SELECT TOP 1 RoleID
        FROM dbo.SYS_Role
        WHERE RoleCode = 'FDPur'
    );

    DECLARE @ModuleID int = (
        SELECT TOP 1 ModuleID
        FROM dbo.SYS_Module
        WHERE ModuleCode = 'PCR'
    );

    IF @RoleID IS NULL
    BEGIN
        THROW 50001, 'Role FDPur not found.', 1;
    END;

    IF @ModuleID IS NULL
    BEGIN
        THROW 50002, 'Module PCR not found.', 1;
    END;

    DECLARE @Desired table
    (
        FunctionName varchar(200) NOT NULL,
        Permission varchar(4000) NOT NULL
    );

    INSERT INTO @Desired (FunctionName, Permission)
    VALUES
        ('Suppliers List', '1,2,3,4,5'),
        ('Purchase Requisition', '1,2,3,4,5,6,7'),
        ('Purchase Order', '1,2,3,4,5,6,7,8'),
        ('Supplier-PO Report', '1,2,3,4,5'),
        ('Analyzing Suppliers', '1,2,3,4,5'),
        ('Material Request', '1,2,3,4,5,6,7'),
        ('Approve Supplier', '1,2');

    UPDATE rp
    SET
        rp.Permission = d.Permission,
        rp.IsActive = 1
    FROM dbo.SYS_RolePermission rp
    INNER JOIN dbo.SYS_Function f ON f.FunctionID = rp.FunctionID
    INNER JOIN @Desired d ON d.FunctionName = f.FunctionName
    WHERE rp.RoleID = @RoleID
      AND f.ModuleID = @ModuleID;

    INSERT INTO dbo.SYS_RolePermission (RoleID, FunctionID, Permission, IsActive)
    SELECT @RoleID, f.FunctionID, d.Permission, 1
    FROM @Desired d
    INNER JOIN dbo.SYS_Function f ON f.FunctionName = d.FunctionName
        AND f.ModuleID = @ModuleID
    WHERE NOT EXISTS (
        SELECT 1
        FROM dbo.SYS_RolePermission rp
        WHERE rp.RoleID = @RoleID
          AND rp.FunctionID = f.FunctionID
    );

    IF EXISTS (
        SELECT 1
        FROM @Desired d
        WHERE NOT EXISTS (
            SELECT 1
            FROM dbo.SYS_Function f
            WHERE f.FunctionName = d.FunctionName
              AND f.ModuleID = @ModuleID
        )
    )
    BEGIN
        THROW 50003, 'One or more PCR functions were not found.', 1;
    END;

    COMMIT;

    PRINT 'Seed completed for Role FDPur, Module PCR permissions.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;
    THROW;
END CATCH;

