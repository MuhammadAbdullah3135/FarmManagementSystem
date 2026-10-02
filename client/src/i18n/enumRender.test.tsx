import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor, renderHook } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import i18n, { NAMESPACES, resources } from './index';
import { applyDocumentLocale } from './locale';
import { flatten } from '../../tools/i18n/check-locale-coverage.mjs';
import { useEnumOptions } from './enumOptions';
import AuditLogPage from '../pages/admin/AuditLogPage';
import InventoryMovementsPage from '../pages/inventory/InventoryMovementsPage';
import { auditLogsApi } from '../api/auditLogs';
import { inventoryApi } from '../api/inventory';
import type { AuditLogEntry } from '../api/auditLogs';
import type { StockMovement } from '../types';
import { useFarmStore } from '../stores/farmStore';
import type { Farm } from '../types';

vi.mock('../api/auditLogs', () => ({ auditLogsApi: { getLogs: vi.fn() } }));
vi.mock('../api/inventory', () => ({ inventoryApi: { list: vi.fn(), movements: vi.fn() } }));
vi.mock('../api/farmApi', () => ({ getApiError: vi.fn((error: unknown) => String(error)) }));

/**
 * Real pages, rendered in Spanish and Arabic, to pin what the enum migration
 * actually changed for a user:
 *
 *   1. **No leak** — the option lists and status tags show the translated label,
 *      and the English enum word is not on screen beside it;
 *   2. **No raw key** — the generic scan every render test does;
 *   3. **The wire value survives the language** (the acceptance criterion the
 *      whole mechanism exists for): a filter displays `Crear` and still sends
 *      `action: Create` to the API.
 *
 * Only the pages' API modules are mocked; the pages are the real components.
 */

type FlatBundle = Record<string, string>;

const flat = (locale: 'en' | 'es' | 'ar', namespace: string): FlatBundle =>
  flatten((resources as unknown as Record<string, Record<string, unknown>>)[locale][namespace]);

const wording = (locale: 'en' | 'es' | 'ar', namespace: string, key: string): string =>
  flat(locale, namespace)[key];

const KEY_IDS: string[] = NAMESPACES.flatMap((namespace) =>
  Object.keys(flat('en', namespace)).map((key) => `${namespace}:${key}`),
);

const textNodes = (container: HTMLElement): string[] => {
  const out: string[] = [];
  for (const element of container.querySelectorAll('*')) {
    for (const child of Array.from(element.childNodes)) {
      if (child.nodeType === Node.TEXT_NODE) {
        const text = (child.textContent ?? '').trim();
        if (text) out.push(text);
      }
    }
  }
  return out;
};

const expectNoRawKeys = (container: HTMLElement) => {
  const leaked = textNodes(container).filter((text) => KEY_IDS.some((id) => text.includes(id)));
  expect(leaked).toEqual([]);
};

const farm: Farm = {
  id: 'farm-a',
  name: 'Green Acres',
  isActive: true,
  createdAt: '2026-01-01T00:00:00Z',
  userFarmRole: 'SystemOwner',
};

const auditEntry = (overrides: Partial<AuditLogEntry> = {}): AuditLogEntry =>
  ({
    id: 'log-1',
    farmId: farm.id,
    userEmail: 'owner@example.com',
    entityType: 'Animal',
    entityId: 'animal-1',
    action: 'Create',
    actionDisplay: 'Create',
    oldValues: null,
    newValues: null,
    timestamp: '2026-10-01T12:00:00Z',
    ipAddress: null,
    ...overrides,
  }) as AuditLogEntry;

const movement = (overrides: Partial<StockMovement> = {}): StockMovement =>
  ({
    id: 'mov-1',
    movementDate: '2026-10-01T12:00:00Z',
    inventoryItemName: 'Dewormer',
    movementTypeName: 'Transfer',
    signedQuantity: 5,
    unit: 'bottle',
    reason: 'Moved to the barn',
    ...overrides,
  }) as StockMovement;

const renderAt = (element: React.ReactElement, path: string) =>
  render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path={path} element={element} />
      </Routes>
    </MemoryRouter>,
  );

beforeEach(() => {
  vi.clearAllMocks();
  useFarmStore.setState({ farms: [farm], activeFarm: farm, isLoading: false, error: null });
});

afterEach(async () => {
  // The next test file must not inherit a non-English i18next instance.
  applyDocumentLocale('en');
  await i18n.changeLanguage('en');
});

describe('the audit log in Spanish', () => {
  it('shows translated actions and a localized total, with no English enum word left', async () => {
    await i18n.changeLanguage('es');
    vi.mocked(auditLogsApi.getLogs).mockResolvedValue({
      data: { items: Array.from({ length: 8 }, (_, i) => auditEntry({ id: `log-${i}`, entityType: i === 0 ? 'WeightRecord' : 'Animal' })), totalCount: 8 },
    } as never);

    const { container } = renderAt(<AuditLogPage />, '/dashboard/admin/audit-log');

    // Every action tag reads Crear, and the raw wire word is nowhere on screen.
    expect((await screen.findAllByText(wording('es', 'enums', 'auditAction.Create'))).length).toBeGreaterThan(0);
    expect(screen.queryByText('Create')).toBeNull();

    // The entity type of the first row is translated too — a server class name, and
    // none of them survive to the screen.
expect((await screen.findAllByText(wording('es', 'enums', 'auditEntityType.WeightRecord'))).length).toBeGreaterThan(0);
expect(screen.queryByText('WeightRecord')).toBeNull();

    // The pagination total is the localized sentence, not "Total N records".
    expect(await screen.findByText('Total 8 registros')).toBeInTheDocument();
    expect(screen.queryByText('Total 8 records')).toBeNull();

    expectNoRawKeys(container);
  });

  it('displays the Spanish label in the filter and still sends the English wire value', async () => {
    await i18n.changeLanguage('es');
    vi.mocked(auditLogsApi.getLogs).mockResolvedValue({
      data: { items: [], totalCount: 0 },
    } as never);

    renderAt(<AuditLogPage />, '/dashboard/admin/audit-log');
    const user = userEvent.setup();

    // The action filter is the second Select on the page (entity type comes first);
    // antd's internals vary enough that source order is the stable address.
    const selects = document.querySelectorAll('.ant-select');
    expect(selects.length).toBe(2);
    const actionSelect = selects[1] as HTMLElement;
    await user.click(actionSelect);

    // The dropdown offers the Spanish label — and choosing it submits `Create`.
    // (antd's virtual list marks only the active item role="option", so the list
    // items are addressed by their class, which the probe-verified DOM always has.)
    const crear = await waitFor(() => {
      const option = [...document.querySelectorAll('.ant-select-item-option')].find(
        (o) => o.textContent === wording('es', 'enums', 'auditAction.Create'),
      );
      expect(option).toBeDefined();
      return option as HTMLElement;
    });
    await user.click(crear);

    await waitFor(() =>
      expect(auditLogsApi.getLogs).toHaveBeenLastCalledWith(
        farm.id,
        expect.objectContaining({ action: 'Create' }),
      ),
    );
    // The closed select shows the Spanish label the user chose.
    await waitFor(() => expect(actionSelect.textContent).toContain(wording('es', 'enums', 'auditAction.Create')));
  });
});

describe('the audit log in Arabic', () => {
  it('renders translated actions with the dual total form, and the document is right-to-left', async () => {
    await i18n.changeLanguage('ar');
    applyDocumentLocale('ar');
    vi.mocked(auditLogsApi.getLogs).mockResolvedValue({
      data: { items: [auditEntry(), auditEntry({ id: 'log-2' })], totalCount: 2 },
    } as never);

    const { container } = renderAt(<AuditLogPage />, '/dashboard/admin/audit-log');

    expect((await screen.findAllByText(wording('ar', 'enums', 'auditAction.Create'))).length).toBeGreaterThan(0);
    expect(screen.queryByText('Create')).toBeNull();

    // Two records select Arabic's dual form — the plural category English has no key for.
    expect(await screen.findByText(wording('ar', 'admin', 'totalRecords_two'))).toBeInTheDocument();

    expect(document.documentElement.dir).toBe('rtl');
    expect(document.documentElement.lang).toBe('ar');
    expectNoRawKeys(container);
  });
});

describe('the inventory movements page in Spanish', () => {
  it('translates the movement-type tag of a table row', async () => {
    await i18n.changeLanguage('es');
    vi.mocked(inventoryApi.list).mockResolvedValue({
      data: { items: [], totalCount: 0 },
    } as never);
    vi.mocked(inventoryApi.movements).mockResolvedValue({
      data: { items: [movement()], totalCount: 1 },
    } as never);

    const { container } = renderAt(<InventoryMovementsPage />, '/dashboard/inventory/movements');

    // The row's tag reads Transferencia; the exact English word is gone.
    // (An exact-text query deliberately does not match the longer Spanish word.)
    expect(await screen.findByText(wording('es', 'enums', 'movementType.Transfer'))).toBeInTheDocument();
    expect(screen.queryByText('Transfer')).toBeNull();
    expectNoRawKeys(container);
  });
});

describe('the shared mechanism offers wire values, whatever the language', () => {
  it('offers the numeric breeding wire values with Spanish labels', async () => {
    await i18n.changeLanguage('es');
    const { result } = renderHook(() => useEnumOptions('breedingMethod'));
    expect(result.current.map((option) => option.value)).toEqual([0, 1]);
    expect(result.current.map((option) => option.label)).toEqual([
      wording('es', 'enums', 'breedingMethod.natural'),
      wording('es', 'enums', 'breedingMethod.artificialInsemination'),
    ]);
  });

  it('offers the English task wire values with Spanish labels', async () => {
    await i18n.changeLanguage('es');
    const { result } = renderHook(() => useEnumOptions('taskStatus'));
    expect(result.current.map((option) => option.value)).toEqual([
      'Pending',
      'InProgress',
      'Completed',
      'Cancelled',
    ]);
    expect(result.current.map((option) => option.label)).not.toContain('Pending');
  });
});
