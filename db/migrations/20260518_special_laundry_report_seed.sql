SET NOCOUNT ON;

DECLARE @FunctionID int = 161;
DECLARE @ModuleID int = 6;
DECLARE @FunctionName varchar(50) = 'Special Laundry Report';
DECLARE @FormName varchar(50) = 'frmSpecialLaundryReport';
DECLARE @IndexUrl varchar(100) = '/Inventory/SpecialLaundryReport/Index';

IF NOT EXISTS (SELECT 1 FROM dbo.SYS_Function WHERE FunctionID = @FunctionID)
BEGIN
    SET IDENTITY_INSERT dbo.SYS_Function ON;

    INSERT INTO dbo.SYS_Function (FunctionID, ModuleID, FunctionName, FormName, url)
    VALUES (@FunctionID, @ModuleID, @FunctionName, @FormName, @IndexUrl);

    SET IDENTITY_INSERT dbo.SYS_Function OFF;
END
ELSE
BEGIN
    UPDATE dbo.SYS_Function
    SET ModuleID = @ModuleID,
        FunctionName = @FunctionName,
        FormName = @FormName,
        url = @IndexUrl
    WHERE FunctionID = @FunctionID;
END;

IF NOT EXISTS (
    SELECT 1
    FROM dbo.SYS_FuncPermission
    WHERE FunctionID = @FunctionID
      AND PermissionNo = '1'
)
BEGIN
    INSERT INTO dbo.SYS_FuncPermission (FunctionID, PermissionNo, PermissionName, url)
    VALUES (@FunctionID, '1', 'View', @IndexUrl);
END
ELSE
BEGIN
    UPDATE dbo.SYS_FuncPermission
    SET PermissionName = 'View',
        url = @IndexUrl
    WHERE FunctionID = @FunctionID
      AND PermissionNo = '1';
END;

;WITH SourceRoles AS
(
    SELECT DISTINCT RoleID
    FROM dbo.SYS_RolePermission
    WHERE FunctionID IN (116, 118)
      AND ISNULL(IsActive, 1) = 1
      AND (',' + ISNULL(Permission, '') + ',') LIKE '%,1,%'
    UNION
    SELECT RoleID
    FROM dbo.SYS_Role
    WHERE IsAdminRole = 1
)
INSERT INTO dbo.SYS_RolePermission (RoleID, FunctionID, Permission, IsActive)
SELECT RoleID, @FunctionID, '1', 1
FROM SourceRoles src
WHERE NOT EXISTS (
    SELECT 1
    FROM dbo.SYS_RolePermission rp
    WHERE rp.RoleID = src.RoleID
      AND rp.FunctionID = @FunctionID
);

UPDATE rp
SET Permission = CASE
        WHEN (',' + ISNULL(rp.Permission, '') + ',') LIKE '%,1,%' THEN rp.Permission
        WHEN NULLIF(LTRIM(RTRIM(ISNULL(rp.Permission, ''))), '') IS NULL THEN '1'
        ELSE rp.Permission + ',1'
    END,
    IsActive = 1
FROM dbo.SYS_RolePermission rp
WHERE rp.FunctionID = @FunctionID;

SELECT FunctionID, ModuleID, FunctionName, FormName, url
FROM dbo.SYS_Function
WHERE FunctionID = @FunctionID;

SELECT FunctionID, PermissionNo, PermissionName, url
FROM dbo.SYS_FuncPermission
WHERE FunctionID = @FunctionID
ORDER BY PermissionNo;

SELECT RoleID, FunctionID, Permission, IsActive
FROM dbo.SYS_RolePermission
WHERE FunctionID = @FunctionID
ORDER BY RoleID;
