import { describe, expect, it } from 'vitest';
import {
  APP_ROLES,
  MODULE_ROLES,
  canAccessModule,
  filterMenuByRole,
} from './permissions';
import { appMenuItems } from '../components/AppLayout';

/*
 * The role matrix mirrors the API's [Authorize(Roles = "…")] attributes
 * (asserted on the backend by AuthorizeRoleAttributesTests). These tests pin
 * the frontend half: which modules each role may see in the menu.
 */

const groupKeysFor = (roles: string[]): string[] =>
  filterMenuByRole(appMenuItems, roles).map((item) => item.key);

const childKeysFor = (roles: string[], groupKey: string): string[] =>
  filterMenuByRole(appMenuItems, roles)
    .find((item) => item.key === groupKey)!
    .children!.map((child) => child.key);

describe('canAccessModule', () => {
  it('treats modules without a role rule as open to everyone', () => {
    expect(canAccessModule(undefined, 'dashboard')).toBe(true);
    expect(canAccessModule(['Viewer'], 'dashboard')).toBe(true);
  });

  it('denies restricted modules to roles that lack them', () => {
    expect(canAccessModule(['Viewer'], 'admin.audit-log')).toBe(false);
    expect(canAccessModule(['Viewer'], 'inventory.items')).toBe(false);
    expect(canAccessModule(['Veterinarian'], 'inventory.suppliers')).toBe(false);
    expect(canAccessModule(undefined, 'admin.audit-log')).toBe(false);
    expect(canAccessModule([], 'inventory.items')).toBe(false);
  });

  it('allows restricted modules to the roles the API permits', () => {
    expect(canAccessModule(['Accountant'], 'admin.audit-log')).toBe(true);
  });

  /*
   * The job-status view is account-scoped infrastructure (GET /api/admin/jobs),
   * not farm data, so only the account-level SystemOwner role may reach it — a
   * farm role never grants it.
   */
  it('restricts the scheduled jobs view to a SystemOwner account', () => {
    expect(canAccessModule(['SystemOwner'], 'admin.jobs')).toBe(true);
    expect(canAccessModule(['FarmManager'], 'admin.jobs')).toBe(false);
    expect(canAccessModule(['Accountant'], 'admin.jobs')).toBe(false);
    expect(canAccessModule(undefined, 'admin.jobs')).toBe(false);
    expect(canAccessModule(['SystemOwner'], 'inventory.suppliers')).toBe(true);
    expect(canAccessModule(['Veterinarian'], 'inventory.items')).toBe(true);
  });
});

describe('MODULE_ROLES', () => {
  it('only names roles from the authoritative list', () => {
    for (const roles of Object.values(MODULE_ROLES)) {
      for (const role of roles) {
        expect(APP_ROLES).toContain(role);
      }
    }
  });

  it('does not grant Viewer any restricted module', () => {
    for (const roles of Object.values(MODULE_ROLES)) {
      expect(roles).not.toContain('Viewer');
    }
  });
});

describe('appMenuItems role filtering', () => {
  it('hides the Admin and Inventory groups from a Viewer', () => {
    const keys = groupKeysFor(['Viewer']);

    expect(keys).toContain('/dashboard');
    expect(keys).toContain('animals');
    expect(keys).not.toContain('admin');
    expect(keys).not.toContain('inventory');
  });

  it('keeps Audit Log for an Accountant but hides Inventory', () => {
    const keys = groupKeysFor(['Accountant']);

    expect(keys).toContain('admin');
    expect(keys).not.toContain('inventory');
  });

  it('keeps Inventory for a Veterinarian but drops its supplier and customer entries', () => {
    const keys = childKeysFor(['Veterinarian'], 'inventory');

    expect(keys).toContain('/dashboard/inventory/items');
    expect(keys).toContain('/dashboard/inventory/movements');
    expect(keys).toContain('/dashboard/inventory/reports');
    expect(keys).not.toContain('/dashboard/inventory/suppliers');
    expect(keys).not.toContain('/dashboard/inventory/customers');
  });

  it('shows the full restricted surface to a FarmManager', () => {
    const keys = groupKeysFor(['FarmManager']);

    expect(keys).toContain('admin');
    expect(keys).toContain('inventory');
    expect(childKeysFor(['FarmManager'], 'inventory')).toContain('/dashboard/inventory/suppliers');
  });

  it('leaves the unrestricted modules untouched for every role', () => {
    const unrestricted = ['/dashboard', 'feed', 'animals', 'breeding', 'hr', 'finance', 'health', 'reports', 'configuration'];

    for (const role of APP_ROLES) {
      expect(groupKeysFor([role])).toEqual(expect.arrayContaining(unrestricted));
    }
  });
});
