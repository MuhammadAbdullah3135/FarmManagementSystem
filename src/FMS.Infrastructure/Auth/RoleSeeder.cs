using FMS.Domain.Entities;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FMS.Infrastructure.Auth;

public static class RoleSeeder
{
    public static async Task SeedRolesAsync(FmsDbContext context)
    {
        var roles = new[]
        {
            "SystemOwner",
            "FarmManager",
            "Veterinarian",
            "Employee",
            "Accountant",
            "Viewer"
        };

        foreach (var roleName in roles)
        {
            if (!await context.Roles.AnyAsync(r => r.Name == roleName))
            {
                context.Roles.Add(new Role
                {
                    Id = Guid.NewGuid(),
                    Name = roleName,
                    CreatedAt = DateTime.UtcNow
                });
            }
        }

        await context.SaveChangesAsync();
    }
}
