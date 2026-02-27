namespace SmartSam.Pages.Purchasing.Supplier;

public class SupplierLookupOptionDto
{
    public int Id { get; set; }
    public string CodeOrName { get; set; } = string.Empty;
}

public class SupplierFilterCriteria
{
    public string ViewMode { get; set; } = "current";
    public int? Year { get; set; }
    public int? DeptId { get; set; }
    public string? SupplierCode { get; set; }
    public string? SupplierName { get; set; }
    public string? Business { get; set; }
    public string? Contact { get; set; }
    public int? StatusId { get; set; }
    public bool IsNew { get; set; }
    public int? PageIndex { get; set; }
    public int? PageSize { get; set; }
}

public class SupplierSearchResultDto
{
    public List<SupplierListRowDto> Rows { get; set; } = [];
    public int TotalCount { get; set; }
}

public class SupplierListRowDto
{
    public int SupplierID { get; set; }
    public string SupplierCode { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Mobile { get; set; } = string.Empty;
    public string Fax { get; set; } = string.Empty;
    public string Contact { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;
    public string Business { get; set; } = string.Empty;
    public DateTime? ApprovedDate { get; set; }
    public string Document { get; set; } = string.Empty;
    public string Certificate { get; set; } = string.Empty;
    public string Service { get; set; } = string.Empty;
    public string Comment { get; set; } = string.Empty;
    public bool IsNew { get; set; }
    public string CodeOfAcc { get; set; } = string.Empty;
    public int? DeptID { get; set; }
    public string DeptCode { get; set; } = string.Empty;
    public string SupplierStatusName { get; set; } = string.Empty;
    public int? Status { get; set; }
}

public class SupplierDetailDto
{
    public string? SupplierCode { get; set; }
    public string? SupplierName { get; set; }
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public string? Mobile { get; set; }
    public string? Fax { get; set; }
    public string? Contact { get; set; }
    public string? Position { get; set; }
    public string? Business { get; set; }
    public DateTime? ApprovedDate { get; set; }
    public bool Document { get; set; }
    public string? Certificate { get; set; }
    public string? Service { get; set; }
    public string? Comment { get; set; }
    public bool IsNew { get; set; }
    public string? CodeOfAcc { get; set; }
    public int? DeptID { get; set; }
    public int? Status { get; set; }
}

public class SupplierApprovalHistoryDto
{
    public string Action { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public DateTime? ActionDate { get; set; }
}
