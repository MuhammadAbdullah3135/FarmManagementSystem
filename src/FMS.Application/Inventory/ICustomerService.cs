using FMS.Application.Common;

namespace FMS.Application.Inventory;

public interface ICustomerService
{
    Task<Result<PagedResult<CustomerDto>>> GetCustomersAsync(Guid farmId, CustomerListFilter filter);
    Task<Result<CustomerDto>> GetCustomerAsync(Guid farmId, Guid id);
    Task<Result<CustomerDto>> CreateCustomerAsync(Guid farmId, CreateCustomerRequest request);
    Task<Result<CustomerDto>> UpdateCustomerAsync(Guid farmId, Guid id, UpdateCustomerRequest request);
    Task<Result> DeleteCustomerAsync(Guid farmId, Guid id);
    Task<Result<PagedResult<CustomerSaleDto>>> GetSalesAsync(Guid farmId, CustomerSaleListFilter filter);
    Task<Result<CustomerSaleDto>> CreateSaleAsync(Guid farmId, CreateCustomerSaleRequest request);
}
