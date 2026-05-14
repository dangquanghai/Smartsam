SET NOCOUNT ON;

UPDATE dbo.SYS_Function
SET url = '/Inventory/LinenReport/Index'
WHERE FunctionID = 118;

UPDATE dbo.SYS_FuncPermission
SET url = '/Inventory/LinenReport/Index'
WHERE FunctionID = 118
  AND PermissionNo = 1;

SELECT FunctionID, FunctionName, FormName, url
FROM dbo.SYS_Function
WHERE FunctionID = 118;

SELECT FunctionID, PermissionNo, PermissionName, url
FROM dbo.SYS_FuncPermission
WHERE FunctionID = 118
ORDER BY PermissionNo;
