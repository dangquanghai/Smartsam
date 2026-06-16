/*
    Laundry Record web cache schema check.

    Purpose:
    - Keep legacy dbo.LN_LaudryRecord_TMP as the shared cache table.
    - Ensure dbo.View_LNLinenRecord points to dbo.LN_LaudryRecord_TMP.
    - No new table is created for the web cache.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID('dbo.LN_LaudryRecord_TMP', 'U') IS NULL
BEGIN
    THROW 51002, 'Missing legacy table dbo.LN_LaudryRecord_TMP.', 1;
END;
GO

CREATE OR ALTER VIEW dbo.View_LNLinenRecord
AS
SELECT TOP (100) PERCENT
       rec.UserCode,
       rec.MyMonth,
       rec.MyYear,
       rec.SupplierID,
       rec.GroupID,
       rec.Express,
       rec.Price,
       dbo.PC_Suppliers.SupplierName,
       SUM(rec.D01) AS D01,
       SUM(rec.D02) AS D02,
       SUM(rec.D03) AS D03,
       SUM(rec.D04) AS D04,
       SUM(rec.D05) AS D05,
       SUM(rec.D06) AS D06,
       SUM(rec.D07) AS D07,
       SUM(rec.D08) AS D08,
       SUM(rec.D09) AS D09,
       SUM(rec.D10) AS D10,
       SUM(rec.D11) AS D11,
       SUM(rec.D12) AS D12,
       SUM(rec.D13) AS D13,
       SUM(rec.D14) AS D14,
       SUM(rec.D15) AS D15,
       SUM(rec.D16) AS D16,
       SUM(rec.D17) AS D17,
       SUM(rec.D18) AS D18,
       SUM(rec.D19) AS D19,
       SUM(rec.D20) AS D20,
       SUM(rec.D21) AS D21,
       SUM(rec.D22) AS D22,
       SUM(rec.D23) AS D23,
       SUM(rec.D24) AS D24,
       SUM(rec.D25) AS D25,
       SUM(rec.D26) AS D26,
       SUM(rec.D27) AS D27,
       SUM(rec.D28) AS D28,
       SUM(rec.D29) AS D29,
       SUM(rec.D30) AS D30,
       SUM(rec.D31) AS D31,
       rec.FromDate,
       rec.ToDate,
       dbo.LN_Linnen.LinnenCode AS LinenCode
FROM dbo.LN_LaudryRecord_TMP rec
INNER JOIN dbo.PC_Suppliers ON rec.SupplierID = dbo.PC_Suppliers.SupplierID
INNER JOIN dbo.LN_Linnen ON rec.LinnenID = dbo.LN_Linnen.ID
GROUP BY rec.UserCode,
         rec.MyMonth,
         rec.MyYear,
         rec.SupplierID,
         rec.LinenCode,
         rec.Express,
         rec.Price,
         rec.GroupID,
         dbo.PC_Suppliers.SupplierName,
         rec.FromDate,
         rec.ToDate,
         dbo.LN_Linnen.LinnenCode
ORDER BY rec.SupplierID, rec.GroupID, LinenCode;
GO

PRINT 'dbo.View_LNLinenRecord points to dbo.LN_LaudryRecord_TMP.';
