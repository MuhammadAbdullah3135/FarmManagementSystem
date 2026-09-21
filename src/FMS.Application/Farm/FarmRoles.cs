namespace FMS.Application.Farm;

/// <summary>
/// The canonical farm-scoped role names.
///
/// These are deliberately the same strings the API enforces through
/// <c>[Authorize(Roles = "…")]</c> (locked down by
/// <c>AuthorizeRoleAttributesTests</c>), so an invitation can only assign a role
/// the rest of the system already understands — no separate farm-role vocabulary
/// that could drift.
///
/// These same strings are what the farm middleware writes into the principal's
/// role claim for a farm-scoped request, so an invited member's farm role — not
/// their account role — is what <c>[Authorize(Roles = "…")]</c> actually
/// enforces. Farm roles therefore drive module access, not just farm
/// administration.
/// </summary>
public static class FarmRoles
{
    public const string SystemOwner = "SystemOwner";
    public const string FarmManager = "FarmManager";
    public const string Veterinarian = "Veterinarian";
    public const string Employee = "Employee";
    public const string Accountant = "Accountant";
    public const string Viewer = "Viewer";

    /// <summary>Every role a farm membership may hold.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        SystemOwner,
        FarmManager,
        Veterinarian,
        Employee,
        Accountant,
        Viewer
    ];

    /// <summary>Roles allowed to administer a farm (settings and membership).</summary>
    public static readonly IReadOnlyList<string> FarmAdmins =
    [
        SystemOwner,
        FarmManager
    ];

    public static bool IsValid(string? role) =>
        role is not null && All.Contains(role);

    public static bool IsFarmAdmin(string? role) =>
        role is not null && FarmAdmins.Contains(role);
}
