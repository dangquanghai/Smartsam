using System.Data;
using System.Linq;
using Microsoft.Data.SqlClient;
using SmartSam.Helpers;

namespace SmartSam.Pages.Purchasing.MaterialRequest;

public class MaterialRequestService
{
    private readonly string _connectionString;

    public MaterialRequestService(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Missing connection string: DefaultConnection");
    }

    public async Task<EmployeeMaterialScopeDto> GetEmployeeScopeAsync(string? employeeCode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(employeeCode))
        {
            return new EmployeeMaterialScopeDto();
        }

        const string sql = @"
            SELECT TOP 1
                StoreGR,
                ISNULL(Approval, 0) AS ApprovalLevel,
                ISNULL(HeadDept, 0) AS IsHeadDept
            FROM dbo.MS_Employee
            WHERE EmployeeCode = @EmployeeCode";

        return await Helper.QuerySingleOrDefaultAsync(
            _connectionString,
            sql,
            rd => new EmployeeMaterialScopeDto
            {
                StoreGroup = rd.IsDBNull(0) ? null : Convert.ToInt32(rd[0]),
                ApprovalLevel = rd.IsDBNull(1) ? 0 : Convert.ToInt32(rd[1]),
                IsHeadDept = !rd.IsDBNull(2) && Convert.ToInt32(rd[2]) == 1
            },
            cmd => Helper.AddParameter(cmd, "@EmployeeCode", employeeCode.Trim(), SqlDbType.VarChar, 50),
            cancellationToken) ?? new EmployeeMaterialScopeDto();
    }

    public async Task<IReadOnlyList<MaterialRequestLookupOptionDto>> GetStoreGroupsAsync(CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT KPGroupID, KPGroupName
            FROM dbo.INV_KPGroup
            ORDER BY KPGroupID";

        var rows = await Helper.QueryAsync(
            _connectionString,
            sql,
            rd => new MaterialRequestLookupOptionDto
            {
                Id = rd.GetInt32(0),
                Name = rd[1]?.ToString() ?? $"Store Group {rd.GetInt32(0)}"
            },
            null,
            cancellationToken);

        if (rows.Count > 0)
        {
            return rows;
        }

        const string fallbackSql = @"
            SELECT DISTINCT CAST(StoreGR AS int) AS StoreGroup
            FROM dbo.MS_Employee
            WHERE StoreGR IS NOT NULL AND StoreGR > 0
            ORDER BY StoreGroup";

        rows = await Helper.QueryAsync(
            _connectionString,
            fallbackSql,
            rd => new MaterialRequestLookupOptionDto
            {
                Id = rd.GetInt32(0),
                Name = $"Store Group {rd.GetInt32(0)}"
            },
            null,
            cancellationToken);

        return rows;
    }

    public async Task<IReadOnlyList<MaterialRequestLookupOptionDto>> GetStatusesAsync(CancellationToken cancellationToken = default)
    {
        static string NormalizeStatusName(int id, string currentName)
        {
            return id switch
            {
                0 => "Just Created",
                1 => "Submitted by Owner",
                2 => "Head Dept Approved",
                3 => "Purchaser Checked",
                4 => "Approved by Chief Accounting",
                5 => "Rejected",
                _ => currentName
            };
        }

        const string sql = @"
            SELECT MaterialStatusID, MaterialStatusName
            FROM dbo.MaterialStatus
            ORDER BY MaterialStatusID";

        var rows = await Helper.QueryAsync(
            _connectionString,
            sql,
            rd => new MaterialRequestLookupOptionDto
            {
                Id = rd.GetInt32(0),
                Name = NormalizeStatusName(rd.GetInt32(0), rd[1]?.ToString() ?? string.Empty)
            },
            null,
            cancellationToken);

        if (rows.Count > 0)
        {
            return rows;
        }

        return
        [
            new MaterialRequestLookupOptionDto { Id = 0, Name = "Just Created" },
            new MaterialRequestLookupOptionDto { Id = 1, Name = "Submitted by Owner" },
            new MaterialRequestLookupOptionDto { Id = 2, Name = "Head Dept Approved" },
            new MaterialRequestLookupOptionDto { Id = 3, Name = "Purchaser Checked" },
            new MaterialRequestLookupOptionDto { Id = 4, Name = "Approved by Chief Accounting" },
            new MaterialRequestLookupOptionDto { Id = 5, Name = "Rejected" }
        ];
    }

    public async Task<MaterialRequestSearchResultDto> SearchPagedAsync(MaterialRequestFilterCriteria criteria, CancellationToken cancellationToken = default)
    {
        var pageIndex = criteria.PageIndex.GetValueOrDefault() <= 0 ? 1 : criteria.PageIndex!.Value;
        var pageSize = criteria.PageSize.GetValueOrDefault() <= 0 ? 25 : criteria.PageSize!.Value;
        var statusIds = (criteria.StatusIds ?? [])
            .Distinct()
            .ToList();
        var statusFilterSql = BuildStatusFilterSql(statusIds);

        var fromWhereSql = $@"
            FROM dbo.MATERIAL_REQUEST r
            LEFT JOIN dbo.MaterialStatus ms ON ms.MaterialStatusID = r.MATERIALSTATUSID
            LEFT JOIN dbo.INV_KPGroup kp ON kp.KPGroupID = r.STORE_GROUP
            WHERE
                (@RequestNo IS NULL OR r.REQUEST_NO = @RequestNo)
                AND (@StoreGroup IS NULL OR r.STORE_GROUP = @StoreGroup)
                AND {statusFilterSql}
                AND (@NoIssue IS NULL OR r.NO_ISSUE = @NoIssue)
                AND (@IsAuto IS NULL OR r.IS_AUTO = @IsAuto)
                AND (@FromDate IS NULL OR r.DATE_CREATE >= @FromDate)
                AND (@ToDate IS NULL OR r.DATE_CREATE < DATEADD(DAY, 1, @ToDate))
                AND (@AccordingTo IS NULL OR r.ACCORDINGTO LIKE '%' + @AccordingTo + '%')
                AND (
                    @ItemCode IS NULL OR EXISTS (
                        SELECT 1
                        FROM dbo.MATERIAL_REQUEST_DETAIL d
                        WHERE d.REQUEST_NO = r.REQUEST_NO
                        AND d.ITEMCODE LIKE '%' + @ItemCode + '%'
                    )
                )
                AND (
                    @BuyGreaterThanZero IS NULL OR EXISTS (
                        SELECT 1
                        FROM dbo.MATERIAL_REQUEST_DETAIL d2
                        WHERE d2.REQUEST_NO = r.REQUEST_NO
                        AND ISNULL(d2.BUY, 0) > 0
                    )
                )";

        var countSql = "SELECT COUNT(1) " + fromWhereSql;
        var totalObj = await Helper.ExecuteScalarAsync(
            _connectionString,
            countSql,
            cmd => BindSearchParams(cmd, criteria, statusIds),
            cancellationToken);
        var totalCount = Convert.ToInt32(totalObj);

        var querySql = $@"
            SELECT
                CAST(r.REQUEST_NO AS bigint) AS REQUEST_NO,
                CAST(r.STORE_GROUP AS int) AS STORE_GROUP,
                ISNULL(kp.KPGroupName, CONCAT('Store Group ', CAST(r.STORE_GROUP AS varchar(20)))) AS KPGroupName,
                r.DATE_CREATE,
                r.FROM_DATE,
                r.TO_DATE,
                ISNULL(r.ACCORDINGTO, '') AS ACCORDINGTO,
                ISNULL(r.APPROVAL, 0) AS APPROVAL,
                ISNULL(r.APPROVAL_END, 0) AS APPROVAL_END,
                ISNULL(r.IS_AUTO, 0) AS IS_AUTO,
                ISNULL(r.POST_PR, 0) AS POST_PR,
                r.MATERIALSTATUSID,
                ISNULL(ms.MaterialStatusName, '') AS MaterialStatusName,
                r.NO_ISSUE,
                ISNULL(r.PRNO, '') AS PRNO
            {fromWhereSql}
            ORDER BY r.DATE_CREATE DESC, r.REQUEST_NO DESC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

        var rows = await Helper.QueryAsync(
            _connectionString,
            querySql,
            rd => new MaterialRequestListRowDto
            {
                RequestNo = rd.IsDBNull(0) ? 0 : rd.GetInt64(0),
                StoreGroup = rd.IsDBNull(1) ? null : rd.GetInt32(1),
                KPGroupName = rd[2]?.ToString() ?? string.Empty,
                DateCreate = rd.IsDBNull(3) ? null : rd.GetDateTime(3),
                FromDate = rd.IsDBNull(4) ? null : rd.GetDateTime(4),
                ToDate = rd.IsDBNull(5) ? null : rd.GetDateTime(5),
                AccordingTo = rd[6]?.ToString() ?? string.Empty,
                Approval = !rd.IsDBNull(7) && Convert.ToBoolean(rd[7]),
                ApprovalEnd = !rd.IsDBNull(8) && Convert.ToBoolean(rd[8]),
                IsAuto = !rd.IsDBNull(9) && Convert.ToBoolean(rd[9]),
                PostPr = !rd.IsDBNull(10) && Convert.ToBoolean(rd[10]),
                MaterialStatusId = rd.IsDBNull(11) ? null : Convert.ToInt32(rd[11]),
                MaterialStatusName = rd[12]?.ToString() ?? string.Empty,
                NoIssue = rd.IsDBNull(13) ? null : Convert.ToInt32(rd[13]),
                PrNo = rd[14]?.ToString() ?? string.Empty
            },
            cmd =>
            {
                BindSearchParams(cmd, criteria, statusIds);
                Helper.AddParameter(cmd, "@Offset", (pageIndex - 1) * pageSize, SqlDbType.Int);
                Helper.AddParameter(cmd, "@PageSize", pageSize, SqlDbType.Int);
            },
            cancellationToken);

        return new MaterialRequestSearchResultDto
        {
            Rows = rows,
            TotalCount = totalCount
        };
    }

    public async Task<MaterialRequestDetailDto?> GetDetailAsync(long requestNo, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT
                CAST(REQUEST_NO AS bigint) AS REQUEST_NO,
                CAST(STORE_GROUP AS int) AS STORE_GROUP,
                DATE_CREATE,
                ACCORDINGTO,
                ISNULL(APPROVAL, 0) AS APPROVAL,
                ISNULL(APPROVAL_END, 0) AS APPROVAL_END,
                ISNULL(POST_PR, 0) AS POST_PR,
                ISNULL(IS_AUTO, 0) AS IS_AUTO,
                FROM_DATE,
                TO_DATE,
                MATERIALSTATUSID,
                PRNO,
                NO_ISSUE
            FROM dbo.MATERIAL_REQUEST
            WHERE REQUEST_NO = @RequestNo";

        return await Helper.QuerySingleOrDefaultAsync(
            _connectionString,
            sql,
            rd => new MaterialRequestDetailDto
            {
                RequestNo = rd.IsDBNull(0) ? 0 : rd.GetInt64(0),
                StoreGroup = rd.IsDBNull(1) ? null : rd.GetInt32(1),
                DateCreate = rd.IsDBNull(2) ? null : rd.GetDateTime(2),
                AccordingTo = rd[3]?.ToString(),
                Approval = !rd.IsDBNull(4) && Convert.ToBoolean(rd[4]),
                ApprovalEnd = !rd.IsDBNull(5) && Convert.ToBoolean(rd[5]),
                PostPr = !rd.IsDBNull(6) && Convert.ToBoolean(rd[6]),
                IsAuto = !rd.IsDBNull(7) && Convert.ToBoolean(rd[7]),
                FromDate = rd.IsDBNull(8) ? null : rd.GetDateTime(8),
                ToDate = rd.IsDBNull(9) ? null : rd.GetDateTime(9),
                MaterialStatusId = rd.IsDBNull(10) ? null : Convert.ToInt32(rd[10]),
                PrNo = rd[11]?.ToString(),
                NoIssue = rd.IsDBNull(12) ? null : Convert.ToInt32(rd[12])
            },
            cmd => AddNumeric18_0Param(cmd, "@RequestNo", requestNo),
            cancellationToken);
    }

    public async Task<IReadOnlyList<MaterialRequestLineDto>> GetLinesAsync(long requestNo, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT
                d.ID,
                d.ITEMCODE,
                ISNULL(i.ItemName, '') AS ITEMNAME,
                d.UNIT,
                d.NEW_ORDER,
                d.NOT_RECEIPT,
                d.INSTOCK,
                d.acctualyInventory,
                d.BUY,
                d.PRICE,
                d.NOTE,
                ISNULL(d.NEW_ITEM, 0) AS NEW_ITEM,
                ISNULL(d.SELECTED, 0) AS SELECTED
            FROM dbo.MATERIAL_REQUEST_DETAIL d
            LEFT JOIN dbo.INV_ItemList i ON i.ItemCode = d.ITEMCODE
            WHERE REQUEST_NO = @RequestNo
            ORDER BY d.ID";

        var rows = await Helper.QueryAsync(
            _connectionString,
            sql,
            rd => new MaterialRequestLineDto
            {
                Id = rd.IsDBNull(0) ? null : Convert.ToInt32(rd[0]),
                ItemCode = rd[1]?.ToString(),
                ItemName = rd[2]?.ToString(),
                Unit = rd[3]?.ToString(),
                OrderQty = rd.IsDBNull(4) ? null : Convert.ToDecimal(rd[4]),
                NotReceipt = rd.IsDBNull(5) ? null : Convert.ToDecimal(rd[5]),
                InStock = rd.IsDBNull(6) ? null : Convert.ToDecimal(rd[6]),
                AccIn = rd.IsDBNull(7) ? null : Convert.ToDecimal(rd[7]),
                Buy = rd.IsDBNull(8) ? null : Convert.ToDecimal(rd[8]),
                Price = rd.IsDBNull(9) ? null : Convert.ToDecimal(rd[9]),
                Note = rd[10]?.ToString(),
                NewItem = !rd.IsDBNull(11) && Convert.ToBoolean(rd[11]),
                Selected = !rd.IsDBNull(12) && Convert.ToBoolean(rd[12])
            },
            cmd => AddNumeric18_0Param(cmd, "@RequestNo", requestNo),
            cancellationToken);

        return rows;
    }

    public async Task<long> SaveAsync(long? requestNo, MaterialRequestDetailDto header, IReadOnlyList<MaterialRequestLineDto> lines, CancellationToken cancellationToken = default)
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);
        await using var tx = await conn.BeginTransactionAsync(cancellationToken);

        try
        {
            var resolvedRequestNo = requestNo ?? await GetNextRequestNoAsync(conn, (SqlTransaction)tx, cancellationToken);
            var statusId = header.MaterialStatusId ?? await GetDefaultStatusIdAsync(conn, (SqlTransaction)tx, cancellationToken);

            if (requestNo.HasValue)
            {
                const string updateSql = @"
                    UPDATE dbo.MATERIAL_REQUEST
                    SET
                        STORE_GROUP = @StoreGroup,
                        DATE_CREATE = @DateCreate,
                        ACCORDINGTO = @AccordingTo,
                        APPROVAL = @Approval,
                        POST_PR = @PostPr,
                        IS_AUTO = @IsAuto,
                        FROM_DATE = @FromDate,
                        TO_DATE = @ToDate,
                        APPROVAL_END = @ApprovalEnd,
                        MATERIALSTATUSID = @StatusId,
                        PRNO = @PrNo,
                        NO_ISSUE = @NoIssue
                    WHERE REQUEST_NO = @RequestNo";

                await using var updateCmd = new SqlCommand(updateSql, conn, (SqlTransaction)tx);
                BindHeaderParams(updateCmd, resolvedRequestNo, header, statusId);
                var updated = await updateCmd.ExecuteNonQueryAsync(cancellationToken);
                if (updated == 0)
                {
                    throw new InvalidOperationException("Material Request not found.");
                }
            }
            else
            {
                const string insertSql = @"
                    INSERT INTO dbo.MATERIAL_REQUEST
                    (
                        REQUEST_NO, STORE_GROUP, DATE_CREATE, ACCORDINGTO, APPROVAL, POST_PR, IS_AUTO,
                        FROM_DATE, TO_DATE, APPROVAL_END, MATERIALSTATUSID, PRNO, NO_ISSUE
                    )
                    VALUES
                    (
                        @RequestNo, @StoreGroup, @DateCreate, @AccordingTo, @Approval, @PostPr, @IsAuto,
                        @FromDate, @ToDate, @ApprovalEnd, @StatusId, @PrNo, @NoIssue
                    )";

                await using var insertCmd = new SqlCommand(insertSql, conn, (SqlTransaction)tx);
                BindHeaderParams(insertCmd, resolvedRequestNo, header, statusId);
                await insertCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            const string deleteLineSql = "DELETE FROM dbo.MATERIAL_REQUEST_DETAIL WHERE REQUEST_NO = @RequestNo";
            await using (var deleteCmd = new SqlCommand(deleteLineSql, conn, (SqlTransaction)tx))
            {
                AddNumeric18_0Param(deleteCmd, "@RequestNo", resolvedRequestNo);
                await deleteCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            if (lines.Count > 0)
            {
                const string insertLineSql = @"
                    INSERT INTO dbo.MATERIAL_REQUEST_DETAIL
                    (
                        REQUEST_NO, ITEMCODE, UNIT, BEGIN_Q, RECEIPT_Q, USING_Q, END_Q,
                        NORM_Q, NOT_RECEIPT, NEW_ORDER, INSTOCK, acctualyInventory, BUY, ISSUED,
                        NEW_ITEM, SELECTED, NORM_Q_MAIN, PRICE, NOTE, PostedPR, ManualCheck, TempStore, CreatedAsset
                    )
                    VALUES
                    (
                        @RequestNo, @ItemCode, @Unit, 0, 0, 0, 0,
                        0, @NotReceipt, @OrderQty, @InStock, @AccIn, @Buy, 0,
                        @NewItem, @Selected, 0, @Price, @Note, 0, 0, 0, 0
                    )";

                foreach (var line in lines)
                {
                    await using var lineCmd = new SqlCommand(insertLineSql, conn, (SqlTransaction)tx);
                    AddNumeric18_0Param(lineCmd, "@RequestNo", resolvedRequestNo);
                    Helper.AddParameter(lineCmd, "@ItemCode", (line.ItemCode ?? string.Empty).Trim(), SqlDbType.VarChar, 20);
                    Helper.AddParameter(lineCmd, "@Unit", (line.Unit ?? string.Empty).Trim(), SqlDbType.VarChar, 50);
                    AddDecimal18_2Param(lineCmd, "@OrderQty", line.OrderQty ?? 0m);
                    AddDecimal18_2Param(lineCmd, "@NotReceipt", line.NotReceipt ?? 0m);
                    AddDecimal18_2Param(lineCmd, "@InStock", line.InStock ?? 0m);
                    AddDecimal18_2Param(lineCmd, "@AccIn", line.AccIn ?? 0m);
                    AddDecimal18_2Param(lineCmd, "@Buy", line.Buy ?? 0m);
                    AddDecimal18_2Param(lineCmd, "@Price", line.Price ?? 0m);
                    Helper.AddParameter(lineCmd, "@Note", (line.Note ?? string.Empty).Trim(), SqlDbType.VarChar, 255);
                    Helper.AddParameter(lineCmd, "@NewItem", line.NewItem, SqlDbType.Bit);
                    Helper.AddParameter(lineCmd, "@Selected", line.Selected, SqlDbType.Bit);

                    await lineCmd.ExecuteNonQueryAsync(cancellationToken);
                }
            }

            await tx.CommitAsync(cancellationToken);
            return resolvedRequestNo;
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<IReadOnlyList<MaterialRequestItemLookupDto>> SearchItemsAsync(
        string? keyword,
        bool checkBalanceInStore = false,
        int? storeGroup = null,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT TOP 100
                i.ItemCode,
                i.ItemName,
                ISNULL(i.Unit, '') AS Unit,
                ISNULL(MAX(b.IsQ), 0) AS InStock,
                i.KPGroupItem
            FROM dbo.INV_ItemList i
            LEFT JOIN dbo.INV_ItemBalanceACC_TMP b ON b.ItemCode = i.ItemCode
            WHERE
                (@Keyword IS NULL
                    OR i.ItemCode LIKE '%' + @Keyword + '%'
                    OR i.ItemName LIKE '%' + @Keyword + '%')
                AND (i.IsActive = 1 OR i.IsActive IS NULL)
                AND (@CheckBalanceInStore = 0 OR ISNULL(b.IsQ, 0) > 0)
                AND (@StoreGroup IS NULL OR @StoreGroup = 0 OR i.KPGroupItem = @StoreGroup)
            GROUP BY i.ItemCode, i.ItemName, i.Unit, i.KPGroupItem
            ORDER BY i.ItemCode";

        var rows = await Helper.QueryAsync(
            _connectionString,
            sql,
            rd => new MaterialRequestItemLookupDto
            {
                ItemCode = rd[0]?.ToString() ?? string.Empty,
                ItemName = rd[1]?.ToString() ?? string.Empty,
                Unit = rd[2]?.ToString() ?? string.Empty,
                InStock = rd.IsDBNull(3) ? 0 : Convert.ToDecimal(rd[3]),
                StoreGroupId = rd.IsDBNull(4) ? null : Convert.ToInt32(rd[4])
            },
            cmd =>
            {
                Helper.AddParameter(cmd, "@Keyword", string.IsNullOrWhiteSpace(keyword) ? null : keyword.Trim(), SqlDbType.VarChar, 150);
                Helper.AddParameter(cmd, "@CheckBalanceInStore", checkBalanceInStore, SqlDbType.Bit);
                Helper.AddParameter(cmd, "@StoreGroup", storeGroup, SqlDbType.Int);
            },
            cancellationToken);

        return rows;
    }

    public async Task<MaterialRequestItemLookupDto> CreateQuickItemAsync(string itemName, string? unit, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(itemName))
        {
            throw new InvalidOperationException("Item Name is required.");
        }

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);
        await using var tx = await conn.BeginTransactionAsync(cancellationToken);

        try
        {
            var itemCode = await GenerateQuickItemCodeAsync(conn, (SqlTransaction)tx, cancellationToken);

            const string sql = @"
                INSERT INTO dbo.INV_ItemList
                (
                    ItemCode, ItemName, ItemCatg, Unit, IsMaterial, IsPurchase, IsActive,
                    IsNewItem, CreatedDate, ItemNameNew
                )
                VALUES
                (
                    @ItemCode, @ItemName, 0, @Unit, 1, 1, 1,
                    1, CONVERT(nvarchar(100), GETDATE(), 120), @ItemName
                )";

            await using var cmd = new SqlCommand(sql, conn, (SqlTransaction)tx);
            Helper.AddParameter(cmd, "@ItemCode", itemCode, SqlDbType.VarChar, 20);
            Helper.AddParameter(cmd, "@ItemName", itemName.Trim(), SqlDbType.VarChar, 150);
            Helper.AddParameter(cmd, "@Unit", string.IsNullOrWhiteSpace(unit) ? null : unit.Trim(), SqlDbType.VarChar, 10);
            await cmd.ExecuteNonQueryAsync(cancellationToken);

            await tx.CommitAsync(cancellationToken);

            return new MaterialRequestItemLookupDto
            {
                ItemCode = itemCode,
                ItemName = itemName.Trim(),
                Unit = string.IsNullOrWhiteSpace(unit) ? string.Empty : unit.Trim()
            };
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static void BindSearchParams(SqlCommand cmd, MaterialRequestFilterCriteria criteria, IReadOnlyList<int> statusIds)
    {
        AddNumeric18_0Param(cmd, "@RequestNo", criteria.RequestNo);
        AddNumeric18_0Param(cmd, "@StoreGroup", criteria.StoreGroup);
        Helper.AddParameter(cmd, "@NoIssue", criteria.NoIssue, SqlDbType.Int);
        Helper.AddParameter(cmd, "@IsAuto", criteria.IsAuto, SqlDbType.Bit);
        Helper.AddParameter(cmd, "@BuyGreaterThanZero", criteria.BuyGreaterThanZero, SqlDbType.Bit);
        Helper.AddParameter(cmd, "@FromDate", criteria.FromDate, SqlDbType.DateTime);
        Helper.AddParameter(cmd, "@ToDate", criteria.ToDate, SqlDbType.DateTime);
        Helper.AddParameter(cmd, "@AccordingTo", string.IsNullOrWhiteSpace(criteria.AccordingToKeyword) ? null : criteria.AccordingToKeyword.Trim(), SqlDbType.VarChar, 300);
        Helper.AddParameter(cmd, "@ItemCode", string.IsNullOrWhiteSpace(criteria.ItemCode) ? null : criteria.ItemCode.Trim(), SqlDbType.VarChar, 20);

        for (var i = 0; i < statusIds.Count; i++)
        {
            Helper.AddParameter(cmd, $"@StatusId{i}", statusIds[i], SqlDbType.Int);
        }
    }

    private static void BindHeaderParams(SqlCommand cmd, long requestNo, MaterialRequestDetailDto header, int statusId)
    {
        AddNumeric18_0Param(cmd, "@RequestNo", requestNo);
        AddNumeric18_0Param(cmd, "@StoreGroup", header.StoreGroup);
        Helper.AddParameter(cmd, "@DateCreate", header.DateCreate, SqlDbType.DateTime);
        Helper.AddParameter(cmd, "@AccordingTo", (header.AccordingTo ?? string.Empty).Trim(), SqlDbType.VarChar, 300);
        Helper.AddParameter(cmd, "@Approval", header.Approval, SqlDbType.Bit);
        Helper.AddParameter(cmd, "@PostPr", header.PostPr, SqlDbType.Bit);
        Helper.AddParameter(cmd, "@IsAuto", header.IsAuto, SqlDbType.Bit);
        Helper.AddParameter(cmd, "@FromDate", header.FromDate, SqlDbType.DateTime);
        Helper.AddParameter(cmd, "@ToDate", header.ToDate, SqlDbType.DateTime);
        Helper.AddParameter(cmd, "@ApprovalEnd", header.ApprovalEnd, SqlDbType.Bit);
        Helper.AddParameter(cmd, "@StatusId", statusId, SqlDbType.Int);
        Helper.AddParameter(cmd, "@PrNo", string.IsNullOrWhiteSpace(header.PrNo) ? null : header.PrNo.Trim(), SqlDbType.VarChar, 50);
        Helper.AddParameter(cmd, "@NoIssue", header.NoIssue, SqlDbType.Int);
    }

    private static string BuildStatusFilterSql(IReadOnlyList<int> statusIds)
    {
        if (statusIds.Count == 0)
        {
            return "1 = 1";
        }

        var parameters = Enumerable.Range(0, statusIds.Count)
            .Select(i => $"@StatusId{i}");
        return $"r.MATERIALSTATUSID IN ({string.Join(", ", parameters)})";
    }

    private static async Task<long> GetNextRequestNoAsync(SqlConnection conn, SqlTransaction tx, CancellationToken cancellationToken)
    {
        const string sql = "SELECT ISNULL(MAX(REQUEST_NO), 0) + 1 FROM dbo.MATERIAL_REQUEST WITH (UPDLOCK, HOLDLOCK)";
        await using var cmd = new SqlCommand(sql, conn, tx);
        var value = await cmd.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(value);
    }

    private static async Task<int> GetDefaultStatusIdAsync(SqlConnection conn, SqlTransaction tx, CancellationToken cancellationToken)
    {
        const string sql = "SELECT ISNULL(MIN(MaterialStatusID), 0) FROM dbo.MaterialStatus";
        await using var cmd = new SqlCommand(sql, conn, tx);
        var value = await cmd.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(value);
    }

    private static async Task<string> GenerateQuickItemCodeAsync(SqlConnection conn, SqlTransaction tx, CancellationToken cancellationToken)
    {
        for (var i = 0; i < 50; i++)
        {
            var baseCode = $"NI{DateTime.UtcNow:yyMMddHHmmss}";
            var suffix = i == 0 ? string.Empty : i.ToString("00");
            var code = $"{baseCode}{suffix}";
            if (code.Length > 20)
            {
                code = code.Substring(0, 20);
            }

            const string sql = "SELECT COUNT(1) FROM dbo.INV_ItemList WITH (UPDLOCK, HOLDLOCK) WHERE ItemCode = @ItemCode";
            await using var cmd = new SqlCommand(sql, conn, tx);
            Helper.AddParameter(cmd, "@ItemCode", code, SqlDbType.VarChar, 20);
            var count = Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));
            if (count == 0)
            {
                return code;
            }
        }

        throw new InvalidOperationException("Cannot generate Item Code. Please retry.");
    }

    /// <summary>
    /// Thêm parameter numeric(18,0) đúng precision/scale để tránh lỗi ép kiểu khi chạy ADO.NET.
    /// </summary>
    private static void AddNumeric18_0Param(SqlCommand cmd, string name, object? value)
    {
        var p = cmd.Parameters.Add(name, SqlDbType.Decimal);
        p.Precision = 18;
        p.Scale = 0;
        p.Value = value is null ? DBNull.Value : Convert.ToDecimal(value);
    }

    /// <summary>
    /// Dùng cho các cột decimal có scale 2 ở dòng chi tiết MR.
    /// </summary>
    private static void AddDecimal18_2Param(SqlCommand cmd, string name, object? value)
    {
        var p = cmd.Parameters.Add(name, SqlDbType.Decimal);
        p.Precision = 18;
        p.Scale = 2;
        p.Value = value is null ? DBNull.Value : Convert.ToDecimal(value);
    }
}
