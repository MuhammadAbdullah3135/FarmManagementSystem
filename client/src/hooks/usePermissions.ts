import { useAuthStore } from '../stores/authStore';
import { useFarmStore } from '../stores/farmStore';
import { canAccessModule } from '../utils/permissions';

export interface Permissions {
  /**
   * The role(s) that govern the current context.
   *
   * When a farm is active this is the caller's farm-scoped role
   * (`activeFarm.userFarmRole`) — the same value the API enforces on farm-scoped
   * routes. Before a farm is selected it falls back to the account roles returned
   * by the API on login/register.
   */
  roles: string[];
  /** True when any of the current roles may access the given module. */
  canAccess: (module: string) => boolean;
}

/**
 * Capability check for pages and components, so role-string comparisons live in
 * one place instead of being scattered across the UI.
 *
 * The active farm's membership role is authoritative for farm-scoped decisions
 * (mirroring the backend, where FarmContextMiddleware swaps the role claim to the
 * farm role before authorization). Because the value is read from the store, the
 * menu recomputes automatically when the user switches farms.
 *
 * Usage: `const { canAccess } = usePermissions(); if (canAccess('inventory.items')) { … }`
 */
export const usePermissions = (): Permissions => {
  const farmRole = useFarmStore((state) => state.activeFarm?.userFarmRole);
  const accountRoles = useAuthStore((state) => state.user?.roles) ?? [];

  // The farm role wins whenever a farm is active; otherwise fall back to the
  // account roles so the pre-selection UI behaves as before.
  const roles = farmRole ? [farmRole] : accountRoles;

  return {
    roles,
    canAccess: (module: string) => canAccessModule(roles, module),
  };
};
