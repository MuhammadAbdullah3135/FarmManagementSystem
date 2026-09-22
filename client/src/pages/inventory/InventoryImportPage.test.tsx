import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import InventoryImportPage from './InventoryImportPage';
import { inventoryImportApi } from '../../api/inventoryImport';
import type { ImportPreview, ImportRow } from '../../api/importApi';
import { exportCsv } from '../../utils/export';

vi.mock('../../api/inventoryImport', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../api/inventoryImport')>();
  return {
    ...actual,
    inventoryImportApi: { preview: vi.fn(), commit: vi.fn() },
  };
});

vi.mock('../../utils/export', () => ({
  exportCsv: vi.fn(),
  exportExcel: vi.fn(),
  exportPdf: vi.fn(),
}));

const csvFile = (name = 'items.csv') =>
  new File(['name,unit\nDairy meal,kg\n'], name, { type: 'text/csv' });

const row = (overrides: Partial<ImportRow> = {}): ImportRow => ({
  rowNumber: 5,
  isValid: false,
  errors: [{ field: 'name', message: "An inventory item with this name already exists" }],
  values: { name: 'Dairy meal' },
  ...overrides,
});

const preview = (overrides: Partial<ImportPreview> = {}): ImportPreview => ({
  fields: [
    { key: 'name', label: 'Item name', required: true, hint: "Unique within the farm: this is the item's identifier" },
    { key: 'unit', label: 'Unit', required: true, hint: 'e.g. kg, litre, bag' },
    { key: 'quantity', label: 'Quantity', required: false, hint: 'Opening stock' },
  ],
  headers: ['name', 'unit'],
  mapping: { fields: { name: { column: 0 }, unit: { column: 1 } }, dateFormat: null },
  suggestedMapping: { fields: { name: { column: 0 } } },
  // Inventory resolves nothing against farm configuration, so there are no fixed-value
  // options to offer — the same empty map the server sends.
  lookups: {},
  totalRows: 2,
  validRowCount: 2,
  invalidRowCount: 0,
  truncated: false,
  invalidRows: [],
  sampleValidRows: [
    { rowNumber: 2, isValid: true, errors: [], values: { name: 'Dairy meal', unit: 'kg' } },
  ],
  ...overrides,
});

const renderPage = () =>
  render(
    <MemoryRouter>
      <InventoryImportPage />
    </MemoryRouter>,
  );

const uploadFile = async (container: HTMLElement) => {
  const input = container.querySelector('input[type="file"]');
  if (!input) throw new Error('file input not found');
  await userEvent.upload(input as HTMLInputElement, csvFile());
};

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(inventoryImportApi.preview).mockResolvedValue({ data: preview() } as never);
  vi.mocked(inventoryImportApi.commit).mockResolvedValue({
    data: { totalRows: 2, importedCount: 2, truncated: false, invalidRows: [] },
  } as never);
});

describe('InventoryImportPage', () => {
  it('uses the shared wizard, named for inventory items', async () => {
    renderPage();

    expect(screen.getByText('Import inventory items')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Download CSV template/ })).toBeInTheDocument();
  });

  it('calls the inventory endpoint, not another entity’s', async () => {
    const { container } = renderPage();

    await uploadFile(container);

    await waitFor(() => expect(inventoryImportApi.preview).toHaveBeenCalledTimes(1));
    expect(vi.mocked(inventoryImportApi.preview).mock.calls[0][1]).toBeUndefined();
  });

  it('counts inventory items, not animals', async () => {
    const { container } = renderPage();
    await uploadFile(container);
    await screen.findByRole('button', { name: /Check file/ });
    await userEvent.click(screen.getByRole('button', { name: /Check file/ }));

    await userEvent.click(await screen.findByRole('button', { name: /Import 2 inventory item/ }));

    // Same sentence, two roots: the antd toast and the result step's Alert. Assert the alert, so
    // the check cannot depend on whether the toast has committed or expired yet.
    const resultAlert = (
      await screen.findByText(
        /Every inventory item in the file was created exactly as the add form would have created it/,
      )
    ).closest('[role="alert"]');
    expect(resultAlert).toHaveTextContent('Imported 2 inventory item(s)');
    expect(screen.getByRole('button', { name: 'View inventory' })).toBeInTheDocument();
  });

  it('shows the duplicate-name error exactly as the server words it', async () => {
    vi.mocked(inventoryImportApi.commit).mockResolvedValue({
      data: { totalRows: 2, importedCount: 0, truncated: false, invalidRows: [row()] },
    } as never);

    const { container } = renderPage();
    await uploadFile(container);
    await screen.findByRole('button', { name: /Check file/ });
    await userEvent.click(screen.getByRole('button', { name: /Check file/ }));
    await userEvent.click(await screen.findByRole('button', { name: /Import 2 inventory item/ }));

    expect(await screen.findByText('Nothing was imported')).toBeInTheDocument();
    expect(screen.getByText('An inventory item with this name already exists')).toBeInTheDocument();
    // The row number is reported, so the user can find it in their own file.
    expect(screen.getByRole('cell', { name: '5' })).toBeInTheDocument();
  });

  it('offers a template built from the inventory importer’s own headers', async () => {
    renderPage();

    await userEvent.click(screen.getByRole('button', { name: /Download CSV template/ }));

    const [filename, headers, rows] = vi.mocked(exportCsv).mock.calls[0];
    expect(filename).toBe('inventory-import-template');
    expect(headers).toContain('Item name');
    expect(headers).toContain('Reorder level');
    expect(rows[0]).toHaveLength(headers.length);
  });
});
