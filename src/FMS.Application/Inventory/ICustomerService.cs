using FMS.Application.Common;

namespace FMS.Application.Inventory;

public interface ICustomerService
{
    Task<Result<PagedResult<CustomerDto>>> GetCustomersAsync(Guid farmId, CustomerListFilter filter);
    Task<Result<CustomerDto>> GetCustomerAsync(Guid farmId, Guid id);
    Task<Result<CustomerDto>> CreateCustomerAsync(Guid farmId, CreateCustomerRequest request);

    /// <summary>
    /// Validates a batch without writing it. Same rules and same duplicate check as
    /// <see cref="CreateCustomerAsync"/>, reported per row, so a bulk caller can preview
    /// a file and get exactly the answer a commit would give.
    /// </summary>
    Task<Result<BulkCreateResultDto>> ValidateCustomersAsync(Guid farmId, IReadOnlyList<CreateCustomerRequest> requests);

    /// <summary>Creates the whole batch in one SaveChanges, so it is atomic.</summary>
    Task<Result<BulkCreateResultDto>> CreateCustomersAsync(Guid farmId, IReadOnlyList<CreateCustomerRequest> requests);
    Task<Result<CustomerDto>> UpdateCustomerAsync(Guid farmId, Guid id, UpdateCustomerRequest request);
    Task<Result> DeleteCustomerAsync(Guid farmId, Guid id);
    Task<Result<PagedResult<CustomerSaleDto>>> GetSalesAsync(Guid farmId, CustomerSaleListFilter filter);
    Task<Result<CustomerSaleDto>> CreateSaleAsync(Guid farmId, CreateCustomerSaleRequest request);
}
