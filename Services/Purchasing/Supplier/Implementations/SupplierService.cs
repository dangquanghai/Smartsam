using SmartSam.Models.Purchasing.Supplier;
using SmartSam.Services.Purchasing.Supplier.Abstractions;

namespace SmartSam.Services.Purchasing.Supplier.Implementations;

public class SupplierService : ISupplierService
{
    private readonly ISupplierRepository _repository;

    public SupplierService(ISupplierRepository repository)
    {
        _repository = repository;
    }

    public Task<IReadOnlyList<SupplierLookupOptionDto>> GetDepartmentsAsync(CancellationToken cancellationToken = default)
        => _repository.GetDepartmentsAsync(cancellationToken);

    public Task<IReadOnlyList<SupplierLookupOptionDto>> GetStatusesAsync(CancellationToken cancellationToken = default)
        => _repository.GetStatusesAsync(cancellationToken);

    public Task<IReadOnlyList<SupplierListRowDto>> SearchAsync(SupplierFilterCriteria criteria, CancellationToken cancellationToken = default)
        => _repository.SearchAsync(criteria, cancellationToken);

    public Task CopyCurrentSuppliersToYearAsync(int copyYear, CancellationToken cancellationToken = default)
        => _repository.CopyCurrentSuppliersToYearAsync(copyYear, cancellationToken);

    public Task<SupplierDetailDto?> GetDetailAsync(int supplierId, CancellationToken cancellationToken = default)
        => _repository.GetDetailAsync(supplierId, cancellationToken);

    public Task<IReadOnlyList<SupplierApprovalHistoryDto>> GetApprovalHistoryAsync(int supplierId, CancellationToken cancellationToken = default)
        => _repository.GetApprovalHistoryAsync(supplierId, cancellationToken);

    public async Task<int> SaveAsync(int? supplierId, SupplierDetailDto detail, string operatorCode, CancellationToken cancellationToken = default)
    {
        if (supplierId.HasValue && supplierId.Value > 0)
        {
            await _repository.UpdateAsync(supplierId.Value, detail, cancellationToken);
            return supplierId.Value;
        }

        return await _repository.CreateAsync(detail, operatorCode, cancellationToken);
    }

    public Task SubmitApprovalAsync(int supplierId, string operatorCode, CancellationToken cancellationToken = default)
        => _repository.SubmitApprovalAsync(supplierId, operatorCode, cancellationToken);
}
