import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor, renderHook, fireEvent } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { Children, cloneElement, type ComponentProps, type ReactElement, type ReactNode } from 'react';
import i18n, { NAMESPACES, resources } from './index';
import { applyDocumentLocale } from './locale';
import { flatten } from '../../tools/i18n/check-locale-coverage.mjs';
import { useEnumOptions } from './enumOptions';
import AuditLogPage from '../pages/admin/AuditLogPage';
import InventoryMovementsPage from '../pages/inventory/InventoryMovementsPage';
import InventoryReportsPage from '../pages/inventory/InventoryReportsPage';
import FeedTypesPage from '../pages/feed/FeedTypesPage';
import MedicalRecordsPage from '../pages/health/MedicalRecordsPage';
import { auditLogsApi } from '../api/auditLogs';
import { inventoryApi } from '../api/inventory';
import { feedTypesApi, feedInventoryApi } from '../api/feed';
import { medicalRecordsApi } from '../api/health';
import { lookupsApi } from '../api/attendance';
import type { AuditLogEntry } from '../api/auditLogs';
import type { FeedStockMovement, InventoryReport, MedicalRecordListItem, StockMovement } from '../types';
import { useFarmStore } from '../stores/farmStore';
import type { Farm } from '../types';

// jsdom measures every container as zero-width and never advances recharts' animation
// frames, so a ResponsiveContainer would draw nothing and the axis under test would never
// appear. Same approach as BreedingReportsPage.test.tsx: pin the size, drop the animation.
vi.mock('recharts', async (importOriginal) => {
  const actual = await importOriginal<typeof import('recharts')>();
  return {
    ...actual,
    ResponsiveContainer: ({ children }: { children?: ReactNode }) => {
      const child = Children.only(children) as ReactElement<{ width?: number; height?: number }>;
      return cloneElement(child, { width: 400, height: 220 });
    },
    Bar: (props: ComponentProps<typeof actual.Bar>) => <actual.Bar {...props} isAnimationActive={false} />,
  };
});

vi.mock('../api/auditLogs', () => ({ auditLogsApi: { getLogs: vi.fn() } }));
vi.mock('../api/inventory', () => ({ inventoryApi: { list: vi.fn(), movements: vi.fn(), report: vi.fn() } }));
vi.mock('../api/health', () => ({
  medicalRecordsApi: { list: vi.fn(), get: vi.fn(), create: vi.fn(), update: vi.fn(), remove: vi.fn() },
}));
vi.mock('../api/attendance', () => ({ lookupsApi: { animals: vi.fn() } }));
vi.mock('../api/feed', () => ({
  feedTypesApi: { list: vi.fn(), create: vi.fn(), update: vi.fn(), remove: vi.fn() },
  feedInventoryApi: { stock: vi.fn(), movements: vi.fn(), recordMovement: vi.fn() },
}));
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

const feedMovement = (overrides: Partial<FeedStockMovement> = {}): FeedStockMovement =>
  ({
    id: 'fmov-1',
    feedTypeId: 'ft-1',
    feedTypeName: 'Alfalfa',
    movementType: 'Transfer',
    movementTypeName: 'Transfer',
    quantity: 5,
    signedQuantity: 5,
    movementDate: '2026-10-01T12:00:00Z',
    ...overrides,
  }) as FeedStockMovement;

const medicalRecord = (overrides: Partial<MedicalRecordListItem> = {}): MedicalRecordListItem => ({
  id: 'mrec-1',
  animalId: 'animal-1',
  animalTagNumber: 'TAG-001',
  cost: 25,
  dateRecorded: '2026-10-01T12:00:00Z',
  status: 'InProgress',
  statusName: 'InProgress',
  ...overrides,
});

const inventoryReport = (overrides: Partial<InventoryReport> = {}): InventoryReport => ({
  totalStockValue: 0,
  totalItems: 0,
  lowStockItemCount: 0,
  totalMovementQuantity: 0,
  currentStock: [],
  lowStockAlerts: [],
  movementSummary: [{ movementType: 'Transfer', movementTypeName: 'Transfer', movementCount: 1, quantity: 5 }],
  ...overrides,
});

/** Every text node recharts drew, axis ticks included. */
const chartTexts = (): string[] =>
  [...document.querySelectorAll('.recharts-surface text')].map((el) => el.textContent ?? '');

/**
 * Both of FeedTypesPage's loaders run on mount, so opening the stock-movements tab
 * only has to reveal the table the row is already in.
 */
const mockFeed = (items: FeedStockMovement[]) => {
  vi.mocked(feedTypesApi.list).mockResolvedValue([] as never);
  vi.mocked(feedInventoryApi.stock).mockResolvedValue({ data: [] } as never);
  vi.mocked(feedInventoryApi.movements).mockResolvedValue({ data: { items, totalCount: items.length } } as never);
};

/**
 * Opens a tab by its visible label.
 *
 * Addressed by class, not by role: computing an accessible name calls
 * `getComputedStyle`, which this jsdom does not implement for pseudo-elements and
 * which does not return at all on a DOM this size — the by-role query hangs until
 * the test times out rather than failing. Same reason the audit-log filter below
 * addresses antd's option list by class.
 */
const openTab = (label: string) => {
  const tab = [...document.querySelectorAll('.ant-tabs-tab')].find((t) => t.textContent === label);
  expect(tab, `no tab labelled ${label}`).toBeDefined();
  fireEvent.click(tab as HTMLElement);
};

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

describe('the feed types movement column in Spanish', () => {
  it('translates the movement type instead of printing the server wire name', async () => {
    await i18n.changeLanguage('es');
    mockFeed([feedMovement()]);

    const { container } = renderAt(<FeedTypesPage />, '/feed/types');
    openTab(wording('es', 'feed', 'stockMovements'));

    // Anchored on the row's feed type, then asserted synchronously. Waiting on the
    // translated label directly would fail by *timeout* when the bug is present, which
    // says nothing about what is wrong; this way the failure names the offending word.
    await screen.findByText('Alfalfa');
    expect(screen.queryByText('Transfer')).toBeNull();
    expect(screen.getByText(wording('es', 'enums', 'movementType.Transfer'))).toBeInTheDocument();
    expectNoRawKeys(container);
  });
});

describe('the feed types movement column in Arabic', () => {
  it('translates the movement type and lays the document out right-to-left', async () => {
    await i18n.changeLanguage('ar');
    applyDocumentLocale('ar');
    mockFeed([feedMovement({ movementType: 'Purchase', movementTypeName: 'Purchase' })]);

    const { container } = renderAt(<FeedTypesPage />, '/feed/types');
    openTab(wording('ar', 'feed', 'stockMovements'));

    await screen.findByText('Alfalfa');
    expect(screen.queryByText('Purchase')).toBeNull();
    expect(screen.getByText(wording('ar', 'enums', 'movementType.Purchase'))).toBeInTheDocument();
    expect(document.documentElement.dir).toBe('rtl');
    expectNoRawKeys(container);
  });
});

describe('the inventory movements page in Arabic', () => {
  it('translates the movement-type tag of a table row', async () => {
    await i18n.changeLanguage('ar');
    applyDocumentLocale('ar');
    vi.mocked(inventoryApi.list).mockResolvedValue({ data: { items: [], totalCount: 0 } } as never);
    vi.mocked(inventoryApi.movements).mockResolvedValue({ data: { items: [movement()], totalCount: 1 } } as never);

    const { container } = renderAt(<InventoryMovementsPage />, '/dashboard/inventory/movements');

    expect(await screen.findByText(wording('ar', 'enums', 'movementType.Transfer'))).toBeInTheDocument();
    expect(screen.queryByText('Transfer')).toBeNull();
    expectNoRawKeys(container);
  });
});

describe('the medical records status column in Spanish', () => {
  it('translates the server status name rather than printing Open/InProgress/Resolved', async () => {
    await i18n.changeLanguage('es');
    vi.mocked(medicalRecordsApi.list).mockResolvedValue({
      data: { items: [medicalRecord()], totalCount: 1 },
    } as never);
    vi.mocked(lookupsApi.animals).mockResolvedValue({ data: { items: [], totalCount: 0 } } as never);

    const { container } = renderAt(<MedicalRecordsPage />, '/dashboard/health/medical-records');

    // Anchored on the row's animal tag, then asserted synchronously — waiting on the
    // translated label itself would time out rather than name the offending word.
    await screen.findByText('TAG-001');
    expect(screen.queryByText('InProgress')).toBeNull();
    expect(screen.getByText(wording('es', 'enums', 'medicalStatus.InProgress'))).toBeInTheDocument();
    expectNoRawKeys(container);
  });
});

describe('the medical records status column in Arabic', () => {
  it('translates the server status name and lays the document out right-to-left', async () => {
    await i18n.changeLanguage('ar');
    applyDocumentLocale('ar');
    vi.mocked(medicalRecordsApi.list).mockResolvedValue({
      data: { items: [medicalRecord({ status: 'Resolved', statusName: 'Resolved' })], totalCount: 1 },
    } as never);
    vi.mocked(lookupsApi.animals).mockResolvedValue({ data: { items: [], totalCount: 0 } } as never);

    const { container } = renderAt(<MedicalRecordsPage />, '/dashboard/health/medical-records');

    await screen.findByText('TAG-001');
    expect(screen.queryByText('Resolved')).toBeNull();
    expect(screen.getByText(wording('ar', 'enums', 'medicalStatus.Resolved'))).toBeInTheDocument();
    expect(document.documentElement.dir).toBe('rtl');
    expectNoRawKeys(container);
  });
});

describe('the inventory reports movement chart in Spanish', () => {
  it('labels the axis with the translated movement type, not the server wire name', async () => {
    await i18n.changeLanguage('es');
    vi.mocked(inventoryApi.report).mockResolvedValue({ data: inventoryReport() } as never);

    const { container } = renderAt(<InventoryReportsPage />, '/dashboard/inventory/reports');

    // Anchor on the chart having drawn at all — deliberately independent of the
    // translation, so this wait still succeeds when the bug is present and the
    // failure below names the offending word instead of timing out.
    await waitFor(() => expect(chartTexts().length).toBeGreaterThan(0));
    expect(chartTexts()).toContain(wording('es', 'enums', 'movementType.Transfer'));
    expect(chartTexts()).not.toContain('Transfer');
    expectNoRawKeys(container);
  });
});

describe('the inventory reports movement chart in Arabic', () => {
  it('labels the axis with the translated movement type and lays out right-to-left', async () => {
    await i18n.changeLanguage('ar');
    applyDocumentLocale('ar');
    vi.mocked(inventoryApi.report).mockResolvedValue({
      data: inventoryReport({
        movementSummary: [{ movementType: 'Purchase', movementTypeName: 'Purchase', movementCount: 1, quantity: 7 }],
      }),
    } as never);

    const { container } = renderAt(<InventoryReportsPage />, '/dashboard/inventory/reports');

    await waitFor(() => expect(chartTexts().length).toBeGreaterThan(0));
    expect(chartTexts()).toContain(wording('ar', 'enums', 'movementType.Purchase'));
    expect(chartTexts()).not.toContain('Purchase');
    expect(document.documentElement.dir).toBe('rtl');
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
