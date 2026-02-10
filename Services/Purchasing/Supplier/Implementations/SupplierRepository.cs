using System.Data;
using Microsoft.Data.SqlClient;
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
        var list = new List<SupplierLookupOptionDto>();

        await using var conn = new SqlConnection(_connectionString);
        await using var cmd = new SqlCommand(sql, conn);
        await conn.OpenAsync(cancellationToken);

        await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await rd.ReadAsync(cancellationToken))
        {
            list.Add(new SupplierLookupOptionDto
            {
                Id = rd.IsDBNull(0) ? 0 : rd.GetInt32(0),
                CodeOrName = rd[1]?.ToString() ?? string.Empty
            });
        }

        return list;
    }

    public async Task<IReadOnlyList<SupplierLookupOptionDto>> GetStatusesAsync(CancellationToken cancellationToken = default)
    {
        const string sql = "SELECT SupplierStatusID, SupplierStatusName FROM dbo.PC_SupplierStatus ORDER BY SupplierStatusID";
        var list = new List<SupplierLookupOptionDto>();

        await using var conn = new SqlConnection(_connectionString);
        await using var cmd = new SqlCommand(sql, conn);
        await conn.OpenAsync(cancellationToken);

        await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await rd.ReadAsync(cancellationToken))
        {
            list.Add(new SupplierLookupOptionDto
            {
                Id = rd.IsDBNull(0) ? 0 : rd.GetInt32(0),
                CodeOrName = rd[1]?.ToString() ?? string.Empty
            });
        }

        return list;
    }

    public async Task<IReadOnlyList<SupplierListRowDto>> SearchAsync(SupplierFilterCriteria criteria, CancellationToken cancellationToken = default)
    {
        var sourceTable = string.Equals(criteria.ViewMode, "byyear", StringComparison.OrdinalIgnoreCase)
            ? "dbo.PC_SupplierAnualy"
            : "dbo.PC_Suppliers";

        var sql = $@"
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
    AND (@IsNew = 0 OR s.IsNew = 1)
ORDER BY s.SupplierCode";

        var rows = new List<SupplierListRowDto>();
        await using var conn = new SqlConnection(_connectionString);
        await using var cmd = new SqlCommand(sql, conn);

        AddNullable(cmd, "@SupplierCode", NullIfEmpty(criteria.SupplierCode));
        AddNullable(cmd, "@SupplierName", NullIfEmpty(criteria.SupplierName));
        AddNullable(cmd, "@Business", NullIfEmpty(criteria.Business));
        AddNullable(cmd, "@Contact", NullIfEmpty(criteria.Contact));
        AddNullable(cmd, "@DeptID", criteria.DeptId);
        AddNullable(cmd, "@StatusID", criteria.StatusId);
        cmd.Parameters.AddWithValue("@IsNew", criteria.IsNew ? 1 : 0);

        await conn.OpenAsync(cancellationToken);
        await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await rd.ReadAsync(cancellationToken))
        {
            rows.Add(new SupplierListRowDto
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
            });
        }

        return rows;
    }

    public async Task CopyCurrentSuppliersToYearAsync(int copyYear, CancellationToken cancellationToken = default)
    {
        var sql = @"
DECLARE @YearCol sysname = (
    SELECT TOP 1 c.name
    FROM sys.columns c
    WHERE c.object_id = OBJECT_ID('dbo.PC_SupplierAnualy')
      AND c.name IN ('Year', 'TheYear', 'YearNo', 'AnnualYear')
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
       ApprovedDate,[Document],Certificate,Service,Comment,IsNew,CodeOfAcc,DeptID,[Status]
FROM dbo.PC_Suppliers
WHERE SupplierID=@ID";

        await using var conn = new SqlConnection(_connectionString);
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@ID", supplierId);

        await conn.OpenAsync(cancellationToken);
        await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await rd.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new SupplierDetailDto
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
            Status = rd.IsDBNull(17) ? null : Convert.ToInt32(rd[17])
        };
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

        var rows = new List<SupplierApprovalHistoryDto>();

        await using var conn = new SqlConnection(_connectionString);
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@ID", supplierId);

        await conn.OpenAsync(cancellationToken);
        await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await rd.ReadAsync(cancellationToken))
        {
            rows.Add(new SupplierApprovalHistoryDto
            {
                Action = rd[0]?.ToString() ?? string.Empty,
                UserCode = rd[1]?.ToString() ?? string.Empty,
                ActionDate = rd.IsDBNull(2) ? null : rd.GetDateTime(2)
            });
        }

        return rows;
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
    @OperatorCode,GETDATE()
);
SELECT CAST(SCOPE_IDENTITY() as int);";

        await using var conn = new SqlConnection(_connectionString);
        await using var cmd = new SqlCommand(sql, conn);
        BindDetailParams(cmd, detail);
        cmd.Parameters.AddWithValue("@OperatorCode", operatorCode);

        await conn.OpenAsync(cancellationToken);
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
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

        await using var conn = new SqlConnection(_connectionString);
        await using var cmd = new SqlCommand(sql, conn);
        BindDetailParams(cmd, detail);
        cmd.Parameters.AddWithValue("@SupplierID", supplierId);

        await conn.OpenAsync(cancellationToken);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SubmitApprovalAsync(int supplierId, string operatorCode, CancellationToken cancellationToken = default)
    {
        const string sql = @"
UPDATE dbo.PC_Suppliers
SET [Status]=1,
    PurchaserCode=@OperatorCode,
    PurchaserPreparedDate=GETDATE()
WHERE SupplierID=@SupplierID";

        await using var conn = new SqlConnection(_connectionString);
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@SupplierID", supplierId);
        cmd.Parameters.AddWithValue("@OperatorCode", operatorCode);

        await conn.OpenAsync(cancellationToken);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
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
        cmd.Parameters.AddWithValue("@IsNew", detail.IsNew);
        AddNullable(cmd, "@CodeOfAcc", detail.CodeOfAcc);
        AddNullable(cmd, "@DeptID", detail.DeptID);
        AddNullable(cmd, "@Status", detail.Status);
    }

    private static void AddNullable(SqlCommand cmd, string name, object? value)
    {
        cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
    }

    private static string? NullIfEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
