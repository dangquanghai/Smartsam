using System.Data;
using Microsoft.Data.SqlClient;
using SmartSam.Helpers;

namespace SmartSam.Pages.Purchasing.Supplier;

public class SupplierService
{
    private const string AnnualYearColumn = "ForYear";
    private readonly IConfiguration _configuration;
    private readonly string _connectionString;

    public SupplierService(IConfiguration configuration)
    {
        _configuration = configuration;
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Missing connection string: DefaultConnection");
    }

    public Task<IReadOnlyList<SupplierLookupOptionDto>> GetDepartmentsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var data = Helper.LoadLookup(
            _configuration,
            "MS_Department",
            "DeptID",
            "DeptCode",
            keyword: null,
            top: 1000);

        var result = data.Select(x => new SupplierLookupOptionDto
        {
            Id = Convert.ToInt32(x.Id),
            CodeOrName = x.Text
        }).ToList();

        return Task.FromResult<IReadOnlyList<SupplierLookupOptionDto>>(result);
    }

    public Task<IReadOnlyList<SupplierLookupOptionDto>> GetStatusesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var data = Helper.LoadLookup(
            _configuration,
            "PC_SupplierStatus",
            "SupplierStatusID",
            "SupplierStatusName",
            keyword: null,
            top: 100);

        var result = data.Select(x => new SupplierLookupOptionDto
        {
            Id = Convert.ToInt32(x.Id),
            CodeOrName = x.Text
        }).ToList();

        return Task.FromResult<IReadOnlyList<SupplierLookupOptionDto>>(result);
    }

    public async Task<EmployeeDataScopeDto> GetEmployeeDataScopeAsync(string? employeeCode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(employeeCode))
        {
            return new EmployeeDataScopeDto();
        }

        const string sql = @"
                            SELECT TOP 1 DeptID, ISNULL(SeeDataAllDept, 0) AS SeeDataAllDept
                            FROM dbo.MS_Employee
                            WHERE EmployeeCode = @EmployeeCode";

        return await Helper.QuerySingleOrDefaultAsync(
            _connectionString,
            sql,
            rd => new EmployeeDataScopeDto
            {
                DeptID = rd.IsDBNull(0) ? null : Convert.ToInt32(rd[0]),
                SeeDataAllDept = !rd.IsDBNull(1) && Convert.ToBoolean(rd[1])
            },
            cmd => Helper.AddParameter(cmd, "@EmployeeCode", employeeCode.Trim(), SqlDbType.NVarChar, 50),
            cancellationToken) ?? new EmployeeDataScopeDto();
    }

    public async Task<int?> GetSupplierDepartmentAsync(int supplierId, string? viewMode, int? year, CancellationToken cancellationToken = default)
    {
        var isByYear = string.Equals(viewMode, "byyear", StringComparison.OrdinalIgnoreCase);
        var sourceTable = isByYear ? "dbo.PC_SupplierAnualy" : "dbo.PC_Suppliers";
        var yearFilter = isByYear ? $@" AND [{AnnualYearColumn}] = @Year" : string.Empty;

        var sql = $@"
                    SELECT TOP 1 DeptID
                    FROM {sourceTable}
                    WHERE SupplierID = @SupplierID{yearFilter}";

        return await Helper.QuerySingleOrDefaultAsync<int?>(
            _connectionString,
            sql,
            rd => rd.IsDBNull(0) ? null : Convert.ToInt32(rd[0]),
            cmd =>
            {
                Helper.AddParameter(cmd, "@SupplierID", supplierId, SqlDbType.Int);
                if (isByYear)
                {
                    Helper.AddParameter(cmd, "@Year", year, SqlDbType.Int);
                }
            },
            cancellationToken);
    }

    public async Task<IReadOnlyList<SupplierListRowDto>> SearchAsync(SupplierFilterCriteria criteria, CancellationToken cancellationToken = default)
    {
        var noPagingCriteria = new SupplierFilterCriteria
        {
            ViewMode = criteria.ViewMode,
            Year = criteria.Year,
            DeptId = criteria.DeptId,
            SupplierCode = criteria.SupplierCode,
            SupplierName = criteria.SupplierName,
            Business = criteria.Business,
            Contact = criteria.Contact,
            StatusId = criteria.StatusId,
            IsNew = criteria.IsNew,
            PageIndex = null,
            PageSize = null
        };
        var result = await SearchPagedAsync(noPagingCriteria, cancellationToken);
        return result.Rows;
    }

    public async Task<SupplierSearchResultDto> SearchPagedAsync(SupplierFilterCriteria criteria, CancellationToken cancellationToken = default)
    {
        var isByYear = string.Equals(criteria.ViewMode, "byyear", StringComparison.OrdinalIgnoreCase);
        var sourceTable = isByYear ? "dbo.PC_SupplierAnualy" : "dbo.PC_Suppliers";
        var pageIndex = criteria.PageIndex.GetValueOrDefault() <= 0 ? 1 : criteria.PageIndex!.Value;
        var pageSize = criteria.PageSize.GetValueOrDefault() <= 0 ? 25 : criteria.PageSize!.Value;
        var applyPaging = criteria.PageIndex.HasValue && criteria.PageSize.HasValue;
        var yearFilterSql = isByYear
            ? $"\n    AND (@Year IS NULL OR s.[{AnnualYearColumn}] = @Year)"
            : string.Empty;
        var fromWhereSql = $@"
FROM {sourceTable} s
LEFT JOIN dbo.PC_SupplierStatus st ON s.[Status] = st.SupplierStatusID
LEFT JOIN dbo.MS_Department d ON s.DeptID = d.DeptID
WHERE
    (@SupplierCode IS NULL OR s.SupplierCode LIKE '%' + @SupplierCode + '%')
    AND (@SupplierName IS NULL OR s.SupplierName LIKE '%' + @SupplierName + '%')
    AND (@Business IS NULL OR s.Business LIKE '%' + @Business + '%')
    AND (@Contact IS NULL OR s.Contact LIKE '%' + @Contact + '%')
    AND (@DeptID IS NULL OR s.DeptID = @DeptID)
    AND (@StatusID IS NULL OR s.[Status] = @StatusID)
    AND (@IsNew = 0 OR s.IsNew = 1){yearFilterSql}";

        var countSql = "SELECT COUNT(1) " + fromWhereSql;
        var totalObj = await Helper.ExecuteScalarAsync(
            _connectionString,
            countSql,
            cmd => BindSearchParams(cmd, criteria, isByYear),
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
    s.ApprovedDate,
    s.[Document],
    s.Certificate,
    s.Service,
    s.Comment,
    s.IsNew,
    s.CodeOfAcc,
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
            rd => new SupplierListRowDto
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
                Document = rd[11]?.ToString() ?? string.Empty,
                Certificate = rd[12]?.ToString() ?? string.Empty,
                Service = rd[13]?.ToString() ?? string.Empty,
                Comment = rd[14]?.ToString() ?? string.Empty,
                IsNew = !rd.IsDBNull(15) && Convert.ToBoolean(rd[15]),
                CodeOfAcc = rd[16]?.ToString() ?? string.Empty,
                DeptID = rd.IsDBNull(17) ? null : rd.GetInt32(17),
                DeptCode = rd[18]?.ToString() ?? string.Empty,
                SupplierStatusName = rd[19]?.ToString() ?? string.Empty,
                Status = rd.IsDBNull(20) ? null : Convert.ToInt32(rd[20])
            },
            cmd =>
            {
                BindSearchParams(cmd, criteria, isByYear);
                if (applyPaging)
                {
                    Helper.AddParameter(cmd, "@Offset", (pageIndex - 1) * pageSize, SqlDbType.Int);
                    Helper.AddParameter(cmd, "@PageSize", pageSize, SqlDbType.Int);
                }
            },
            cancellationToken);

        return new SupplierSearchResultDto
        {
            Rows = rows.ToList(),
            TotalCount = totalCount
        };
    }

    public async Task CopyCurrentSuppliersToYearAsync(int copyYear, IReadOnlyCollection<int> supplierIds, CancellationToken cancellationToken = default)
    {
        if (supplierIds is null || supplierIds.Count == 0)
        {
            throw new InvalidOperationException("No supplier selected for copy.");
        }

        var normalizedIds = supplierIds
            .Distinct()
            .Where(x => x > 0)
            .ToList();

        if (normalizedIds.Count == 0)
        {
            throw new InvalidOperationException("No valid supplier selected for copy.");
        }

        var inClause = string.Join(", ", normalizedIds);

        var sql = $@"
                    CREATE TABLE #CopyIds (SupplierID int NOT NULL PRIMARY KEY);

                    INSERT INTO #CopyIds (SupplierID)
                    SELECT s.SupplierID
                    FROM dbo.PC_Suppliers s
                    WHERE s.SupplierID IN ({inClause})
                    AND NOT EXISTS (
                        SELECT 1
                        FROM dbo.PC_SupplierAnualy a
                        WHERE a.SupplierID = s.SupplierID
                        AND a.[{AnnualYearColumn}] = @CopyYear
                    );

                    INSERT INTO dbo.PC_SupplierAnualy
                    (
                        SupplierID, SupplierCode, SupplierName, Address, Phone, Mobile, Fax,
                        Contact, [Position], Business, ApprovedDate, [Document], Certificate,
                        Service, Comment, Appcept, IsApproved, DeptID, IsNew, CodeOfAcc, IsLinen, [Status],
                        PurchaserCode, PurchaserPreparedDate, PurchaserCPT,
                        DepartmentCode, DepartmentApproveDate, DepartmentCPT,
                        FinancialCode, FinancialApproveDate, FinancialCPT,
                        BODCode, BODApproveDate, BODCPT, [{AnnualYearColumn}]
                    )
                    SELECT
                        SupplierID, SupplierCode, SupplierName, Address, Phone, Mobile, Fax,
                        Contact, [Position], Business, ApprovedDate, [Document], Certificate,
                        Service, Comment, Appcept, IsApproved, DeptID, IsNew, CodeOfAcc, IsLinen, [Status],
                        PurchaserCode, PurchaserPreparedDate, PurchaserCPT,
                        DepartmentCode, DepartmentApproveDate, DepartmentCPT,
                        FinancialCode, FinancialApproveDate, FinancialCPT,
                        BODCode, BODApproveDate, BODCPT, @CopyYear
                    FROM dbo.PC_Suppliers
                    WHERE SupplierID IN (SELECT SupplierID FROM #CopyIds);";

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        await using var tx = await conn.BeginTransactionAsync(cancellationToken);
        try
        {
            await using var cmd = new SqlCommand(sql, conn, (SqlTransaction)tx);
            cmd.Parameters.AddWithValue("@CopyYear", copyYear);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<SupplierDetailDto?> GetDetailAsync(int supplierId, CancellationToken cancellationToken = default)
    {
        const string sql = @"
                            SELECT SupplierCode,SupplierName,Address,Phone,Mobile,Fax,Contact,[Position],Business,
                                ApprovedDate,[Document],Certificate,Service,Comment,IsNew,CodeOfAcc,DeptID,[Status]
                            FROM dbo.PC_Suppliers
                            WHERE SupplierID=@ID";

        return await Helper.QuerySingleOrDefaultAsync(
            _connectionString,
            sql,
            rd => new SupplierDetailDto
            {
                SupplierCode = rd[0]?.ToString(),
                SupplierName = rd[1]?.ToString(),
                Address = rd[2]?.ToString(),
                Phone = rd[3]?.ToString(),
                Mobile = rd[4]?.ToString(),
                Fax = rd[5]?.ToString(),
                Contact = rd[6]?.ToString(),
                Position = rd[7]?.ToString(),
                Business = rd[8]?.ToString(),
                ApprovedDate = rd.IsDBNull(9) ? null : rd.GetDateTime(9),
                Document = !rd.IsDBNull(10) && Convert.ToBoolean(rd[10]),
                Certificate = rd[11]?.ToString(),
                Service = rd[12]?.ToString(),
                Comment = rd[13]?.ToString(),
                IsNew = !rd.IsDBNull(14) && Convert.ToBoolean(rd[14]),
                CodeOfAcc = rd[15]?.ToString(),
                DeptID = rd.IsDBNull(16) ? null : Convert.ToInt32(rd[16]),
                Status = rd.IsDBNull(17) ? null : Convert.ToInt32(rd[17])
            },
            cmd => Helper.AddParameter(cmd, "@ID", supplierId, SqlDbType.Int),
            cancellationToken);
    }

    public async Task<IReadOnlyList<SupplierApprovalHistoryDto>> GetApprovalHistoryAsync(int supplierId, CancellationToken cancellationToken = default)
    {
        const string sql = @"
                            WITH ApprovalHistory AS
                            (
                                SELECT 'Purchasing Officer submitted' AS [Action], PurchaserCode AS [UserCode], PurchaserPreparedDate AS [ActionDate]
                                FROM dbo.PC_Suppliers WHERE SupplierID=@ID
                                UNION ALL
                                SELECT 'Head Department approved/dis', DepartmentCode, DepartmentApproveDate FROM dbo.PC_Suppliers WHERE SupplierID=@ID
                                UNION ALL
                                SELECT 'Head Financial approved/dis', FinancialCode, FinancialApproveDate FROM dbo.PC_Suppliers WHERE SupplierID=@ID
                                UNION ALL
                                SELECT 'BOD approved/dis', BODCode, BODApproveDate FROM dbo.PC_Suppliers WHERE SupplierID=@ID
                            )
                            SELECT
                                h.[Action],
                                COALESCE(NULLIF(e.EmployeeName, ''), h.[UserCode], '') AS [UserName],
                                h.[ActionDate]
                            FROM ApprovalHistory h
                            LEFT JOIN dbo.MS_Employee e ON e.EmployeeCode = h.[UserCode]";

        return await Helper.QueryAsync(
            _connectionString,
            sql,
            rd => new SupplierApprovalHistoryDto
            {
                Action = rd[0]?.ToString() ?? string.Empty,
                UserName = rd[1]?.ToString() ?? string.Empty,
                ActionDate = rd.IsDBNull(2) ? null : rd.GetDateTime(2)
            },
            cmd => Helper.AddParameter(cmd, "@ID", supplierId, SqlDbType.Int),
            cancellationToken);
    }

    public async Task<SupplierDetailDto?> GetAnnualDetailAsync(int supplierId, int year, CancellationToken cancellationToken = default)
    {
        var sql = $@"
                    SELECT SupplierCode,SupplierName,Address,Phone,Mobile,Fax,Contact,[Position],Business,
                        ApprovedDate,[Document],Certificate,Service,Comment,IsNew,CodeOfAcc,DeptID,[Status]
                    FROM dbo.PC_SupplierAnualy
                    WHERE SupplierID=@ID
                    AND [{AnnualYearColumn}]=@Year";

        return await Helper.QuerySingleOrDefaultAsync(
            _connectionString,
            sql,
            rd => new SupplierDetailDto
            {
                SupplierCode = rd[0]?.ToString(),
                SupplierName = rd[1]?.ToString(),
                Address = rd[2]?.ToString(),
                Phone = rd[3]?.ToString(),
                Mobile = rd[4]?.ToString(),
                Fax = rd[5]?.ToString(),
                Contact = rd[6]?.ToString(),
                Position = rd[7]?.ToString(),
                Business = rd[8]?.ToString(),
                ApprovedDate = rd.IsDBNull(9) ? null : rd.GetDateTime(9),
                Document = !rd.IsDBNull(10) && Convert.ToBoolean(rd[10]),
                Certificate = rd[11]?.ToString(),
                Service = rd[12]?.ToString(),
                Comment = rd[13]?.ToString(),
                IsNew = !rd.IsDBNull(14) && Convert.ToBoolean(rd[14]),
                CodeOfAcc = rd[15]?.ToString(),
                DeptID = rd.IsDBNull(16) ? null : Convert.ToInt32(rd[16]),
                Status = rd.IsDBNull(17) ? null : Convert.ToInt32(rd[17])
            },
            cmd =>
            {
                Helper.AddParameter(cmd, "@ID", supplierId, SqlDbType.Int);
                Helper.AddParameter(cmd, "@Year", year, SqlDbType.Int);
            },
            cancellationToken);
    }

    public async Task<IReadOnlyList<SupplierApprovalHistoryDto>> GetAnnualApprovalHistoryAsync(int supplierId, int year, CancellationToken cancellationToken = default)
    {
        var sql = $@"
                    WITH ApprovalHistory AS
                    (
                        SELECT 'Purchasing Officer submitted' AS [Action], PurchaserCode AS [UserCode], PurchaserPreparedDate AS [ActionDate]
                        FROM dbo.PC_SupplierAnualy WHERE SupplierID=@ID AND [{AnnualYearColumn}]=@Year
                        UNION ALL
                        SELECT 'Head Department approved/dis', DepartmentCode, DepartmentApproveDate FROM dbo.PC_SupplierAnualy WHERE SupplierID=@ID AND [{AnnualYearColumn}]=@Year
                        UNION ALL
                        SELECT 'Head Financial approved/dis', FinancialCode, FinancialApproveDate FROM dbo.PC_SupplierAnualy WHERE SupplierID=@ID AND [{AnnualYearColumn}]=@Year
                        UNION ALL
                        SELECT 'BOD approved/dis', BODCode, BODApproveDate FROM dbo.PC_SupplierAnualy WHERE SupplierID=@ID AND [{AnnualYearColumn}]=@Year
                    )
                    SELECT
                        h.[Action],
                        COALESCE(NULLIF(e.EmployeeName, ''), h.[UserCode], '') AS [UserName],
                        h.[ActionDate]
                    FROM ApprovalHistory h
                    LEFT JOIN dbo.MS_Employee e ON e.EmployeeCode = h.[UserCode]";

        return await Helper.QueryAsync(
            _connectionString,
            sql,
            rd => new SupplierApprovalHistoryDto
            {
                Action = rd[0]?.ToString() ?? string.Empty,
                UserName = rd[1]?.ToString() ?? string.Empty,
                ActionDate = rd.IsDBNull(2) ? null : rd.GetDateTime(2)
            },
            cmd =>
            {
                Helper.AddParameter(cmd, "@ID", supplierId, SqlDbType.Int);
                Helper.AddParameter(cmd, "@Year", year, SqlDbType.Int);
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

    public async Task<string> GetSuggestedSupplierCodeAsync(CancellationToken cancellationToken = default)
    {
        const string sql = @"
                            SELECT
                                ISNULL(MAX(TRY_CONVERT(int, SUBSTRING(LTRIM(RTRIM(SupplierCode)), 3, 50))), 0) AS MaxNo,
                                ISNULL(MAX(LEN(LTRIM(RTRIM(SupplierCode))) - 2), 3) AS NumWidth
                            FROM dbo.PC_Suppliers
                            WHERE LEFT(UPPER(LTRIM(RTRIM(SupplierCode))), 2) = 'SP'
                            AND TRY_CONVERT(int, SUBSTRING(LTRIM(RTRIM(SupplierCode)), 3, 50)) IS NOT NULL;";

        var seed = await Helper.QuerySingleOrDefaultAsync(
            _connectionString,
            sql,
            rd => new
            {
                MaxNo = rd.IsDBNull(0) ? 0 : Convert.ToInt32(rd[0]),
                NumWidth = rd.IsDBNull(1) ? 3 : Convert.ToInt32(rd[1])
            },
            null,
            cancellationToken);

        var nextNo = (seed?.MaxNo ?? 0) + 1;
        var width = Math.Max(3, seed?.NumWidth ?? 3);
        return $"SP{nextNo.ToString().PadLeft(width, '0')}";
    }

    public async Task<int> SaveAsync(int? supplierId, SupplierDetailDto detail, string operatorCode, CancellationToken cancellationToken = default)
    {
        var supplierCode = (detail.SupplierCode ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(supplierCode))
        {
            throw new InvalidOperationException("Supplier code is required.");
        }

        var exists = await SupplierCodeExistsAsync(
            supplierCode,
            supplierId.HasValue && supplierId.Value > 0 ? supplierId.Value : null,
            cancellationToken);

        if (exists)
        {
            throw new InvalidOperationException("Supplier code already exists.");
        }

        detail.SupplierCode = supplierCode;

        if (supplierId.HasValue && supplierId.Value > 0)
        {
            await UpdateAsync(supplierId.Value, detail, cancellationToken);
            return supplierId.Value;
        }

        return await CreateAsync(detail, operatorCode, cancellationToken);
    }

    public async Task SubmitApprovalAsync(int supplierId, string operatorCode, CancellationToken cancellationToken = default)
    {
        const string sql = @"
                            UPDATE dbo.PC_Suppliers
                            SET [Status]=1,
                                PurchaserCode=@OperatorCode,
                                PurchaserPreparedDate=GETDATE()
                            WHERE SupplierID=@SupplierID";

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

    public async Task ResetWorkflowToPreparingAsync(int supplierId, CancellationToken cancellationToken = default)
    {
        const string sql = @"
                            UPDATE dbo.PC_Suppliers
                            SET [Status] = 0,
                                ApprovedDate = NULL,
                                PurchaserCode = NULL,
                                PurchaserPreparedDate = NULL,
                                PurchaserCPT = NULL,
                                DepartmentCode = NULL,
                                DepartmentApproveDate = NULL,
                                DepartmentCPT = NULL,
                                FinancialCode = NULL,
                                FinancialApproveDate = NULL,
                                FinancialCPT = NULL,
                                BODCode = NULL,
                                BODApproveDate = NULL,
                                BODCPT = NULL
                            WHERE SupplierID = @SupplierID";

        await Helper.ExecuteNonQueryAsync(
            _connectionString,
            sql,
            cmd => { Helper.AddParameter(cmd, "@SupplierID", supplierId, SqlDbType.Int); },
            cancellationToken);
    }

    private async Task<int> CreateAsync(SupplierDetailDto detail, string operatorCode, CancellationToken cancellationToken = default)
    {
        const string sql = @"
                            INSERT INTO dbo.PC_Suppliers
                            (
                                SupplierCode,SupplierName,Address,Phone,Mobile,Fax,Contact,[Position],Business,
                                [Document],Certificate,Service,Comment,IsNew,CodeOfAcc,DeptID,[Status],
                                PurchaserCode,PurchaserPreparedDate
                            )
                            VALUES
                            (
                                @SupplierCode,@SupplierName,@Address,@Phone,@Mobile,@Fax,@Contact,@Position,@Business,
                                @Document,@Certificate,@Service,@Comment,@IsNew,@CodeOfAcc,@DeptID,0,
                                NULL,NULL
                            );
                            SELECT CAST(SCOPE_IDENTITY() as int);";

        var result = await Helper.ExecuteScalarAsync(
            _connectionString,
            sql,
            cmd =>
            {
                BindDetailParams(cmd, detail);
                Helper.AddParameter(cmd, "@OperatorCode", operatorCode, SqlDbType.NVarChar, 50);
            },
            cancellationToken);
        return Convert.ToInt32(result);
    }

    private async Task UpdateAsync(int supplierId, SupplierDetailDto detail, CancellationToken cancellationToken = default)
    {
        const string sql = @"
                            UPDATE dbo.PC_Suppliers
                            SET SupplierCode=@SupplierCode,
                                SupplierName=@SupplierName,
                                Address=@Address,
                                Phone=@Phone,
                                Mobile=@Mobile,
                                Fax=@Fax,
                                Contact=@Contact,
                                [Position]=@Position,
                                Business=@Business,
                                [Document]=@Document,
                                Certificate=@Certificate,
                                Service=@Service,
                                Comment=@Comment,
                                IsNew=@IsNew,
                                CodeOfAcc=@CodeOfAcc,
                                DeptID=@DeptID
                            WHERE SupplierID=@SupplierID";

        await Helper.ExecuteNonQueryAsync(
            _connectionString,
            sql,
            cmd =>
            {
                BindDetailParams(cmd, detail);
                Helper.AddParameter(cmd, "@SupplierID", supplierId, SqlDbType.Int);
            },
            cancellationToken);
    }

    private static void BindDetailParams(SqlCommand cmd, SupplierDetailDto detail)
    {
        AddNullable(cmd, "@SupplierCode", detail.SupplierCode);
        AddNullable(cmd, "@SupplierName", detail.SupplierName);
        AddNullable(cmd, "@Address", detail.Address);
        AddNullable(cmd, "@Phone", detail.Phone);
        AddNullable(cmd, "@Mobile", detail.Mobile);
        AddNullable(cmd, "@Fax", detail.Fax);
        AddNullable(cmd, "@Contact", detail.Contact);
        AddNullable(cmd, "@Position", detail.Position);
        AddNullable(cmd, "@Business", detail.Business);
        AddNullable(cmd, "@Document", detail.Document);
        AddNullable(cmd, "@Certificate", detail.Certificate);
        AddNullable(cmd, "@Service", detail.Service);
        AddNullable(cmd, "@Comment", detail.Comment);
        Helper.AddParameter(cmd, "@IsNew", detail.IsNew, SqlDbType.Bit);
        AddNullable(cmd, "@CodeOfAcc", detail.CodeOfAcc);
        AddNullable(cmd, "@DeptID", detail.DeptID);
    }

    private static void BindSearchParams(SqlCommand cmd, SupplierFilterCriteria criteria, bool isByYear)
    {
        Helper.AddParameter(cmd, "@SupplierCode", NullIfEmpty(criteria.SupplierCode), SqlDbType.NVarChar, 255);
        Helper.AddParameter(cmd, "@SupplierName", NullIfEmpty(criteria.SupplierName), SqlDbType.NVarChar, 255);
        Helper.AddParameter(cmd, "@Business", NullIfEmpty(criteria.Business), SqlDbType.NVarChar, 255);
        Helper.AddParameter(cmd, "@Contact", NullIfEmpty(criteria.Contact), SqlDbType.NVarChar, 255);
        Helper.AddParameter(cmd, "@DeptID", criteria.DeptId, SqlDbType.Int);
        Helper.AddParameter(cmd, "@StatusID", criteria.StatusId, SqlDbType.Int);
        Helper.AddParameter(cmd, "@IsNew", criteria.IsNew ? 1 : 0, SqlDbType.Int);
        if (isByYear)
        {
            Helper.AddParameter(cmd, "@Year", criteria.Year, SqlDbType.Int);
        }
    }

    private static void AddNullable(SqlCommand cmd, string name, object? value)
    {
        Helper.AddParameter(cmd, name, value);
    }

    private static string? NullIfEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

}
