using System.Data;
using Microsoft.Data.SqlClient;
using SmartSam.Helpers;
using SmartSam.Models.Purchasing.Supplier;
using SmartSam.Services.Purchasing.Supplier.Abstractions;

namespace SmartSam.Services.Purchasing.Supplier.Implementations;

public class SupplierRepository : ISupplierRepository
{
    private readonly string _connectionString;

    public SupplierRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Missing connection string: DefaultConnection");
    }

    public async Task<IReadOnlyList<SupplierLookupOptionDto>> GetDepartmentsAsync(CancellationToken cancellationToken = default)
    {
        const string sql = "SELECT DeptID, DeptCode FROM dbo.MS_Department ORDER BY DeptCode";
        return await Helper.QueryAsync(
            _connectionString,
            sql,
            rd => new SupplierLookupOptionDto
            {
                Id = rd.IsDBNull(0) ? 0 : rd.GetInt32(0),
                CodeOrName = rd[1]?.ToString() ?? string.Empty
            },
            cancellationToken: cancellationToken);
    }

    public async Task<IReadOnlyList<SupplierLookupOptionDto>> GetStatusesAsync(CancellationToken cancellationToken = default)
    {
        const string sql = "SELECT SupplierStatusID, SupplierStatusName FROM dbo.PC_SupplierStatus ORDER BY SupplierStatusID";
        return await Helper.QueryAsync(
            _connectionString,
            sql,
            rd => new SupplierLookupOptionDto
            {
                Id = rd.IsDBNull(0) ? 0 : rd.GetInt32(0),
                CodeOrName = rd[1]?.ToString() ?? string.Empty
            },
            cancellationToken: cancellationToken);
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
        var annualYearColumn = isByYear ? await GetSupplierAnnualYearColumnAsync(cancellationToken) : null;
        var hasDeleteColumns = isByYear ? false : await HasSupplierDeleteColumnsAsync(cancellationToken);
        var pageIndex = criteria.PageIndex.GetValueOrDefault() <= 0 ? 1 : criteria.PageIndex!.Value;
        var pageSize = criteria.PageSize.GetValueOrDefault() <= 0 ? 25 : criteria.PageSize!.Value;
        var applyPaging = criteria.PageIndex.HasValue && criteria.PageSize.HasValue;
        var yearFilterSql = isByYear && !string.IsNullOrWhiteSpace(annualYearColumn)
            ? $"\n    AND (@Year IS NULL OR s.{QuoteIdentifier(annualYearColumn)} = @Year)"
            : string.Empty;
        var isDeletedSelectSql = !isByYear && hasDeleteColumns
            ? "ISNULL(s.IsDeleted, 0)"
            : "0";
        var deleteFilterSql = !isByYear && hasDeleteColumns && !criteria.IncludeDeleted
            ? "\n    AND ISNULL(s.IsDeleted, 0) = 0"
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
    AND (@IsNew = 0 OR s.IsNew = 1){yearFilterSql}{deleteFilterSql}";

        var countSql = "SELECT COUNT(1) " + fromWhereSql;
        var totalObj = await Helper.ExecuteScalarAsync(
            _connectionString,
            countSql,
            cmd => BindSearchParams(cmd, criteria, isByYear, annualYearColumn),
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
    s.[Status],
    {isDeletedSelectSql} AS IsDeleted
{fromWhereSql}
ORDER BY s.SupplierCode, s.SupplierID";

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
                Status = rd.IsDBNull(20) ? null : Convert.ToInt32(rd[20]),
                IsDeleted = !rd.IsDBNull(21) && Convert.ToInt32(rd[21]) == 1
            },
            cmd =>
            {
                BindSearchParams(cmd, criteria, isByYear, annualYearColumn);
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

    public async Task CopyCurrentSuppliersToYearAsync(int copyYear, CancellationToken cancellationToken = default)
    {
        var sql = @"
DECLARE @YearCol sysname = (
    SELECT TOP 1 c.name
    FROM sys.columns c
    WHERE c.object_id = OBJECT_ID('dbo.PC_SupplierAnualy')
      AND c.name IN ('ForYear', 'Year', 'TheYear', 'YearNo', 'AnnualYear')
);

IF @YearCol IS NULL
BEGIN
    INSERT INTO dbo.PC_SupplierAnualy
    (
        SupplierID, SupplierCode, SupplierName, Address, Phone, Mobile, Fax,
        Contact, [Position], Business, ApprovedDate, [Document], Certificate,
        Service, Comment, IsNew, CodeOfAcc, DeptID, [Status]
    )
    SELECT
        SupplierID, SupplierCode, SupplierName, Address, Phone, Mobile, Fax,
        Contact, [Position], Business, ApprovedDate, [Document], Certificate,
        Service, Comment, IsNew, CodeOfAcc, DeptID, [Status]
    FROM dbo.PC_Suppliers;
END
ELSE
BEGIN
    DECLARE @Sql nvarchar(max) = N'
        INSERT INTO dbo.PC_SupplierAnualy
        (
            SupplierID, SupplierCode, SupplierName, Address, Phone, Mobile, Fax,
            Contact, [Position], Business, ApprovedDate, [Document], Certificate,
            Service, Comment, IsNew, CodeOfAcc, DeptID, [Status], ' + QUOTENAME(@YearCol) + N'
        )
        SELECT
            SupplierID, SupplierCode, SupplierName, Address, Phone, Mobile, Fax,
            Contact, [Position], Business, ApprovedDate, [Document], Certificate,
            Service, Comment, IsNew, CodeOfAcc, DeptID, [Status], @Y
        FROM dbo.PC_Suppliers;';

    EXEC sp_executesql @Sql, N'@Y int', @Y = @CopyYear;
END

UPDATE dbo.PC_Suppliers
SET [Status] = 0;";

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
       ApprovedDate,[Document],Certificate,Service,Comment,IsNew,CodeOfAcc,DeptID,[Status],
       CASE WHEN COL_LENGTH('dbo.PC_Suppliers', 'IsDeleted') IS NULL THEN 0 ELSE ISNULL(IsDeleted, 0) END AS IsDeleted
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
                Document = rd[10]?.ToString(),
                Certificate = rd[11]?.ToString(),
                Service = rd[12]?.ToString(),
                Comment = rd[13]?.ToString(),
                IsNew = !rd.IsDBNull(14) && Convert.ToBoolean(rd[14]),
                CodeOfAcc = rd[15]?.ToString(),
                DeptID = rd.IsDBNull(16) ? null : Convert.ToInt32(rd[16]),
                Status = rd.IsDBNull(17) ? null : Convert.ToInt32(rd[17]),
                IsDeleted = !rd.IsDBNull(18) && Convert.ToInt32(rd[18]) == 1
            },
            cmd => Helper.AddParameter(cmd, "@ID", supplierId, SqlDbType.Int),
            cancellationToken);
    }

    public async Task<IReadOnlyList<SupplierApprovalHistoryDto>> GetApprovalHistoryAsync(int supplierId, CancellationToken cancellationToken = default)
    {
        const string sql = @"
SELECT 'Purchasing Officer submitted' AS [Action], PurchaserCode AS [UserCode], PurchaserPreparedDate AS [ActionDate]
FROM dbo.PC_Suppliers WHERE SupplierID=@ID
UNION ALL
SELECT 'Head Department approved/dis', DepartmentCode, DepartmentApproveDate FROM dbo.PC_Suppliers WHERE SupplierID=@ID
UNION ALL
SELECT 'Head Financial approved/dis', FinancialCode, FinancialApproveDate FROM dbo.PC_Suppliers WHERE SupplierID=@ID
UNION ALL
SELECT 'BOD approved/dis', BODCode, BODApproveDate FROM dbo.PC_Suppliers WHERE SupplierID=@ID";

        return await Helper.QueryAsync(
            _connectionString,
            sql,
            rd => new SupplierApprovalHistoryDto
            {
                Action = rd[0]?.ToString() ?? string.Empty,
                UserCode = rd[1]?.ToString() ?? string.Empty,
                ActionDate = rd.IsDBNull(2) ? null : rd.GetDateTime(2)
            },
            cmd => Helper.AddParameter(cmd, "@ID", supplierId, SqlDbType.Int),
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

    public async Task<int> CreateAsync(SupplierDetailDto detail, string operatorCode, CancellationToken cancellationToken = default)
    {
        const string sql = @"
INSERT INTO dbo.PC_Suppliers
(
    SupplierCode,SupplierName,Address,Phone,Mobile,Fax,Contact,[Position],Business,
    ApprovedDate,[Document],Certificate,Service,Comment,IsNew,CodeOfAcc,DeptID,[Status],
    PurchaserCode,PurchaserPreparedDate
)
VALUES
(
    @SupplierCode,@SupplierName,@Address,@Phone,@Mobile,@Fax,@Contact,@Position,@Business,
    @ApprovedDate,@Document,@Certificate,@Service,@Comment,@IsNew,@CodeOfAcc,@DeptID,@Status,
    CASE WHEN ISNULL(@Status, 0) = 1 THEN @OperatorCode ELSE NULL END,
    CASE WHEN ISNULL(@Status, 0) = 1 THEN GETDATE() ELSE NULL END
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

    public async Task UpdateAsync(int supplierId, SupplierDetailDto detail, CancellationToken cancellationToken = default)
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
    ApprovedDate=@ApprovedDate,
    [Document]=@Document,
    Certificate=@Certificate,
    Service=@Service,
    Comment=@Comment,
    IsNew=@IsNew,
    CodeOfAcc=@CodeOfAcc,
    DeptID=@DeptID,
    [Status]=@Status
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

    public async Task<SupplierDeleteResultDto> DeleteAsync(int supplierId, string operatorCode, CancellationToken cancellationToken = default)
    {
        const string inspectSql = @"
SELECT
    SupplierID,
    ISNULL([Status], 0) AS [Status],
    CASE WHEN PurchaserCode IS NOT NULL OR PurchaserPreparedDate IS NOT NULL
           OR DepartmentCode IS NOT NULL OR DepartmentApproveDate IS NOT NULL
           OR FinancialCode IS NOT NULL OR FinancialApproveDate IS NOT NULL
           OR BODCode IS NOT NULL OR BODApproveDate IS NOT NULL
         THEN 1 ELSE 0 END AS HasApprovalTrace,
    CASE WHEN EXISTS (SELECT 1 FROM dbo.PC_SupplierAnualy a WHERE a.SupplierID = s.SupplierID) THEN 1 ELSE 0 END AS HasAnnualRef,
    CASE WHEN COL_LENGTH('dbo.PC_Suppliers', 'IsDeleted') IS NULL THEN 0 ELSE ISNULL(IsDeleted, 0) END AS IsDeleted
FROM dbo.PC_Suppliers s
WHERE SupplierID = @SupplierID;";

        var inspected = await Helper.QuerySingleOrDefaultAsync(
            _connectionString,
            inspectSql,
            rd => new
            {
                Status = rd.IsDBNull(1) ? 0 : Convert.ToInt32(rd[1]),
                HasApprovalTrace = !rd.IsDBNull(2) && Convert.ToInt32(rd[2]) == 1,
                HasAnnualRef = !rd.IsDBNull(3) && Convert.ToInt32(rd[3]) == 1,
                IsDeleted = !rd.IsDBNull(4) && Convert.ToInt32(rd[4]) == 1
            },
            cmd => Helper.AddParameter(cmd, "@SupplierID", supplierId, SqlDbType.Int),
            cancellationToken);

        if (inspected is null || inspected.IsDeleted)
        {
            return new SupplierDeleteResultDto { NotFound = true, Reason = "Không tìm thấy nhà cung cấp." };
        }

        var canHardDelete = inspected.Status == 0 && !inspected.HasApprovalTrace && !inspected.HasAnnualRef;
        if (canHardDelete)
        {
            const string hardDeleteSql = "DELETE FROM dbo.PC_Suppliers WHERE SupplierID=@SupplierID;";
            await Helper.ExecuteNonQueryAsync(
                _connectionString,
                hardDeleteSql,
                cmd => Helper.AddParameter(cmd, "@SupplierID", supplierId, SqlDbType.Int),
                cancellationToken);

            return new SupplierDeleteResultDto
            {
                Success = true,
                IsHardDelete = true
            };
        }

        var hasDeleteColumns = await HasSupplierDeleteColumnsAsync(cancellationToken);
        if (!hasDeleteColumns)
        {
            return new SupplierDeleteResultDto
            {
                Success = false,
                Reason = "Thiếu cột soft delete (IsDeleted/DeletedBy/DeletedDate). Hãy chạy migration DB."
            };
        }

        const string softDeleteSql = @"
UPDATE dbo.PC_Suppliers
SET IsDeleted = 1,
    DeletedBy = @OperatorCode,
    DeletedDate = GETDATE()
WHERE SupplierID = @SupplierID
  AND ISNULL(IsDeleted, 0) = 0;";

        var affected = await Helper.ExecuteNonQueryAsync(
            _connectionString,
            softDeleteSql,
            cmd =>
            {
                Helper.AddParameter(cmd, "@SupplierID", supplierId, SqlDbType.Int);
                Helper.AddParameter(cmd, "@OperatorCode", operatorCode, SqlDbType.NVarChar, 50);
            },
            cancellationToken);

        return new SupplierDeleteResultDto
        {
            Success = affected > 0,
            IsSoftDelete = affected > 0,
            Reason = affected > 0 ? null : "Không thể soft delete nhà cung cấp."
        };
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
        AddNullable(cmd, "@ApprovedDate", detail.ApprovedDate);
        AddNullable(cmd, "@Document", detail.Document);
        AddNullable(cmd, "@Certificate", detail.Certificate);
        AddNullable(cmd, "@Service", detail.Service);
        AddNullable(cmd, "@Comment", detail.Comment);
        Helper.AddParameter(cmd, "@IsNew", detail.IsNew, SqlDbType.Bit);
        AddNullable(cmd, "@CodeOfAcc", detail.CodeOfAcc);
        AddNullable(cmd, "@DeptID", detail.DeptID);
        AddNullable(cmd, "@Status", detail.Status);
    }

    private static void BindSearchParams(SqlCommand cmd, SupplierFilterCriteria criteria, bool isByYear, string? annualYearColumn)
    {
        Helper.AddParameter(cmd, "@SupplierCode", NullIfEmpty(criteria.SupplierCode), SqlDbType.NVarChar, 255);
        Helper.AddParameter(cmd, "@SupplierName", NullIfEmpty(criteria.SupplierName), SqlDbType.NVarChar, 255);
        Helper.AddParameter(cmd, "@Business", NullIfEmpty(criteria.Business), SqlDbType.NVarChar, 255);
        Helper.AddParameter(cmd, "@Contact", NullIfEmpty(criteria.Contact), SqlDbType.NVarChar, 255);
        Helper.AddParameter(cmd, "@DeptID", criteria.DeptId, SqlDbType.Int);
        Helper.AddParameter(cmd, "@StatusID", criteria.StatusId, SqlDbType.Int);
        Helper.AddParameter(cmd, "@IsNew", criteria.IsNew ? 1 : 0, SqlDbType.Int);
        if (isByYear && !string.IsNullOrWhiteSpace(annualYearColumn))
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

    private async Task<string?> GetSupplierAnnualYearColumnAsync(CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT TOP 1 c.name
FROM sys.columns c
WHERE c.object_id = OBJECT_ID('dbo.PC_SupplierAnualy')
  AND c.name IN ('ForYear', 'Year', 'TheYear', 'YearNo', 'AnnualYear')
ORDER BY CASE c.name
    WHEN 'ForYear' THEN 1
    WHEN 'Year' THEN 2
    WHEN 'TheYear' THEN 3
    WHEN 'YearNo' THEN 4
    WHEN 'AnnualYear' THEN 5
    ELSE 99
END;";

        var result = await Helper.ExecuteScalarAsync(_connectionString, sql, null, cancellationToken);
        return result?.ToString();
    }

    private static string QuoteIdentifier(string identifier)
        => $"[{identifier.Replace("]", "]]")}]";

    private async Task<bool> HasSupplierDeleteColumnsAsync(CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT COUNT(*)
FROM sys.columns
WHERE object_id = OBJECT_ID('dbo.PC_Suppliers')
  AND name IN ('IsDeleted', 'DeletedBy', 'DeletedDate');";

        var result = await Helper.ExecuteScalarAsync(_connectionString, sql, null, cancellationToken);
        return Convert.ToInt32(result) >= 3;
    }
}
