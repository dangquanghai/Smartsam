using SmartSam.Models.Purchasing.Supplier;

namespace SmartSam.Services.Purchasing.Supplier.Abstractions;

public interface ISupplierRepository
{
    Task<IReadOnlyList<SupplierLookupOptionDto>> GetDepartmentsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SupplierLookupOptionDto>> GetStatusesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SupplierListRowDto>> SearchAsync(SupplierFilterCriteria criteria, CancellationToken cancellationToken = default);
    Task<SupplierSearchResultDto> SearchPagedAsync(SupplierFilterCriteria criteria, CancellationToken cancellationToken = default);
    Task CopyCurrentSuppliersToYearAsync(int copyYear, IReadOnlyCollection<int> supplierIds, CancellationToken cancellationToken = default);
    Task<SupplierDetailDto?> GetDetailAsync(int supplierId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SupplierApprovalHistoryDto>> GetApprovalHistoryAsync(int supplierId, CancellationToken cancellationToken = default);
    Task<SupplierDetailDto?> GetAnnualDetailAsync(int supplierId, int year, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SupplierApprovalHistoryDto>> GetAnnualApprovalHistoryAsync(int supplierId, int year, CancellationToken cancellationToken = default);
    Task<bool> SupplierCodeExistsAsync(string supplierCode, int? excludeSupplierId = null, CancellationToken cancellationToken = default);
    Task<string> GetSuggestedSupplierCodeAsync(CancellationToken cancellationToken = default);
    Task<int> CreateAsync(SupplierDetailDto detail, string operatorCode, CancellationToken cancellationToken = default);
    Task UpdateAsync(int supplierId, SupplierDetailDto detail, CancellationToken cancellationToken = default);
    Task SubmitApprovalAsync(int supplierId, string operatorCode, CancellationToken cancellationToken = default);
}
