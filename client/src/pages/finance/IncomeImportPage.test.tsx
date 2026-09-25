import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import IncomeImportPage from './IncomeImportPage';
import { incomeImportApi } from '../../api/incomeImport';
import type { ImportPreview } from '../../api/importApi';

vi.mock('../../api/incomeImport', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../api/incomeImport')>();
  return {
    ...actual,
    incomeImportApi: { preview: vi.fn(), commit: vi.fn() },
  };
});

vi.mock('../../utils/export', () => ({
  exportCsv: vi.fn(),
  exportExcel: vi.fn(),
  exportPdf: vi.fn(),
}));

const preview = (): ImportPreview => ({
  fields: [
    { key: 'amount', label: 'Amount', required: true, hint: 'Greater than zero' },
    { key: 'incomeCategory', label: 'Income category', required: true, hint: 'Must match an income category in this farm' },
  ],
  headers: ['date', 'amount', 'category'],
  mapping: { fields: { amount: { column: 1 }, incomeCategory: { column: 2 } }, dateFormat: null },
  suggestedMapping: { fields: { amount: { column: 1 } } },
  lookups: { incomeCategory: ['Milk Sales'] },
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
      <IncomeImportPage />
    </MemoryRouter>,
  );

const uploadFile = async (container: HTMLElement) => {
  const input = container.querySelector('input[type="file"]');
  if (!input) throw new Error('file input not found');
  await userEvent.upload(
    input as HTMLInputElement,
    new File(['date,amount,category\n2026-01-20,100,Milk Sales\n'], 'income.csv', { type: 'text/csv' }),
  );
};

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(incomeImportApi.preview).mockResolvedValue({ data: preview() } as never);
  vi.mocked(incomeImportApi.commit).mockResolvedValue({
    data: { totalRows: 1, importedCount: 1, truncated: false, invalidRows: [] },
  } as never);
});

describe('IncomeImportPage', () => {
  it('uses the shared wizard, named for income records', async () => {
    renderPage();

    expect(screen.getByText('Import income')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Download CSV template/ })).toBeInTheDocument();
  });

  it('discloses the no-duplicate limitation before commit', async () => {
    const { container } = renderPage();

    await uploadFile(container);
    await screen.findByRole('button', { name: /Check file/ });
    await userEvent.click(screen.getByRole('button', { name: /Check file/ }));

    expect(await screen.findByText('Before you import')).toBeInTheDocument();
    expect(screen.getByText(/this import does not check for duplicates/)).toBeInTheDocument();
  });
});
