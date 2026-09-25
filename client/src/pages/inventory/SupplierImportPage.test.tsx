import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import SupplierImportPage from './SupplierImportPage';
import { supplierImportApi } from '../../api/supplierImport';
import type { ImportPreview } from '../../api/importApi';

vi.mock('../../api/supplierImport', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../api/supplierImport')>();
  return {
    ...actual,
    supplierImportApi: { preview: vi.fn(), commit: vi.fn() },
  };
});

vi.mock('../../utils/export', () => ({
  exportCsv: vi.fn(),
  exportExcel: vi.fn(),
  exportPdf: vi.fn(),
}));

const preview = (): ImportPreview => ({
  fields: [
    { key: 'name', label: 'Supplier name', required: true, hint: "Unique within the farm: this is the supplier's identifier" },
    { key: 'contactInfo', label: 'Contact information', required: false, hint: 'Free text' },
  ],
  headers: ['supplier', 'contact'],
  mapping: { fields: { name: { column: 0 }, contactInfo: { column: 1 } }, dateFormat: null },
  suggestedMapping: { fields: { name: { column: 0 } } },
  lookups: {},
  totalRows: 1,
  validRowCount: 1,
  invalidRowCount: 0,
  truncated: false,
  invalidRows: [],
  sampleValidRows: [],
});

const renderPage = () =>
  render(
    <MemoryRouter>
      <SupplierImportPage />
    </MemoryRouter>,
  );

const uploadFile = async (container: HTMLElement) => {
  const input = container.querySelector('input[type="file"]');
  if (!input) throw new Error('file input not found');
  await userEvent.upload(
    input as HTMLInputElement,
    new File(['supplier,contact\nKilimo Feeds,\n'], 'suppliers.csv', { type: 'text/csv' }),
  );
};

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(supplierImportApi.preview).mockResolvedValue({ data: preview() } as never);
  vi.mocked(supplierImportApi.commit).mockResolvedValue({
    data: { totalRows: 1, importedCount: 1, truncated: false, invalidRows: [] },
  } as never);
});

describe('SupplierImportPage', () => {
  it('uses the shared wizard, named for suppliers', async () => {
    renderPage();

    expect(screen.getByText('Import suppliers')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Download CSV template/ })).toBeInTheDocument();
  });

  it('calls the supplier endpoint, not another entity’s', async () => {
    const { container } = renderPage();

    await uploadFile(container);

    await waitFor(() => expect(supplierImportApi.preview).toHaveBeenCalledTimes(1));
  });
});
