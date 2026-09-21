import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import AnimalImportPage from './AnimalImportPage';
import { animalImportApi } from '../../api/animalImport';
import type { AnimalImportPreview, AnimalImportRow } from '../../api/animalImport';
import { exportCsv } from '../../utils/export';

vi.mock('../../api/animalImport', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../api/animalImport')>();
  return {
    ...actual,
    animalImportApi: {
      preview: vi.fn(),
      commit: vi.fn(),
    },
  };
});

vi.mock('../../utils/export', () => ({
  exportCsv: vi.fn(),
  exportExcel: vi.fn(),
  exportPdf: vi.fn(),
}));

const csvFile = (name = 'animals.csv') =>
  new File(['tagNumber,animalType,sex,status\nIMP-001,Cattle,Female,Active\n'], name, { type: 'text/csv' });

const row = (overrides: Partial<AnimalImportRow> = {}): AnimalImportRow => ({
  rowNumber: 3,
  isValid: false,
  errors: [{ field: 'sex', message: "Sex 'Robot' was not found in this farm" }],
  values: { tagNumber: 'IMP-002' },
  ...overrides,
});

const preview = (overrides: Partial<AnimalImportPreview> = {}): AnimalImportPreview => ({
  fields: [
    { key: 'tagNumber', label: 'Tag number', required: true, hint: 'Unique within the farm' },
    { key: 'animalType', label: 'Animal type', required: true, hint: 'e.g. Cattle' },
    { key: 'sex', label: 'Sex', required: true, hint: '' },
    { key: 'status', label: 'Status', required: true, hint: '' },
  ],
  headers: ['tagNumber', 'animalType', 'sex', 'status'],
  mapping: {
    fields: {
      tagNumber: { column: 0 },
      animalType: { column: 1 },
      sex: { column: 2 },
      status: { column: 3 },
    },
    dateFormat: null,
  },
  suggestedMapping: { fields: { tagNumber: { column: 0 } } },
  lookups: { animalType: ['Cattle'], sex: ['Female', 'Male'], status: ['Active'] },
  totalRows: 2,
  validRowCount: 2,
  invalidRowCount: 0,
  truncated: false,
  invalidRows: [],
  sampleValidRows: [
    {
      rowNumber: 2,
      isValid: true,
      errors: [],
      values: { tagNumber: 'IMP-001', animalType: 'Cattle', sex: 'Female', status: 'Active' },
    },
  ],
  ...overrides,
});

const renderPage = () =>
  render(
    <MemoryRouter>
      <AnimalImportPage />
    </MemoryRouter>,
  );

const uploadFile = async (container: HTMLElement, file: File) => {
  const input = container.querySelector('input[type="file"]');
  if (!input) throw new Error('file input not found');
  await userEvent.upload(input as HTMLInputElement, file);
};

/** Drops a file on the control regardless of its accept filter, as a user's OS picker can. */
const forceUploadFile = (container: HTMLElement, file: File) => {
  const input = container.querySelector('input[type="file"]');
  if (!input) throw new Error('file input not found');
  fireEvent.change(input, { target: { files: [file] } });
};

beforeEach(() => {
  vi.clearAllMocks();
  localStorage.setItem('activeFarmId', 'farm-a');
  vi.mocked(animalImportApi.preview).mockResolvedValue({ data: preview() } as never);
  vi.mocked(animalImportApi.commit).mockResolvedValue({
    data: { totalRows: 2, importedCount: 2, truncated: false, invalidRows: [] },
  } as never);
});

describe('AnimalImportPage', () => {
  it('validates an uploaded file without a mapping and moves to the column step', async () => {
    const { container } = renderPage();

    await uploadFile(container, csvFile());

    // No mapping is sent: the server answers with its own guess plus the headers.
    await waitFor(() => expect(animalImportApi.preview).toHaveBeenCalledTimes(1));
    expect(vi.mocked(animalImportApi.preview).mock.calls[0][1]).toBeUndefined();

    // The mapping table is built from the response, not from a hard-coded list.
    expect(await screen.findByText('Tag number')).toBeInTheDocument();
    expect(screen.getByText('Animal type')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Check file/ })).toBeInTheDocument();
  });

  it('re-checks the file with the mapping and the date format the user chose', async () => {
    const { container } = renderPage();
    await uploadFile(container, csvFile());
    await screen.findByRole('button', { name: /Check file/ });

    await userEvent.type(screen.getByLabelText('date-format'), 'dd/MM/yyyy');
    await userEvent.click(screen.getByRole('button', { name: /Check file/ }));

    await waitFor(() => expect(animalImportApi.preview).toHaveBeenCalledTimes(2));
    const mapping = vi.mocked(animalImportApi.preview).mock.calls[1][1];
    expect(mapping?.dateFormat).toBe('dd/MM/yyyy');
    // Every field stays mapped, including the ones the user did not touch.
    expect(mapping?.fields.tagNumber).toEqual({ column: 0 });
  });

  it('lists the problem rows and blocks the import while any row fails', async () => {
    vi.mocked(animalImportApi.preview).mockResolvedValue({
      data: preview({ validRowCount: 1, invalidRowCount: 1, invalidRows: [row()] }),
    } as never);

    const { container } = renderPage();
    await uploadFile(container, csvFile());
    await screen.findByRole('button', { name: /Check file/ });
    await userEvent.click(screen.getByRole('button', { name: /Check file/ }));

    expect(await screen.findByText("Sex 'Robot' was not found in this farm")).toBeInTheDocument();
    // The row number is reported, so the user can find it in their own file.
    expect(screen.getByRole('cell', { name: '3' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Import 1 animal/ })).toBeDisabled();
    expect(screen.getByText(/2 row\(s\): 1 valid, 1 with problems/)).toBeInTheDocument();
  });

  it('imports the file and reports how many animals were created', async () => {
    const { container } = renderPage();
    await uploadFile(container, csvFile());
    await screen.findByRole('button', { name: /Check file/ });
    await userEvent.click(screen.getByRole('button', { name: /Check file/ }));

    await userEvent.click(await screen.findByRole('button', { name: /Import 2 animal/ }));

    await waitFor(() => expect(animalImportApi.commit).toHaveBeenCalledTimes(1));
    expect(vi.mocked(animalImportApi.commit).mock.calls[0][1]?.fields.sex).toEqual({ column: 2 });
    expect(await screen.findByText('Imported 2 animal(s)')).toBeInTheDocument();
  });

  it('shows the rows when a commit writes nothing', async () => {
    vi.mocked(animalImportApi.commit).mockResolvedValue({
      data: {
        totalRows: 2,
        importedCount: 0,
        truncated: false,
        invalidRows: [
          row({ errors: [{ field: 'tagNumber', message: "An animal with tag 'IMP-001' already exists in this farm" }] }),
        ],
      },
    } as never);

    const { container } = renderPage();
    await uploadFile(container, csvFile());
    await screen.findByRole('button', { name: /Check file/ });
    await userEvent.click(screen.getByRole('button', { name: /Check file/ }));
    await userEvent.click(await screen.findByRole('button', { name: /Import 2 animal/ }));

    expect(await screen.findByText('Nothing was imported')).toBeInTheDocument();
    expect(
      screen.getByText("An animal with tag 'IMP-001' already exists in this farm"),
    ).toBeInTheDocument();
  });

  it('refuses an unsupported file type before calling the API', async () => {
    const { container } = renderPage();

    forceUploadFile(container, new File(['anything'], 'herd.xls', { type: 'application/vnd.ms-excel' }));

    expect(animalImportApi.preview).not.toHaveBeenCalled();
    expect(await screen.findByText(/Choose a \.csv or \.xlsx file/)).toBeInTheDocument();
  });

  it('offers a template built from the importer’s own headers', async () => {
    renderPage();

    await userEvent.click(screen.getByRole('button', { name: /Download CSV template/ }));

    expect(exportCsv).toHaveBeenCalledTimes(1);
    const [filename, headers, rows] = vi.mocked(exportCsv).mock.calls[0];
    expect(filename).toBe('animal-import-template');
    expect(headers).toContain('Tag number');
    expect(headers).toContain('Date of birth');
    expect(rows[0]).toHaveLength(headers.length);
  });

  it('surfaces a validation failure from the server', async () => {
    vi.mocked(animalImportApi.preview).mockRejectedValue({
      response: { data: { error: 'Tag number must be mapped to a column or a fixed value.' } },
    });

    const { container } = renderPage();
    await uploadFile(container, csvFile());

    expect(
      await screen.findByText('Tag number must be mapped to a column or a fixed value.'),
    ).toBeInTheDocument();
    // A rejected preview leaves the user on the upload step.
    expect(screen.queryByRole('button', { name: /Check file/ })).not.toBeInTheDocument();
  });

  it('keeps the file in the wizard so a re-check does not need a re-upload', async () => {
    const { container } = renderPage();
    const file = csvFile('herd-register.csv');
    await uploadFile(container, file);
    await screen.findByRole('button', { name: /Check file/ });

    fireEvent.click(screen.getByRole('button', { name: /Choose another file/ }));
    await uploadFile(container, file);

    await waitFor(() => expect(animalImportApi.preview).toHaveBeenCalledTimes(2));
    expect(vi.mocked(animalImportApi.preview).mock.calls[1][0]).toBe(file);
  });
});
