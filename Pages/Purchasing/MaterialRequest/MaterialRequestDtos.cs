using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace SmartSam.Pages.Purchasing.MaterialRequest;

public class MaterialRequestLookupOptionDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class MaterialRequestFilterCriteria
{
    public long? RequestNo { get; set; }
    public int? StoreGroup { get; set; }
    public IReadOnlyList<int>? StatusIds { get; set; }
    public string? ItemCode { get; set; }
    public int? NoIssue { get; set; }
    public bool? IsAuto { get; set; }
    public bool? BuyGreaterThanZero { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public string? AccordingToKeyword { get; set; }
    public int? PageIndex { get; set; }
    public int? PageSize { get; set; }
}

public class MaterialRequestSearchResultDto
{
    public List<MaterialRequestListRowDto> Rows { get; set; } = [];
    public int TotalCount { get; set; }
}

public class MaterialRequestListRowDto
{
    public long RequestNo { get; set; }
    public int? StoreGroup { get; set; }
    public string KPGroupName { get; set; } = string.Empty;
    public DateTime? DateCreate { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public string AccordingTo { get; set; } = string.Empty;
    public bool Approval { get; set; }
    public bool ApprovalEnd { get; set; }
    public bool IsAuto { get; set; }
    public bool PostPr { get; set; }
    public int? MaterialStatusId { get; set; }
    public string MaterialStatusName { get; set; } = string.Empty;
    public int? NoIssue { get; set; }
    public string PrNo { get; set; } = string.Empty;
}

public class MaterialRequestDetailDto
{
    [BindNever]
    public long RequestNo { get; set; }

    [Required(ErrorMessage = "Store Group is required.")]
    public int? StoreGroup { get; set; }

    [Required(ErrorMessage = "Date is required.")]
    public DateTime? DateCreate { get; set; }

    [Required(ErrorMessage = "Description is required.")]
    [StringLength(300, ErrorMessage = "Description must be at most 300 characters.")]
    public string? AccordingTo { get; set; }

    public bool Approval { get; set; }
    public bool ApprovalEnd { get; set; }
    public bool PostPr { get; set; }
    public bool IsAuto { get; set; }

    public DateTime? FromDate { get; set; }

    public DateTime? ToDate { get; set; }

    public int? MaterialStatusId { get; set; }

    [StringLength(50, ErrorMessage = "PR No must be at most 50 characters.")]
    public string? PrNo { get; set; }

    public int? NoIssue { get; set; }
}

public class MaterialRequestLineDto
{
    public int? Id { get; set; }

    [Required(ErrorMessage = "Item Code is required.")]
    [StringLength(20, ErrorMessage = "Item Code must be at most 20 characters.")]
    public string? ItemCode { get; set; }

    [StringLength(150, ErrorMessage = "Item Name must be at most 150 characters.")]
    public string? ItemName { get; set; }

    [StringLength(50, ErrorMessage = "Unit must be at most 50 characters.")]
    public string? Unit { get; set; }

    public decimal? OrderQty { get; set; }
    public decimal? NotReceipt { get; set; }
    public decimal? InStock { get; set; }
    public decimal? AccIn { get; set; }
    public decimal? Buy { get; set; }
    public decimal? Price { get; set; }

    [StringLength(255, ErrorMessage = "Note must be at most 255 characters.")]
    public string? Note { get; set; }

    public bool NewItem { get; set; }
    public bool Selected { get; set; }
}

public class MaterialRequestLineReadonlySnapshotDto
{
    public string ItemCode { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal NotReceipt { get; set; }
    public decimal InStock { get; set; }
    public decimal AccIn { get; set; }
    public decimal Buy { get; set; }
}

public class EmployeeMaterialScopeDto
{
    public int? StoreGroup { get; set; }
    public int ApprovalLevel { get; set; }
    public bool IsHeadDept { get; set; }
}

public class MaterialRequestItemLookupDto
{
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal InStock { get; set; }
    public int? StoreGroupId { get; set; }
}
