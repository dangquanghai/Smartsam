using System.Data;
using Microsoft.Data.SqlClient;
using SmartSam.Helpers;

namespace SmartSam.Pages.Purchasing.ApproveSupplier;

public class ApproveSupplierService
{
    private readonly IConfiguration _configuration;
    private readonly string _connectionString;

    public ApproveSupplierService(IConfiguration configuration)
    {
        _configuration = configuration;
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Missing connection string: DefaultConnection");
    }

    public Task<IReadOnlyList<ApproveLookupOptionDto>> GetDepartmentsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var data = Helper.LoadLookup(
            _configuration,
            "MS_Department",
            "DeptID",
            "DeptCode",
            keyword: null,
            top: 1000);

        var result = data.Select(x => new ApproveLookupOptionDto
        {
            Id = Convert.ToInt32(x.Id),
            CodeOrName = x.Text
        }).ToList();

        return Task.FromResult<IReadOnlyList<ApproveLookupOptionDto>>(result);
    }

    public async Task<ApproveEmployeeDataScopeDto> GetEmployeeDataScopeAsync(string? employeeCode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(employeeCode))
        {
            return new ApproveEmployeeDataScopeDto();
        }

        const string sql = @"
                            SELECT TOP 1 DeptID, ISNULL(CanAsk, 0) AS CanAsk, LevelCheckSupplier
                            FROM dbo.MS_Employee
                            WHERE EmployeeCode = @EmployeeCode";

        return await Helper.QuerySingleOrDefaultAsync(
            _connectionString,
            sql,
            rd => new ApproveEmployeeDataScopeDto
            {
                DeptID = rd.IsDBNull(0) ? null : Convert.ToInt32(rd[0]),
                CanAsk = !rd.IsDBNull(1) && Convert.ToBoolean(rd[1]),
                LevelCheckSupplier = rd.IsDBNull(2) ? null : Convert.ToInt32(rd[2])
            },
            cmd => Helper.AddParameter(cmd, "@EmployeeCode", employeeCode.Trim(), SqlDbType.NVarChar, 50),
            cancellationToken) ?? new ApproveEmployeeDataScopeDto();
    }

    public async Task UpdateSupplierCommentAsync(int supplierId, string? comment, CancellationToken cancellationToken = default)
    {
        const string sql = @"
                    UPDATE dbo.PC_Suppliers
                    SET Comment = @Comment
                    WHERE SupplierID = @SupplierID";

        await Helper.ExecuteNonQueryAsync(
            _connectionString,
            sql,
            cmd =>
            {
                Helper.AddParameter(cmd, "@SupplierID", supplierId, SqlDbType.Int);
                Helper.AddParameter(cmd, "@Comment", NullIfEmpty(comment), SqlDbType.NVarChar, 2000);
            },
            cancellationToken);
    }

    public async Task<bool> SupplierCodeExistsAsync(string supplierCode, int? excludeSupplierId = null, CancellationToken cancellationToken = default)
    {
        const string sql = @"
                            SELECT COUNT(1)
                            FROM dbo.PC_Suppliers
                            WHERE LTRIM(RTRIM(ISNULL(SupplierCode, ''))) = LTRIM(RTRIM(@SupplierCode))
                            AND (@ExcludeSupplierId IS NULL OR SupplierID <> @ExcludeSupplierId);";

        var result = await Helper.ExecuteScalarAsync(
            _connectionString,
            sql,
            cmd =>
            {
                Helper.AddParameter(cmd, "@SupplierCode", supplierCode.Trim(), SqlDbType.NVarChar, 255);
                Helper.AddParameter(cmd, "@ExcludeSupplierId", excludeSupplierId, SqlDbType.Int);
            },
            cancellationToken);

        return Convert.ToInt32(result) > 0;
    }

    public async Task<int?> GetSupplierDepartmentAsync(int supplierId, CancellationToken cancellationToken = default)
    {
        const string sql = @"
                    SELECT TOP 1 DeptID
                    FROM dbo.PC_Suppliers
                    WHERE SupplierID = @SupplierID";

        return await Helper.QuerySingleOrDefaultAsync<int?>(
            _connectionString,
            sql,
            rd => rd.IsDBNull(0) ? null : Convert.ToInt32(rd[0]),
            cmd => { Helper.AddParameter(cmd, "@SupplierID", supplierId, SqlDbType.Int); },
            cancellationToken);
    }

    public async Task<ApproveSearchResultDto> SearchPagedAsync(ApproveFilterCriteria criteria, CancellationToken cancellationToken = default)
    {
        var pageIndex = criteria.PageIndex.GetValueOrDefault() <= 0 ? 1 : criteria.PageIndex!.Value;
        var pageSize = criteria.PageSize.GetValueOrDefault() <= 0 ? 25 : criteria.PageSize!.Value;
        var applyPaging = criteria.PageIndex.HasValue && criteria.PageSize.HasValue;

        var fromWhereSql = @"
FROM dbo.PC_Suppliers s
LEFT JOIN dbo.PC_SupplierStatus st ON s.[Status] = st.SupplierStatusID
LEFT JOIN dbo.MS_Department d ON s.DeptID = d.DeptID
WHERE
    (@DeptID IS NULL OR s.DeptID = @DeptID)
    AND (@StatusID IS NULL OR s.[Status] = @StatusID)
    AND (@MaxStatusExclusive IS NULL OR ISNULL(s.[Status], 0) < @MaxStatusExclusive)";

        var countSql = "SELECT COUNT(1) " + fromWhereSql;
        var totalObj = await Helper.ExecuteScalarAsync(
            _connectionString,
            countSql,
            cmd => BindSearchParams(cmd, criteria),
            cancellationToken);
        var totalCount = Convert.ToInt32(totalObj);

        var selectSql = $@"
SELECT
    s.SupplierID,
    s.SupplierCode,
    s.SupplierName,
    s.Address,
    s.Phone,
    s.Mobile,
    s.Fax,
    s.Contact,
    s.[Position],
    s.Business,
    s.DeptID,
    d.DeptCode,
    st.SupplierStatusName,
    s.[Status]
{fromWhereSql}
ORDER BY s.SupplierID DESC";

        if (applyPaging)
        {
            selectSql += "\nOFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";
        }

        var rows = await Helper.QueryAsync(
            _connectionString,
            selectSql,
            rd => new ApproveListRowDto
            {
                SupplierID = rd.GetInt32(0),
                SupplierCode = rd[1]?.ToString() ?? string.Empty,
                SupplierName = rd[2]?.ToString() ?? string.Empty,
                Address = rd[3]?.ToString() ?? string.Empty,
                Phone = rd[4]?.ToString() ?? string.Empty,
                Mobile = rd[5]?.ToString() ?? string.Empty,
                Fax = rd[6]?.ToString() ?? string.Empty,
                Contact = rd[7]?.ToString() ?? string.Empty,
                Position = rd[8]?.ToString() ?? string.Empty,
                Business = rd[9]?.ToString() ?? string.Empty,
                DeptID = rd.IsDBNull(10) ? null : rd.GetInt32(10),
                DeptCode = rd[11]?.ToString() ?? string.Empty,
                SupplierStatusName = rd[12]?.ToString() ?? string.Empty,
                Status = rd.IsDBNull(13) ? null : Convert.ToInt32(rd[13])
            },
            cmd =>
            {
                BindSearchParams(cmd, criteria);
                if (applyPaging)
                {
                    Helper.AddParameter(cmd, "@Offset", (pageIndex - 1) * pageSize, SqlDbType.Int);
                    Helper.AddParameter(cmd, "@PageSize", pageSize, SqlDbType.Int);
                }
            },
            cancellationToken);

        return new ApproveSearchResultDto
        {
            Rows = rows.ToList(),
            TotalCount = totalCount
        };
    }

    public async Task<ApproveSupplierDetailDto?> GetSupplierDetailAsync(int supplierId, CancellationToken cancellationToken = default)
    {
        const string sql = @"
SELECT
    s.SupplierID,
    s.SupplierCode,
    s.SupplierName,
    s.Address,
    s.Phone,
    s.Mobile,
    s.Fax,
    s.Contact,
    s.[Position],
    s.Business,
    s.ApprovedDate,
    s.[Document],
    s.Certificate,
    s.Service,
    s.Comment,
    s.IsNew,
    s.CodeOfAcc,
    s.DeptID,
    d.DeptCode,
    s.[Status],
    st.SupplierStatusName
FROM dbo.PC_Suppliers s
LEFT JOIN dbo.PC_SupplierStatus st ON s.[Status] = st.SupplierStatusID
LEFT JOIN dbo.MS_Department d ON s.DeptID = d.DeptID
WHERE s.SupplierID = @SupplierID";

        return await Helper.QuerySingleOrDefaultAsync(
            _connectionString,
            sql,
            rd => new ApproveSupplierDetailDto
            {
                SupplierID = rd.GetInt32(0),
                SupplierCode = rd[1]?.ToString() ?? string.Empty,
                SupplierName = rd[2]?.ToString() ?? string.Empty,
                Address = rd[3]?.ToString() ?? string.Empty,
                Phone = rd[4]?.ToString() ?? string.Empty,
                Mobile = rd[5]?.ToString() ?? string.Empty,
                Fax = rd[6]?.ToString() ?? string.Empty,
                Contact = rd[7]?.ToString() ?? string.Empty,
                Position = rd[8]?.ToString() ?? string.Empty,
                Business = rd[9]?.ToString() ?? string.Empty,
                ApprovedDate = rd.IsDBNull(10) ? null : rd.GetDateTime(10),
                Document = !rd.IsDBNull(11) && Convert.ToBoolean(rd[11]),
                Certificate = rd[12]?.ToString() ?? string.Empty,
                Service = rd[13]?.ToString() ?? string.Empty,
                Comment = rd[14]?.ToString() ?? string.Empty,
                IsNew = !rd.IsDBNull(15) && Convert.ToBoolean(rd[15]),
                CodeOfAcc = rd[16]?.ToString() ?? string.Empty,
                DeptID = rd.IsDBNull(17) ? null : Convert.ToInt32(rd[17]),
                DeptCode = rd[18]?.ToString() ?? string.Empty,
                Status = rd.IsDBNull(19) ? null : Convert.ToInt32(rd[19]),
                SupplierStatusName = rd[20]?.ToString() ?? string.Empty
            },
            cmd => { Helper.AddParameter(cmd, "@SupplierID", supplierId, SqlDbType.Int); },
            cancellationToken);
    }

    public async Task<ApprovePurchaseOrderInfoDto> GetPurchaseOrderInfoAsync(int supplierId, int currentYear, CancellationToken cancellationToken = default)
    {
        var targetYear = currentYear - 1;

        const string sqlCountPo = @"
        SELECT COUNT(1)
        FROM dbo.PC_PO
        WHERE SupplierID = @SupplierID
        AND YEAR(PODate) = @TheYear;";

            const string sqlTotalCost = @"
        SELECT
        ISNULL(SUM(
            CASE
                WHEN p.Currency = 1 THEN d.POAmount
                ELSE d.POAmount * p.ExRate
            END
        ), 0) AS POAmountVND
        FROM dbo.PC_PO p
        INNER JOIN dbo.PC_PODetail d ON p.POID = d.POID
        WHERE YEAR(p.PODate) = @TheYear
        AND p.SupplierID = @SupplierID;";

            const string sqlRows = @"
        SELECT
        e.PO_IndexDetailID,
        i.PO_IndexName AS IndexName,
        id.PO_IndexDetailName AS SubIndex,
        COUNT(p.POID) AS TheTime,
        e.Point
        FROM dbo.PC_PO p
        INNER JOIN dbo.PO_Estimate e ON p.POID = e.POID
        INNER JOIN dbo.PO_IndexDetail id ON e.PO_IndexDetailID = id.PO_IndexDetailID
        INNER JOIN dbo.PO_Index i ON id.PO_Index = i.PO_Index
        WHERE YEAR(e.TheDate) = @TheYear
        AND p.SupplierID = @SupplierID
        GROUP BY
        e.PO_IndexDetailID,
        i.PO_IndexName,
        id.PO_IndexDetailName,
        e.Point
        ORDER BY
        e.PO_IndexDetailID;";

        var countObj = await Helper.ExecuteScalarAsync(
            _connectionString,
            sqlCountPo,
            cmd =>
            {
                Helper.AddParameter(cmd, "@SupplierID", supplierId, SqlDbType.Int);
                Helper.AddParameter(cmd, "@TheYear", targetYear, SqlDbType.Int);
            },
            cancellationToken);

        var totalCostObj = await Helper.ExecuteScalarAsync(
            _connectionString,
            sqlTotalCost,
            cmd =>
            {
                Helper.AddParameter(cmd, "@SupplierID", supplierId, SqlDbType.Int);
                Helper.AddParameter(cmd, "@TheYear", targetYear, SqlDbType.Int);
            },
            cancellationToken);

        var rows = await Helper.QueryAsync(
            _connectionString,
            sqlRows,
            rd => new ApprovePurchaseOrderRowDto
            {
                PO_IndexDetailID = rd.IsDBNull(0) ? 0 : Convert.ToInt32(rd[0]),
                IndexName = rd[1]?.ToString() ?? string.Empty,
                SubIndex = rd[2]?.ToString() ?? string.Empty,
                TheTime = rd.IsDBNull(3) ? 0 : Convert.ToInt32(rd[3]),
                Point = rd.IsDBNull(4) ? 0m : Convert.ToDecimal(rd[4])
            },
            cmd =>
            {
                Helper.AddParameter(cmd, "@SupplierID", supplierId, SqlDbType.Int);
                Helper.AddParameter(cmd, "@TheYear", targetYear, SqlDbType.Int);
            },
            cancellationToken);

        return new ApprovePurchaseOrderInfoDto
        {
            Year = targetYear,
            CountPO = countObj is null || countObj == DBNull.Value ? 0 : Convert.ToInt32(countObj),
            TotalCost = totalCostObj is null || totalCostObj == DBNull.Value ? 0m : Convert.ToDecimal(totalCostObj),
            Rows = rows.ToList()
        };
    }

    public async Task<IReadOnlyList<ApproveSupplierServiceRowDto>> GetSupplierServiceRowsAsync(int supplierId, CancellationToken cancellationToken = default)
    {
        const string sql = @"
        SELECT
        TheDate,
        Comment,
        ThePoint,
        WarrantyOrService,
        UserCode
        FROM dbo.PC_SupplierService_TMP
        WHERE SupplierID = @SupplierID
        ORDER BY TheDate DESC;";

        var rows = await Helper.QueryAsync(
            _connectionString,
            sql,
            rd => new ApproveSupplierServiceRowDto
            {
                TheDate = rd.IsDBNull(0) ? null : Convert.ToDateTime(rd[0]),
                Comment = rd[1]?.ToString() ?? string.Empty,
                ThePoint = rd.IsDBNull(2) ? null : Convert.ToInt32(rd[2]),
                WarrantyOrService = rd.IsDBNull(3) ? null : Convert.ToInt32(rd[3]),
                UserCode = rd[4]?.ToString() ?? string.Empty
            },
            cmd => { Helper.AddParameter(cmd, "@SupplierID", supplierId, SqlDbType.Int); },
            cancellationToken);

        return rows;
    }

    public async Task ApproveSupplierAsync(int supplierId, string operatorCode, CancellationToken cancellationToken = default)
    {
        const string sql = @"
                    UPDATE dbo.PC_Suppliers
                    SET [Status] = 2,
                        IsApproved = 1,
                        ApprovedDate = GETDATE(),
                        BODCode = @OperatorCode,
                        BODApproveDate = GETDATE()
                    WHERE SupplierID = @SupplierID";

        await Helper.ExecuteNonQueryAsync(
            _connectionString,
            sql,
            cmd =>
            {
                Helper.AddParameter(cmd, "@SupplierID", supplierId, SqlDbType.Int);
                Helper.AddParameter(cmd, "@OperatorCode", operatorCode, SqlDbType.NVarChar, 50);
            },
            cancellationToken);
    }

    public async Task<bool> ApproveByLevelAsync(
        int supplierId,
        int levelCheckSupplier,
        string operatorCode,
        ApproveSupplierDetailDto input,
        bool canEditAllFields,
        bool canEditComment,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
                    UPDATE dbo.PC_Suppliers
                    SET
                        SupplierCode = CASE WHEN @CanEditAll = 1 THEN @SupplierCode ELSE SupplierCode END,
                        SupplierName = CASE WHEN @CanEditAll = 1 THEN @SupplierName ELSE SupplierName END,
                        Address = CASE WHEN @CanEditAll = 1 THEN @Address ELSE Address END,
                        Phone = CASE WHEN @CanEditAll = 1 THEN @Phone ELSE Phone END,
                        Mobile = CASE WHEN @CanEditAll = 1 THEN @Mobile ELSE Mobile END,
                        Fax = CASE WHEN @CanEditAll = 1 THEN @Fax ELSE Fax END,
                        Contact = CASE WHEN @CanEditAll = 1 THEN @Contact ELSE Contact END,
                        [Position] = CASE WHEN @CanEditAll = 1 THEN @Position ELSE [Position] END,
                        Business = CASE WHEN @CanEditAll = 1 THEN @Business ELSE Business END,
                        [Document] = CASE WHEN @CanEditAll = 1 THEN @Document ELSE [Document] END,
                        Certificate = CASE WHEN @CanEditAll = 1 THEN @Certificate ELSE Certificate END,
                        Service = CASE WHEN @CanEditAll = 1 THEN @Service ELSE Service END,
                        IsNew = CASE WHEN @CanEditAll = 1 THEN @IsNew ELSE IsNew END,
                        Comment = CASE WHEN @CanEditComment = 1 THEN @Comment ELSE Comment END,
                        PurchaserCode = CASE WHEN @LevelCheck = 1 THEN @OperatorCode ELSE PurchaserCode END,
                        PurchaserPreparedDate = CASE WHEN @LevelCheck = 1 THEN GETDATE() ELSE PurchaserPreparedDate END,
                        DepartmentCode = CASE WHEN @LevelCheck = 2 THEN @OperatorCode ELSE DepartmentCode END,
                        DepartmentApproveDate = CASE WHEN @LevelCheck = 2 THEN GETDATE() ELSE DepartmentApproveDate END,
                        FinancialCode = CASE WHEN @LevelCheck = 3 THEN @OperatorCode ELSE FinancialCode END,
                        FinancialApproveDate = CASE WHEN @LevelCheck = 3 THEN GETDATE() ELSE FinancialApproveDate END,
                        BODCode = CASE WHEN @LevelCheck = 4 THEN @OperatorCode ELSE BODCode END,
                        BODApproveDate = CASE WHEN @LevelCheck = 4 THEN GETDATE() ELSE BODApproveDate END,
                        IsApproved = CASE WHEN @LevelCheck = 4 THEN 1 ELSE IsApproved END,
                        ApprovedDate = CASE WHEN @LevelCheck = 4 THEN GETDATE() ELSE ApprovedDate END,
                        [Status] = @LevelCheck
                    WHERE SupplierID = @SupplierID
                        AND ISNULL([Status], 0) + 1 = @LevelCheck";

        var affected = await Helper.ExecuteNonQueryAsync(
            _connectionString,
            sql,
            cmd =>
            {
                Helper.AddParameter(cmd, "@SupplierID", supplierId, SqlDbType.Int);
                Helper.AddParameter(cmd, "@LevelCheck", levelCheckSupplier, SqlDbType.Int);
                Helper.AddParameter(cmd, "@OperatorCode", operatorCode, SqlDbType.NVarChar, 50);
                Helper.AddParameter(cmd, "@CanEditAll", canEditAllFields ? 1 : 0, SqlDbType.Int);
                Helper.AddParameter(cmd, "@CanEditComment", canEditComment ? 1 : 0, SqlDbType.Int);
                Helper.AddParameter(cmd, "@Comment", NullIfEmpty(input.Comment), SqlDbType.NVarChar, 1000);
                Helper.AddParameter(cmd, "@SupplierCode", NullIfEmpty(input.SupplierCode), SqlDbType.NVarChar, 255);
                Helper.AddParameter(cmd, "@SupplierName", NullIfEmpty(input.SupplierName), SqlDbType.NVarChar, 255);
                Helper.AddParameter(cmd, "@Address", NullIfEmpty(input.Address), SqlDbType.NVarChar, 255);
                Helper.AddParameter(cmd, "@Phone", NullIfEmpty(input.Phone), SqlDbType.NVarChar, 50);
                Helper.AddParameter(cmd, "@Mobile", NullIfEmpty(input.Mobile), SqlDbType.NVarChar, 50);
                Helper.AddParameter(cmd, "@Fax", NullIfEmpty(input.Fax), SqlDbType.NVarChar, 50);
                Helper.AddParameter(cmd, "@Contact", NullIfEmpty(input.Contact), SqlDbType.NVarChar, 255);
                Helper.AddParameter(cmd, "@Position", NullIfEmpty(input.Position), SqlDbType.NVarChar, 255);
                Helper.AddParameter(cmd, "@Business", NullIfEmpty(input.Business), SqlDbType.NVarChar, 1000);
                Helper.AddParameter(cmd, "@Document", input.Document, SqlDbType.Bit);
                Helper.AddParameter(cmd, "@Certificate", NullIfEmpty(input.Certificate), SqlDbType.NVarChar, 255);
                Helper.AddParameter(cmd, "@Service", NullIfEmpty(input.Service), SqlDbType.NVarChar, 1000);
                Helper.AddParameter(cmd, "@IsNew", input.IsNew, SqlDbType.Bit);
            },
            cancellationToken);

        return affected > 0;
    }

    public async Task<bool> DisapproveByLevelAsync(
        int supplierId,
        int levelCheckSupplier,
        string operatorCode,
        string? comment,
        bool canEditComment,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
                    UPDATE dbo.PC_Suppliers
                    SET
                        Comment = CASE WHEN @CanEditComment = 1 THEN @Comment ELSE Comment END,
                        DepartmentCode = CASE WHEN @LevelCheck = 2 THEN @OperatorCode ELSE DepartmentCode END,
                        DepartmentApproveDate = CASE WHEN @LevelCheck = 2 THEN GETDATE() ELSE DepartmentApproveDate END,
                        FinancialCode = CASE WHEN @LevelCheck = 3 THEN @OperatorCode ELSE FinancialCode END,
                        FinancialApproveDate = CASE WHEN @LevelCheck = 3 THEN GETDATE() ELSE FinancialApproveDate END,
                        BODCode = CASE WHEN @LevelCheck = 4 THEN @OperatorCode ELSE BODCode END,
                        BODApproveDate = CASE WHEN @LevelCheck = 4 THEN GETDATE() ELSE BODApproveDate END,
                        IsApproved = 0,
                        [Status] = 9
                    WHERE SupplierID = @SupplierID
                        AND @LevelCheck IN (2, 3, 4)";

        var affected = await Helper.ExecuteNonQueryAsync(
            _connectionString,
            sql,
            cmd =>
            {
                Helper.AddParameter(cmd, "@SupplierID", supplierId, SqlDbType.Int);
                Helper.AddParameter(cmd, "@LevelCheck", levelCheckSupplier, SqlDbType.Int);
                Helper.AddParameter(cmd, "@OperatorCode", operatorCode, SqlDbType.NVarChar, 50);
                Helper.AddParameter(cmd, "@CanEditComment", canEditComment ? 1 : 0, SqlDbType.Int);
                Helper.AddParameter(cmd, "@Comment", NullIfEmpty(comment), SqlDbType.NVarChar, 1000);
            },
            cancellationToken);

        return affected > 0;
    }

    public async Task<IReadOnlyList<string>> GetEmailsByLevelCheckAsync(int levelCheckSupplier, CancellationToken cancellationToken = default)
    {
        const string sql = @"
        SELECT DISTINCT LTRIM(RTRIM(TheEmail))
        FROM dbo.MS_Employee
        WHERE LevelCheckSupplier = @LevelCheckSupplier
        AND ISNULL(LTRIM(RTRIM(TheEmail)), '') <> ''
        AND ISNULL(IsActive, 0) = 1;";

        var rows = await Helper.QueryAsync(
            _connectionString,
            sql,
            rd => rd[0]?.ToString() ?? string.Empty,
            cmd => { Helper.AddParameter(cmd, "@LevelCheckSupplier", levelCheckSupplier, SqlDbType.Int); },
            cancellationToken);

        return rows
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task UpdateSupplierForApprovalAsync(int supplierId, ApproveSupplierDetailDto input, bool canEditAllFields, CancellationToken cancellationToken = default)
    {
        const string sqlEditAll = @"
                    UPDATE dbo.PC_Suppliers
                    SET SupplierCode = @SupplierCode,
                        SupplierName = @SupplierName,
                        Address = @Address,
                        Phone = @Phone,
                        Mobile = @Mobile,
                        Fax = @Fax,
                        Contact = @Contact,
                        [Position] = @Position,
                        Business = @Business,
                        [Document] = @Document,
                        Certificate = @Certificate,
                        Service = @Service,
                        Comment = @Comment,
                        IsNew = @IsNew
                    WHERE SupplierID = @SupplierID";

        const string sqlCommentOnly = @"
                    UPDATE dbo.PC_Suppliers
                    SET Comment = @Comment
                    WHERE SupplierID = @SupplierID";

        await Helper.ExecuteNonQueryAsync(
            _connectionString,
            canEditAllFields ? sqlEditAll : sqlCommentOnly,
            cmd =>
            {
                Helper.AddParameter(cmd, "@SupplierID", supplierId, SqlDbType.Int);
                Helper.AddParameter(cmd, "@Comment", NullIfEmpty(input.Comment), SqlDbType.NVarChar, 1000);

                if (!canEditAllFields)
                {
                    return;
                }

                Helper.AddParameter(cmd, "@SupplierCode", NullIfEmpty(input.SupplierCode), SqlDbType.NVarChar, 255);
                Helper.AddParameter(cmd, "@SupplierName", NullIfEmpty(input.SupplierName), SqlDbType.NVarChar, 255);
                Helper.AddParameter(cmd, "@Address", NullIfEmpty(input.Address), SqlDbType.NVarChar, 255);
                Helper.AddParameter(cmd, "@Phone", NullIfEmpty(input.Phone), SqlDbType.NVarChar, 50);
                Helper.AddParameter(cmd, "@Mobile", NullIfEmpty(input.Mobile), SqlDbType.NVarChar, 50);
                Helper.AddParameter(cmd, "@Fax", NullIfEmpty(input.Fax), SqlDbType.NVarChar, 50);
                Helper.AddParameter(cmd, "@Contact", NullIfEmpty(input.Contact), SqlDbType.NVarChar, 255);
                Helper.AddParameter(cmd, "@Position", NullIfEmpty(input.Position), SqlDbType.NVarChar, 255);
                Helper.AddParameter(cmd, "@Business", NullIfEmpty(input.Business), SqlDbType.NVarChar, 1000);
                Helper.AddParameter(cmd, "@Document", input.Document, SqlDbType.Bit);
                Helper.AddParameter(cmd, "@Certificate", NullIfEmpty(input.Certificate), SqlDbType.NVarChar, 255);
                Helper.AddParameter(cmd, "@Service", NullIfEmpty(input.Service), SqlDbType.NVarChar, 1000);
                Helper.AddParameter(cmd, "@IsNew", input.IsNew, SqlDbType.Bit);
            },
            cancellationToken);
    }

    private static void BindSearchParams(SqlCommand cmd, ApproveFilterCriteria criteria)
    {
        Helper.AddParameter(cmd, "@DeptID", criteria.DeptId, SqlDbType.Int);
        Helper.AddParameter(cmd, "@StatusID", criteria.StatusId, SqlDbType.Int);
        Helper.AddParameter(cmd, "@MaxStatusExclusive", criteria.MaxStatusExclusive, SqlDbType.Int);
    }

    private static string? NullIfEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
