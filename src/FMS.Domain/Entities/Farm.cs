using FMS.Domain.Common;

namespace FMS.Domain.Entities;

public class Farm : SoftDeleteEntity
{
    public Guid AccountId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;

    public Account Account { get; set; } = null!;
    public ICollection<FarmConfiguration> Configurations { get; set; } = new List<FarmConfiguration>();
    public ICollection<AnimalType> AnimalTypes { get; set; } = new List<AnimalType>();
    public ICollection<SexOption> SexOptions { get; set; } = new List<SexOption>();
    public ICollection<AgeCategory> AgeCategories { get; set; } = new List<AgeCategory>();
    public ICollection<AnimalStatus> AnimalStatuses { get; set; } = new List<AnimalStatus>();
    public ICollection<IdentificationType> IdentificationTypes { get; set; } = new List<IdentificationType>();
    public ICollection<LocationType> LocationTypes { get; set; } = new List<LocationType>();
    public ICollection<Location> Locations { get; set; } = new List<Location>();
    public ICollection<CustomFieldDefinition> CustomFieldDefinitions { get; set; } = new List<CustomFieldDefinition>();
    public ICollection<UserFarm> UserFarms { get; set; } = new List<UserFarm>();
    public ICollection<Animal> Animals { get; set; } = new List<Animal>();
    public ICollection<FeedType> FeedTypes { get; set; } = new List<FeedType>();
    public ICollection<Department> Departments { get; set; } = new List<Department>();
    public ICollection<EmployeeRole> EmployeeRoles { get; set; } = new List<EmployeeRole>();
    public ICollection<Employee> Employees { get; set; } = new List<Employee>();
    public ICollection<ExpenseCategory> ExpenseCategories { get; set; } = new List<ExpenseCategory>();
    public ICollection<PaymentMethod> PaymentMethods { get; set; } = new List<PaymentMethod>();
    public ICollection<Expense> Expenses { get; set; } = new List<Expense>();
    public ICollection<IncomeCategory> IncomeCategories { get; set; } = new List<IncomeCategory>();
    public ICollection<IncomeRecord> IncomeRecords { get; set; } = new List<IncomeRecord>();
}
