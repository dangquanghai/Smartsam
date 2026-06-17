/*
    Laundry Record cache backfill for web report.

    Purpose:
    - Backfill dbo.LN_LaudryRecord_TMP for UserCode WEBRPT before enabling nightly Hangfire cache job.
    - Uses the same set-based logic as LinenLaundryRecordCacheService.
    - Does NOT delete the whole user cache. It resets/rebuilds only Dxx columns for the requested dates.

    How to use:
    1. Set @BackfillFromDate and @BackfillToDate.
    2. Run once with @DryRun = 1 and review messages.
    3. Set @DryRun = 0 and run again to commit data.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @BackfillFromDate date = '2026-06-01';
DECLARE @BackfillToDate date = '2026-06-30';
DECLARE @UserCode varchar(15) = 'WEBRPT';
DECLARE @DryRun bit = 1;

IF @BackfillFromDate IS NULL OR @BackfillToDate IS NULL
BEGIN
    THROW 51000, 'Backfill date range is required.', 1;
END;

IF @BackfillFromDate > @BackfillToDate
BEGIN
    THROW 51001, '@BackfillFromDate must be <= @BackfillToDate.', 1;
END;

IF OBJECT_ID('dbo.LN_LaudryRecord_TMP', 'U') IS NULL
BEGIN
    THROW 51002, 'Missing table dbo.LN_LaudryRecord_TMP.', 1;
END;

IF OBJECT_ID('dbo.LN_ReceiveMT', 'U') IS NULL OR OBJECT_ID('dbo.LN_ReceiveDT', 'U') IS NULL OR OBJECT_ID('dbo.LN_DeliveryMT', 'U') IS NULL
BEGIN
    THROW 51003, 'Missing required linen receive/delivery tables.', 1;
END;

DECLARE @WorkDate date = @BackfillFromDate;
DECLARE @DayColumn sysname;
DECLARE @Month int;
DECLARE @Year int;
DECLARE @FromDate datetime;
DECLARE @ToDate datetime;
DECLARE @MonthFromDate datetime;
DECLARE @MonthToDate datetime;
DECLARE @RowsUpdated int;
DECLARE @Sql nvarchar(max);

BEGIN TRY
    IF @DryRun = 1
    BEGIN
        BEGIN TRANSACTION;
        PRINT 'DRY RUN enabled. All changes will be rolled back.';
    END;

    UPDATE dbo.LN_ReceiveDT SET Express = 0 WHERE Express IS NULL;
    UPDATE dbo.LN_ReceiveDT SET IsChild = 0 WHERE IsChild IS NULL;

    WHILE @WorkDate <= @BackfillToDate
    BEGIN
        SET @DayColumn = QUOTENAME('D' + RIGHT('0' + CONVERT(varchar(2), DAY(@WorkDate)), 2));
        SET @Month = MONTH(@WorkDate);
        SET @Year = YEAR(@WorkDate);
        SET @FromDate = DATEADD(second, 1, CONVERT(datetime, @WorkDate));
        SET @ToDate = DATEADD(second, -1, DATEADD(day, 1, CONVERT(datetime, @WorkDate)));
        SET @MonthFromDate = DATEADD(second, 1, CONVERT(datetime, DATEFROMPARTS(@Year, @Month, 1)));
        SET @MonthToDate = DATEADD(second, -1, DATEADD(day, 1, CONVERT(datetime, EOMONTH(@WorkDate))));
        SET @RowsUpdated = 0;

        SET @Sql = N'
UPDATE dbo.LN_LaudryRecord_TMP
SET ' + @DayColumn + N' = 0,
    FromDate = @MonthFromDate,
    ToDate = @MonthToDate
WHERE UserCode = @UserCode
  AND MyMonth = @Month
  AND MyYear = @Year;

IF OBJECT_ID(''tempdb..#LaundryRecordDay'') IS NOT NULL
BEGIN
    DROP TABLE #LaundryRecordDay;
END;

SELECT dbo.LN_DeliveryMT.SupplierID,
       CASE
           WHEN dbo.LN_ReceiveDT.LocationID = 1000 THEN 1
           WHEN dbo.LN_ReceiveDT.LocationID = 1001 THEN 2
           ELSE 5
       END AS GroupID,
       dbo.LN_ReceiveDT.LinnenID,
       ISNULL(dbo.LN_ReceiveDT.Express, 0) AS Express,
       dbo.LN_ReceiveDT.Price,
       MIN(dbo.LN_ReceiveDT.LocationID) AS LocationID,
       SUM(dbo.LN_ReceiveDT.Quantity) AS Quantity
INTO #LaundryRecordDay
FROM dbo.LN_ReceiveMT
INNER JOIN dbo.LN_ReceiveDT ON dbo.LN_ReceiveMT.ReceiveID = dbo.LN_ReceiveDT.ReceiveID
LEFT OUTER JOIN dbo.LN_DeliveryMT ON dbo.LN_ReceiveMT.SendID = dbo.LN_DeliveryMT.DeliveryID
WHERE dbo.LN_DeliveryMT.DeliveryDate >= @FromDate
  AND dbo.LN_DeliveryMT.DeliveryDate <= @ToDate
  AND MONTH(dbo.LN_DeliveryMT.DeliveryDate) = @Month
  AND YEAR(dbo.LN_DeliveryMT.DeliveryDate) = @Year
  AND dbo.LN_ReceiveDT.Quantity > 0
GROUP BY dbo.LN_DeliveryMT.SupplierID,
         CASE
             WHEN dbo.LN_ReceiveDT.LocationID = 1000 THEN 1
             WHEN dbo.LN_ReceiveDT.LocationID = 1001 THEN 2
             ELSE 5
         END,
         dbo.LN_ReceiveDT.LinnenID,
         ISNULL(dbo.LN_ReceiveDT.Express, 0),
         dbo.LN_ReceiveDT.Price;

INSERT INTO dbo.LN_LaudryRecord_TMP(UserCode, SupplierID, GroupID, LinnenID, Express, Price, MyMonth, MyYear, FromDate, ToDate, LocationID)
SELECT @UserCode,
       src.SupplierID,
       src.GroupID,
       src.LinnenID,
       src.Express,
       src.Price,
       @Month,
       @Year,
       @MonthFromDate,
       @MonthToDate,
       src.LocationID
FROM #LaundryRecordDay src
WHERE NOT EXISTS (
    SELECT 1
    FROM dbo.LN_LaudryRecord_TMP tmp
    WHERE tmp.UserCode = @UserCode
      AND tmp.SupplierID = src.SupplierID
      AND tmp.GroupID = src.GroupID
      AND tmp.LinnenID = src.LinnenID
      AND ISNULL(tmp.Express, 0) = src.Express
      AND ISNULL(tmp.Price, -1) = ISNULL(src.Price, -1)
      AND tmp.MyMonth = @Month
      AND tmp.MyYear = @Year
);

UPDATE tmp
SET ' + @DayColumn + N' = src.Quantity,
    FromDate = @MonthFromDate,
    ToDate = @MonthToDate,
    LocationID = src.LocationID
FROM dbo.LN_LaudryRecord_TMP tmp
INNER JOIN #LaundryRecordDay src ON tmp.UserCode = @UserCode
    AND tmp.SupplierID = src.SupplierID
    AND tmp.GroupID = src.GroupID
    AND tmp.LinnenID = src.LinnenID
    AND ISNULL(tmp.Express, 0) = src.Express
    AND ISNULL(tmp.Price, -1) = ISNULL(src.Price, -1)
    AND tmp.MyMonth = @Month
    AND tmp.MyYear = @Year;

SET @RowsUpdated = @@ROWCOUNT;
';

        EXEC sp_executesql
            @Sql,
            N'@UserCode varchar(15), @Month int, @Year int, @FromDate datetime, @ToDate datetime, @MonthFromDate datetime, @MonthToDate datetime, @RowsUpdated int OUTPUT',
            @UserCode = @UserCode,
            @Month = @Month,
            @Year = @Year,
            @FromDate = @FromDate,
            @ToDate = @ToDate,
            @MonthFromDate = @MonthFromDate,
            @MonthToDate = @MonthToDate,
            @RowsUpdated = @RowsUpdated OUTPUT;

        PRINT CONVERT(varchar(10), @WorkDate, 120) + ' -> updated rows: ' + CONVERT(varchar(20), @RowsUpdated);
        SET @WorkDate = DATEADD(day, 1, @WorkDate);
    END;

    IF @DryRun = 1
    BEGIN
        ROLLBACK TRANSACTION;
        PRINT 'DRY RUN complete. Changes rolled back.';
    END
    ELSE
    BEGIN
        PRINT 'Backfill complete. Changes committed.';
    END;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
    BEGIN
        ROLLBACK TRANSACTION;
    END;

    THROW;
END CATCH;
