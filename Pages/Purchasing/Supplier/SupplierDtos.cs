using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;

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
    [Required(ErrorMessage = "Supplier code is required.")]
    [StringLength(10, ErrorMessage = "Supplier code must be at most 10 characters.")]
    public string? SupplierCode { get; set; }

    [Required(ErrorMessage = "Supplier name is required.")]
    [StringLength(254, ErrorMessage = "Supplier name must be at most 254 characters.")]
    public string? SupplierName { get; set; }

    [Required(ErrorMessage = "Address is required.")]
    [StringLength(254, ErrorMessage = "Address must be at most 254 characters.")]
    public string? Address { get; set; }

    [StringLength(20, ErrorMessage = "Phone must be at most 20 characters.")]
    public string? Phone { get; set; }

    [StringLength(20, ErrorMessage = "Mobile must be at most 20 characters.")]
    public string? Mobile { get; set; }

    [StringLength(20, ErrorMessage = "Fax must be at most 20 characters.")]
    public string? Fax { get; set; }

    [StringLength(40, ErrorMessage = "Contact person must be at most 40 characters.")]
    public string? Contact { get; set; }

    [StringLength(40, ErrorMessage = "Position must be at most 40 characters.")]
    public string? Position { get; set; }

    [StringLength(1000, ErrorMessage = "Business must be at most 1000 characters.")]
    public string? Business { get; set; }

    [BindNever]
    public DateTime? ApprovedDate { get; set; }

    public bool Document { get; set; }

    [StringLength(100, ErrorMessage = "Certificate must be at most 100 characters.")]
    public string? Certificate { get; set; }

    [StringLength(1000, ErrorMessage = "Service must be at most 1000 characters.")]
    public string? Service { get; set; }

    [StringLength(1000, ErrorMessage = "Comment must be at most 1000 characters.")]
    public string? Comment { get; set; }

    public bool IsNew { get; set; }

    [StringLength(20, ErrorMessage = "CodeOfAcc must be at most 20 characters.")]
    public string? CodeOfAcc { get; set; }

    [Required(ErrorMessage = "Department is required.")]
    public int? DeptID { get; set; }

    [BindNever]
    public int? Status { get; set; }
}

public class SupplierApprovalHistoryDto
{
    public string Action { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public DateTime? ActionDate { get; set; }
}

public class EmployeeDataScopeDto
{
    public int? DeptID { get; set; }
    public bool SeeDataAllDept { get; set; }
}
