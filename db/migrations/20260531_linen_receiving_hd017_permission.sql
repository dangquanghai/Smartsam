SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRAN;

DECLARE @FunctionID int = 117;

UPDATE dbo.SYS_Function
SET url = '/Inventory/LinenReceiving/Index'
WHERE FunctionID = @FunctionID;

UPDATE dbo.SYS_FuncPermission
SET url = '/Inventory/LinenReceiving/Index'
WHERE FunctionID = @FunctionID
  AND PermissionNo = 1;

UPDATE dbo.SYS_FuncPermission
SET url = '/Inventory/LinenReceiving/LinenReceivingDetail'
WHERE FunctionID = @FunctionID
  AND PermissionNo = 2;

DECLARE @TargetRoles TABLE
(
    RoleID int NOT NULL PRIMARY KEY
);

INSERT INTO @TargetRoles (RoleID)
SELECT DISTINCT rm.RoleID
FROM dbo.SYS_RoleMember rm
INNER JOIN dbo.MS_Employee e ON e.EmployeeID = rm.Operator
WHERE e.EmployeeCode = 'HD017';

UPDATE rp
SET Permission = '1,2',
    IsActive = 1
FROM dbo.SYS_RolePermission rp
INNER JOIN @TargetRoles tr ON tr.RoleID = rp.RoleID
WHERE rp.FunctionID = @FunctionID;

INSERT INTO dbo.SYS_RolePermission (RoleID, FunctionID, Permission, IsActive)
SELECT tr.RoleID,
       @FunctionID,
       '1,2',
       1
FROM @TargetRoles tr
WHERE NOT EXISTS (
    SELECT 1
    FROM dbo.SYS_RolePermission rp
    WHERE rp.RoleID = tr.RoleID
      AND rp.FunctionID = @FunctionID
);

COMMIT;

SELECT e.EmployeeCode, rm.RoleID, rp.FunctionID, rp.Permission, rp.IsActive
FROM dbo.MS_Employee e
INNER JOIN dbo.SYS_RoleMember rm ON rm.Operator = e.EmployeeID
LEFT JOIN dbo.SYS_RolePermission rp ON rp.RoleID = rm.RoleID AND rp.FunctionID = @FunctionID
WHERE e.EmployeeCode = 'HD017';

SELECT FunctionID, PermissionNo, PermissionName, url
FROM dbo.SYS_FuncPermission
WHERE FunctionID = @FunctionID
ORDER BY PermissionNo;
