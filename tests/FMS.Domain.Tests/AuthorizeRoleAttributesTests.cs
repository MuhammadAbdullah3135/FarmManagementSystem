using System.Reflection;
using FMS.API.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FMS.Domain.Tests;

/// <summary>
/// The API's role system is expressed entirely by <c>[Authorize(Roles = "…")]</c>
/// attributes; there is no runtime permission catalogue. The React menu filter
/// (`client/src/utils/permissions.ts`) mirrors this set, so a controller whose
/// roles change must fail here and prompt the frontend map to be updated in the
/// same change instead of drifting silently.
/// </summary>
public class AuthorizeRoleAttributesTests
{
    /// <summary>Every role string the API actually enforces. Sorted for stable comparison.</summary>
    private static readonly string[] ExpectedRoles =
    [
        "Accountant",
        "Employee",
        "FarmManager",
        "SystemOwner",
        "Veterinarian",
    ];

    [Fact]
    public void ControllerRoleAttributes_MatchDocumentedSet()
    {
        Assert.Equal(ExpectedRoles, EnforcedRoles().ToArray());
    }

    [Fact]
    public void ViewerRole_IsNeverGrantedARestrictedModule()
    {
        // Viewer is seeded by RoleSeeder but intentionally absent from every
        // [Authorize(Roles = …)] attribute — it is a read-only role.
        Assert.DoesNotContain("Viewer", EnforcedRoles());
    }

    private static IOrderedEnumerable<string> EnforcedRoles() =>
        ApiAssembly
            .GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .SelectMany(RoleStringsOn)
            .Distinct()
            .OrderBy(role => role, StringComparer.Ordinal);

    private static IEnumerable<string> RoleStringsOn(Type controller) =>
        ClassRoles(controller).Concat(controller
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .SelectMany(method => method.GetCustomAttributes<AuthorizeAttribute>(inherit: true))
            .SelectMany(RolesOf));

    private static IEnumerable<string> ClassRoles(Type controller) =>
        controller
            .GetCustomAttributes<AuthorizeAttribute>(inherit: true)
            .SelectMany(RolesOf);

    private static IEnumerable<string> RolesOf(AuthorizeAttribute attribute) =>
        string.IsNullOrWhiteSpace(attribute.Roles)
            ? []
            : attribute.Roles!.Split(
                ',',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static Assembly ApiAssembly => typeof(DashboardController).Assembly;
}
