using System.Security.Claims;
using System.Text.Json;
using FMS.Domain.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FMS.Infrastructure.Persistence.Interceptors;

public class AuditLogInterceptor : SaveChangesInterceptor
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public AuditLogInterceptor(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        var dbContext = eventData.Context as FmsDbContext;
        if (dbContext == null || dbContext.IsSavingAuditLogs)
            return base.SavingChangesAsync(eventData, result, cancellationToken);

        var auditEntries = CollectAuditEntries(dbContext);

        if (auditEntries.Count > 0)
        {
            // Prevent recursion
            dbContext.IsSavingAuditLogs = true;
            try
            {
                dbContext.AuditLogs.AddRange(auditEntries);
                dbContext.SaveChangesAsync(cancellationToken).GetAwaiter().GetResult();
            }
            finally
            {
                dbContext.IsSavingAuditLogs = false;
            }
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private List<Domain.Entities.AuditLog> CollectAuditEntries(FmsDbContext dbContext)
    {
        var entries = new List<Domain.Entities.AuditLog>();
        var userId = GetUserId();
        var userEmail = GetUserEmail();
        var ipAddress = GetIpAddress();
        var now = DateTime.UtcNow;
        var farmId = GetFarmId(dbContext);

        foreach (var entry in dbContext.ChangeTracker.Entries())
        {
            if (entry.Entity is Domain.Entities.AuditLog)
                continue;

            if (entry.State == EntityState.Detached || entry.State == EntityState.Unchanged)
                continue;

            var entityTypeName = entry.Entity.GetType().Name;

            // Try to get the entity's Id
            var entityIdProp = entry.Properties.FirstOrDefault(p => p.Metadata.Name == "Id");
            var entityId = entityIdProp?.CurrentValue?.ToString() ?? string.Empty;

            // Try to get FarmId from entity
            var farmIdProp = entry.Properties.FirstOrDefault(p => p.Metadata.Name == "FarmId");
            var entityFarmId = farmIdProp?.CurrentValue as Guid? ?? farmId;

            var action = entry.State switch
            {
                EntityState.Added => AuditAction.Create,
                EntityState.Modified => AuditAction.Update,
                EntityState.Deleted => AuditAction.Delete,
                _ => AuditAction.Update
            };

            string? oldValues = null;
            string? newValues = null;

            if (entry.State == EntityState.Modified)
            {
                var changedProps = entry.Properties
                    .Where(p => p.IsModified && !IsSystemProperty(p.Metadata.Name))
                    .ToList();

                if (changedProps.Count > 0)
                {
                    var oldDict = new Dictionary<string, object?>();
                    var newDict = new Dictionary<string, object?>();
                    foreach (var prop in changedProps)
                    {
                        oldDict[prop.Metadata.Name] = prop.OriginalValue;
                        newDict[prop.Metadata.Name] = prop.CurrentValue;
                    }
                    oldValues = JsonSerializer.Serialize(oldDict, _jsonOptions);
                    newValues = JsonSerializer.Serialize(newDict, _jsonOptions);
                }
            }
            else if (entry.State == EntityState.Added)
            {
                var props = entry.Properties
                    .Where(p => p.CurrentValue != null && !IsSystemProperty(p.Metadata.Name))
                    .ToList();

                if (props.Count > 0)
                {
                    var newDict = props.ToDictionary(p => p.Metadata.Name, p => (object?)p.CurrentValue);
                    newValues = JsonSerializer.Serialize(newDict, _jsonOptions);
                }
            }
            else if (entry.State == EntityState.Deleted)
            {
                var props = entry.Properties
                    .Where(p => !IsSystemProperty(p.Metadata.Name))
                    .ToList();

                if (props.Count > 0)
                {
                    var oldDict = props.ToDictionary(p => p.Metadata.Name, p => (object?)p.OriginalValue);
                    oldValues = JsonSerializer.Serialize(oldDict, _jsonOptions);
                }
            }

            entries.Add(new Domain.Entities.AuditLog
            {
                Id = Guid.NewGuid(),
                FarmId = entityFarmId,
                UserId = userId,
                UserEmail = userEmail,
                EntityType = entityTypeName,
                EntityId = entityId,
                Action = action,
                OldValues = oldValues,
                NewValues = newValues,
                Timestamp = now,
                IpAddress = ipAddress
            });
        }

        return entries;
    }

    private static bool IsSystemProperty(string propertyName)
    {
        // Skip navigation properties and system-managed fields
        var systemProps = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Farm", "AnimalType", "Breed", "SexOption", "AgeCategory",
            "AnimalStatus", "Location", "Sire", "Dam", "Account",
            "UserRoles", "RefreshTokens", "UserFarms", "Configurations",
            "Animals", "FeedTypes", "Departments", "EmployeeRoles",
            "Employees", "ExpenseCategories", "PaymentMethods",
            "Expenses", "IncomeCategories", "IncomeRecords",
            "Identifications", "WeightRecords", "Images", "Documents",
            "TimelineEvents", "Transfers", "OffspringAsSire", "OffspringAsDam"
        };
        return systemProps.Contains(propertyName);
    }

    private Guid? GetUserId()
    {
        var value = _httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out var id) ? id : null;
    }

    private string? GetUserEmail()
    {
        return _httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.Email);
    }

    private string? GetIpAddress()
    {
        return _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
    }

    private static Guid? GetFarmId(FmsDbContext dbContext)
    {
        // Try to extract FarmId from any tracked entity that has one
        foreach (var entry in dbContext.ChangeTracker.Entries())
        {
            if (entry.Entity is Domain.Entities.AuditLog) continue;
            var farmIdProp = entry.Properties.FirstOrDefault(p => p.Metadata.Name == "FarmId");
            if (farmIdProp?.CurrentValue is Guid farmId)
                return farmId;
        }
        return null;
    }
}
