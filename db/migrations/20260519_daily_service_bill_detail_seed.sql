SET NOCOUNT ON;

IF NOT EXISTS (SELECT 1 FROM dbo.SV_BillStatus WHERE StatusID = 1)
BEGIN
    INSERT INTO dbo.SV_BillStatus (StatusID, StatusName)
    VALUES (1, 'Pending (Not Paid)');
END;

IF NOT EXISTS (SELECT 1 FROM dbo.SV_BillStatus WHERE StatusID = 2)
BEGIN
    INSERT INTO dbo.SV_BillStatus (StatusID, StatusName)
    VALUES (2, 'Paid');
END;

IF NOT EXISTS (SELECT 1 FROM dbo.SV_BillStatus WHERE StatusID = 5)
BEGIN
    INSERT INTO dbo.SV_BillStatus (StatusID, StatusName)
    VALUES (5, 'Cancel');
END;

UPDATE dbo.SYS_FuncPermission
SET Url = '/Cus/DailyServiceBill/DailyServiceBillDetail'
WHERE FunctionID = 42
  AND PermissionNo IN (2, 4)
  AND (Url IS NULL OR Url = '');

UPDATE dbo.SV_Bill
SET VNDAmountVAT = ROUND(ISNULL(VNDAmountBefVAT, 0) / PctTax, 2),
    VNDAmount = ROUND(ISNULL(VNDAmountBefVAT, 0) + ROUND(ISNULL(VNDAmountBefVAT, 0) / PctTax, 2), 0)
WHERE LinenDeliveryID IS NOT NULL
  AND ISNULL(PctTax, 0) > 0
  AND (VNDAmountVAT IS NULL OR VNDAmountVAT = 0);

