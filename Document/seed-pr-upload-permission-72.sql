SET NOCOUNT ON;

BEGIN TRY
    BEGIN TRAN;

    IF NOT EXISTS (
        SELECT 1
        FROM dbo.SYS_FuncPermission
        WHERE FunctionID = 72 AND PermissionNo = 6
    )
    BEGIN
        INSERT INTO dbo.SYS_FuncPermission (FunctionID, PermissionNo, PermissionName, url)
        VALUES (72, 6, 'Change Status', NULL);
    END;

    IF NOT EXISTS (
        SELECT 1
        FROM dbo.SYS_FuncPermission
        WHERE FunctionID = 72 AND PermissionNo = 7
    )
    BEGIN
        INSERT INTO dbo.SYS_FuncPermission (FunctionID, PermissionNo, PermissionName, url)
        VALUES (72, 7, 'Upload Attachment', NULL);
    END;

    DECLARE @RoleWithUpload int = 31; -- FDPur
    DECLARE @CurrentPermission varchar(4000) = (
        SELECT TOP 1 ISNULL(Permission, '')
        FROM dbo.SYS_RolePermission
        WHERE FunctionID = 72 AND RoleID = @RoleWithUpload
    );

    IF @CurrentPermission IS NOT NULL
       AND (',' + REPLACE(@CurrentPermission, ' ', '') + ',') NOT LIKE '%,7,%'
    BEGIN
        UPDATE dbo.SYS_RolePermission
        SET Permission = CASE
            WHEN LTRIM(RTRIM(ISNULL(Permission, ''))) = '' THEN '7'
            ELSE CONCAT(Permission, ',7')
        END
        WHERE FunctionID = 72 AND RoleID = @RoleWithUpload;
    END;

    DECLARE @EncryptedAbc123 varchar(200) = '46129.8930871412-79-78-77-127-126-125-46130.8930871412';

    UPDATE dbo.MS_Employee
    SET NewPassword = @EncryptedAbc123
    WHERE EmployeeCode IN ('FD031', 'CD046');

    COMMIT;

    PRINT 'Seed completed for FunctionID=72 Permission=7 (Upload Attachment).';
    PRINT 'Account WITH upload permission: FD031 (Role 31 - FDPur).';
    PRINT 'Account WITHOUT upload permission: CD046 (Role 4 - CssM).';
    PRINT 'Both passwords reset to: abc123';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;
    THROW;
END CATCH;

