import type { ReactNode } from 'react';

/**
 * Role-based UI capability rules.
 *
 * The authoritative role strings come from the API's `[Authorize(Roles = "…")]`
 * attributes. The backend `AuthorizeRoleAttributesTests` reflects over the
 * controllers and fails if that set changes, so a divergence here shows up as a
 * red test rather than a silently wrong menu.
 *
 * The API is always the enforcement point — hiding a menu entry is a UX
 * affordance, not a security boundary.
 */
export const APP_ROLES = [
  'SystemOwner',
  'FarmManager',
  'Veterinarian',
  'Employee',
  'Accountant',
  'Viewer',
] as const;

export type AppRole = (typeof APP_ROLES)[number];

/**
 * Modules whose whole controller is role-restricted at class level. Every other
 * module is readable by any authenticated user, so it is intentionally absent
 * and therefore always allowed.
 *
 * Keep each entry in sync with the controller cited beside it.
 */
export const MODULE_ROLES: Record<string, readonly AppRole[]> = {
  // src/FMS.API/Controllers/InventoryItemsController.cs:10
  'inventory.items': ['Employee', 'Veterinarian', 'FarmManager', 'SystemOwner'],
  // src/FMS.API/Controllers/SuppliersController.cs:10
  'inventory.suppliers': ['Employee', 'FarmManager', 'SystemOwner'],
  // src/FMS.API/Controllers/CustomersController.cs:10
  'inventory.customers': ['Employee', 'FarmManager', 'SystemOwner'],
  // src/FMS.API/Controllers/AuditLogsController.cs:11
  'admin.audit-log': ['SystemOwner', 'FarmManager', 'Accountant'],
  // src/FMS.API/Controllers/AdminJobsController.cs:20 — account-scoped, so this
  // intentionally follows the account role rather than a farm role.
  'admin.jobs': ['SystemOwner'],
  // Enforced per-farm by FarmMembershipService (FarmRoles.IsFarmAdmin). Farm
  // roles are now authoritative for farm-scoped UI (usePermissions reads
  // activeFarm.userFarmRole), so this entry follows the member's farm role.
  // src/FMS.Infrastructure/Farm/FarmMembershipService.cs
  'farm.members': ['SystemOwner', 'FarmManager'],
  // src/FMS.API/Controllers/FarmExportController.cs:34
  // The archive is every record in the farm at once — finance, payroll, health —
  // so it sits behind the same farm-admin boundary as member management.
  'data.export': ['SystemOwner', 'FarmManager'],
};

/**
 * True when any of the user's roles may access the module. Modules not listed in
 * {@link MODULE_ROLES} are open to every authenticated user.
 */
export const canAccessModule = (
  roles: readonly string[] | undefined,
  module: string,
): boolean => {
  const allowed = MODULE_ROLES[module];
  if (!allowed) return true;
  if (!roles || roles.length === 0) return false;
  return roles.some((role) => (allowed as readonly string[]).includes(role));
};

/**
 * A navigation entry before it is handed to antd. `requiredModule` links the
 * entry to a {@link MODULE_ROLES} key; entries without one are visible to all.
 *
 * The tree is deliberately language-free: it carries a `labelKey` into the `nav`
 * namespace (e.g. `nav:tasks`) and the caller resolves it, so the same structure
 * serves every locale and the role filter can be reasoned about without strings.
 */
export interface AppMenuItem {
  key: string;
  labelKey: string;
  label?: ReactNode;
  icon?: ReactNode;
  requiredModule?: string;
  children?: AppMenuItem[];
}

/**
 * Returns a copy of the menu containing only the entries the roles may access.
 * Groups whose children are all hidden are dropped too, so a user never sees an
 * empty parent menu.
 */
export const filterMenuByRole = (
  items: AppMenuItem[],
  roles: readonly string[] | undefined,
): AppMenuItem[] =>
  items.reduce<AppMenuItem[]>((visible, item) => {
    if (item.requiredModule && !canAccessModule(roles, item.requiredModule)) {
      return visible;
    }

    if (item.children && item.children.length > 0) {
      const children = filterMenuByRole(item.children, roles);
      if (children.length === 0) return visible;
      visible.push({ ...item, children });
      return visible;
    }

    visible.push(item);
    return visible;
  }, []);
