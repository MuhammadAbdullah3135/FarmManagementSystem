import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import EmployeeImportPage from './EmployeeImportPage';
import { employeeImportApi } from '../../api/employeeImport';
import type { ImportPreview, ImportRow } from '../../api/importApi';
import { exportCsv } from '../../utils/export';

vi.mock('../../api/employeeImport', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../api/employeeImport')>();
  return {
    ...actual,
    employeeImportApi: { preview: vi.fn(), commit: vi.fn() },
  };
});

vi.mock('../../utils/export', () => ({
  exportCsv: vi.fn(),
  exportExcel: vi.fn(),
  exportPdf: vi.fn(),
}));

const csvFile = (name = 'staff.csv') =>
  new File(['firstName,lastName,department\nAmina,Yusuf,Dairy\n'], name, { type: 'text/csv' });

const row = (overrides: Partial<ImportRow> = {}): ImportRow => ({
  rowNumber: 3,
  isValid: false,
  errors: [{ field: 'department', message: "Department 'Poultry' was not found in this farm" }],
  values: { firstName: 'Amina' },
  ...overrides,
});

const preview = (overrides: Partial<ImportPreview> = {}): ImportPreview => ({
  fields: [
    { key: 'firstName', label: 'First name', required: true, hint: 'e.g. Amina' },
    { key: 'lastName', label: 'Last name', required: true, hint: 'e.g. Yusuf' },
    { key: 'department', label: 'Department', required: true, hint: 'Must match a department in this farm' },
    { key: 'salaryType', label: 'Salary type', required: true, hint: 'Monthly, Weekly, Daily or Hourly' },
  ],
  headers: ['firstName', 'lastName', 'department'],
  mapping: {
    fields: {
      firstName: { column: 0 },
      lastName: { column: 1 },
      department: { column: 2 },
    },
    dateFormat: null,
  },
  suggestedMapping: { fields: { firstName: { column: 0 } } },
  lookups: { department: ['Dairy'], salaryType: ['Monthly', 'Weekly', 'Daily', 'Hourly'] },
  totalRows: 2,
  validRowCount: 2,
  invalidRowCount: 0,
  truncated: false,
  invalidRows: [],
  sampleValidRows: [
    { rowNumber: 2, isValid: true, errors: [], values: { firstName: 'Amina', lastName: 'Yusuf', department: 'Dairy' } },
  ],
  ...overrides,
});

const renderPage = () =>
  render(
    <MemoryRouter>
      <EmployeeImportPage />
    </MemoryRouter>,
  );

const uploadFile = async (container: HTMLElement) => {
  const input = container.querySelector('input[type="file"]');
  if (!input) throw new Error('file input not found');
  await userEvent.upload(input as HTMLInputElement, csvFile());
};

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(employeeImportApi.preview).mockResolvedValue({ data: preview() } as never);
  vi.mocked(employeeImportApi.commit).mockResolvedValue({
    data: { totalRows: 2, importedCount: 2, truncated: false, invalidRows: [] },
  } as never);
});

describe('EmployeeImportPage', () => {
  it('uses the shared wizard, named for employees', async () => {
    renderPage();

    expect(screen.getByText('Import employees')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Download CSV template/ })).toBeInTheDocument();
  });

  it('calls the employee endpoint, not the animal one', async () => {
    const { container } = renderPage();

    await uploadFile(container);

    await waitFor(() => expect(employeeImportApi.preview).toHaveBeenCalledTimes(1));
    // No mapping on the first call: the server answers with its own guess.
    expect(vi.mocked(employeeImportApi.preview).mock.calls[0][1]).toBeUndefined();
  });

  it('builds the mapping table from the response, including the farm’s departments', async () => {
    const { container } = renderPage();

    await uploadFile(container);

    // The labels and the fixed-value options both come from the server, so the wizard
    // has no employee-specific field list of its own.
    expect(await screen.findByText('Department')).toBeInTheDocument();
    expect(screen.getByText('Salary type')).toBeInTheDocument();
    expect(screen.getByText('Must match a department in this farm')).toBeInTheDocument();
  });

  it('counts employees, not animals', async () => {
    const { container } = renderPage();
    await uploadFile(container);
    await screen.findByRole('button', { name: /Check file/ });
    await userEvent.click(screen.getByRole('button', { name: /Check file/ }));

    await userEvent.click(await screen.findByRole('button', { name: /Import 2 employee/ }));

    expect(await screen.findByText('Imported 2 employee(s)')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'View employees' })).toBeInTheDocument();
  });

  it('blames the mapped field, not the raw key, on a problem row', async () => {
    vi.mocked(employeeImportApi.preview).mockResolvedValue({
      data: preview({ validRowCount: 1, invalidRowCount: 1, invalidRows: [row()] }),
    } as never);

    const { container } = renderPage();
    await uploadFile(container);
    await screen.findByRole('button', { name: /Check file/ });
    await userEvent.click(screen.getByRole('button', { name: /Check file/ }));

    expect(await screen.findByText("Department 'Poultry' was not found in this farm")).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Import 1 employee/ })).toBeDisabled();
  });

  it('offers a template built from the employee importer’s own headers', async () => {
    renderPage();

    await userEvent.click(screen.getByRole('button', { name: /Download CSV template/ }));

    expect(exportCsv).toHaveBeenCalledTimes(1);
    const [filename, headers, rows] = vi.mocked(exportCsv).mock.calls[0];
    expect(filename).toBe('employee-import-template');
    expect(headers).toContain('First name');
    expect(headers).toContain('Hire date');
    expect(rows[0]).toHaveLength(headers.length);
  });

  it('blocks the import while any row fails, and says why', async () => {
    vi.mocked(employeeImportApi.preview).mockResolvedValue({
      data: preview({ validRowCount: 1, invalidRowCount: 1, invalidRows: [row()] }),
    } as never);

    const { container } = renderPage();
    await uploadFile(container);
    await screen.findByRole('button', { name: /Check file/ });
    await userEvent.click(screen.getByRole('button', { name: /Check file/ }));

    expect(screen.getByText(/2 row\(s\): 1 valid, 1 with problems/)).toBeInTheDocument();
    expect(employeeImportApi.commit).not.toHaveBeenCalled();
  });
});
