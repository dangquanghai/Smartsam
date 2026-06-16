using System.Data;
using Microsoft.Data.SqlClient;

namespace SmartSam.Services;

public class LinenLaundryRecordCacheService
{
    public const string CacheUserCode = "WEBRPT";

    private readonly IConfiguration _config;

    public LinenLaundryRecordCacheService(IConfiguration config)
    {
        _config = config;
    }

    public async Task CollectRecentLaundryRecordCache()
    {
        var today = DateTime.Today;
        using (var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection")))
        {
            await conn.OpenAsync();

            for (var offset = 3; offset >= 0; offset--)
            {
                await CollectLaundryRecordCacheForDate(conn, today.AddDays(-offset));
            }
        }
    }

    private static async Task CollectLaundryRecordCacheForDate(SqlConnection conn, DateTime date)
    {
        var dayColumn = $"D{date.Day:00}";
        var fromDate = date.Date.AddSeconds(1);
        var toDate = date.Date.AddDays(1).AddSeconds(-1);

        var sql = $@"
UPDATE dbo.LN_ReceiveDT SET Express = 0 WHERE Express IS NULL;
UPDATE dbo.LN_ReceiveDT SET IsChild = 0 WHERE IsChild IS NULL;

UPDATE dbo.LN_LaudryRecord_TMP
SET {dayColumn} = 0,
    FromDate = @MonthFromDate,
    ToDate = @MonthToDate
WHERE UserCode = @UserCode
  AND MyMonth = @Month
  AND MyYear = @Year;

IF OBJECT_ID('tempdb..#LaundryRecordDay') IS NOT NULL
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
SET {dayColumn} = src.Quantity,
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
";

        using (var cmd = new SqlCommand(sql, conn))
        {
            cmd.CommandTimeout = 300;
            cmd.Parameters.Add("@UserCode", SqlDbType.VarChar, 15).Value = CacheUserCode;
            cmd.Parameters.Add("@Month", SqlDbType.Int).Value = date.Month;
            cmd.Parameters.Add("@Year", SqlDbType.Int).Value = date.Year;
            cmd.Parameters.Add("@FromDate", SqlDbType.DateTime).Value = fromDate;
            cmd.Parameters.Add("@ToDate", SqlDbType.DateTime).Value = toDate;
            cmd.Parameters.Add("@MonthFromDate", SqlDbType.DateTime).Value = new DateTime(date.Year, date.Month, 1).AddSeconds(1);
            cmd.Parameters.Add("@MonthToDate", SqlDbType.DateTime).Value = new DateTime(date.Year, date.Month, DateTime.DaysInMonth(date.Year, date.Month)).AddDays(1).AddSeconds(-1);
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
