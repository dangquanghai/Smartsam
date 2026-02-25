using SmartSam.Models.Purchasing.Supplier;

namespace SmartSam.Services.Purchasing.Supplier.Abstractions;

public interface ISupplierService
{
    Task<IReadOnlyList<SupplierLookupOptionDto>> GetDepartmentsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SupplierLookupOptionDto>> GetStatusesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SupplierListRowDto>> SearchAsync(SupplierFilterCriteria criteria, CancellationToken cancellationToken = default);
    Task<SupplierSearchResultDto> SearchPagedAsync(SupplierFilterCriteria criteria, CancellationToken cancellationToken = default);
    Task CopyCurrentSuppliersToYearAsync(int copyYear, CancellationToken cancellationToken = default);
    Task<SupplierDetailDto?> GetDetailAsync(int supplierId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SupplierApprovalHistoryDto>> GetApprovalHistoryAsync(int supplierId, CancellationToken cancellationToken = default);
    Task<bool> SupplierCodeExistsAsync(string supplierCode, int? excludeSupplierId = null, CancellationToken cancellationToken = default);
    Task<string> GetSuggestedSupplierCodeAsync(CancellationToken cancellationToken = default);
    Task<int> SaveAsync(int? supplierId, SupplierDetailDto detail, string operatorCode, CancellationToken cancellationToken = default);
    Task SubmitApprovalAsync(int supplierId, string operatorCode, CancellationToken cancellationToken = default);
}
