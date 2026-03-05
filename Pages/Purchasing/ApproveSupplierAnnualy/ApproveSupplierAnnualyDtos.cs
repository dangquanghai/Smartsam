namespace SmartSam.Pages.Purchasing.ApproveSupplierAnnualy;

public class ApproveAnnualLookupOptionDto
{
    public int Id { get; set; }
    public string CodeOrName { get; set; } = string.Empty;
}

public class ApproveAnnualFilterCriteria
{
    public int? DeptId { get; set; }
    public int? StatusId { get; set; }
    public int? MaxStatusExclusive { get; set; }
    public int? PageIndex { get; set; }
    public int? PageSize { get; set; }
}

public class ApproveAnnualSearchResultDto
{
    public List<ApproveAnnualListRowDto> Rows { get; set; } = [];
    public int TotalCount { get; set; }
}

public class ApproveAnnualListRowDto
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
    public int? DeptID { get; set; }
    public string DeptCode { get; set; } = string.Empty;
    public string SupplierStatusName { get; set; } = string.Empty;
    public int? Status { get; set; }
}

public class ApproveAnnualSupplierDetailDto
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
    public bool Document { get; set; }
    public string Certificate { get; set; } = string.Empty;
    public string Service { get; set; } = string.Empty;
    public string Comment { get; set; } = string.Empty;
    public bool IsNew { get; set; }
    public string CodeOfAcc { get; set; } = string.Empty;
    public int? DeptID { get; set; }
    public string DeptCode { get; set; } = string.Empty;
    public int? Status { get; set; }
    public string SupplierStatusName { get; set; } = string.Empty;
}

public class ApproveAnnualEmployeeDataScopeDto
{
    public int? DeptID { get; set; }
    public bool CanAsk { get; set; }
    public int? LevelCheckSupplier { get; set; }
}

public class ApproveAnnualPurchaseOrderInfoDto
{
    public int Year { get; set; }
    public int CountPO { get; set; }
    public decimal TotalCost { get; set; }
    public List<ApproveAnnualPurchaseOrderRowDto> Rows { get; set; } = [];
}

public class ApproveAnnualPurchaseOrderRowDto
{
    public int PO_IndexDetailID { get; set; }
    public string IndexName { get; set; } = string.Empty;
    public string SubIndex { get; set; } = string.Empty;
    public int TheTime { get; set; }
    public decimal Point { get; set; }
}

public class ApproveAnnualSupplierServiceRowDto
{
    public DateTime? TheDate { get; set; }
    public string Comment { get; set; } = string.Empty;
    public int? ThePoint { get; set; }
    public int? WarrantyOrService { get; set; }
    public string UserCode { get; set; } = string.Empty;
}
