import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import DataExportPage from './DataExportPage';
import { farmExportApi } from '../../api/farmExport';
import type { FarmExport, FarmExportManifest } from '../../api/farmExport';
import { downloadBlob } from '../../utils/export';

vi.mock('../../api/farmExport', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../api/farmExport')>();
  return {
    ...actual,
    farmExportApi: { status: vi.fn(), request: vi.fn(), download: vi.fn() },
  };
});

vi.mock('../../utils/export', () => ({
  exportCsv: vi.fn(),
  exportExcel: vi.fn(),
  exportPdf: vi.fn(),
  downloadBlob: vi.fn(),
}));

const manifest = (overrides: Partial<FarmExportManifest> = {}): FarmExportManifest => ({
  schemaVersion: 1,
  farmId: 'farm-1',
  farmName: 'E2E Test Farm',
  generatedAtUtc: '2026-09-24T09:00:00Z',
  generator: 'FMS full-farm export',
  archiveFormat: 'zip of per-entity CSV; UTF-8 with BOM; \\n line endings',
  totalRowCount: 43016,
  files: [
    {
      fileName: 'animals.csv',
      entity: 'Animals',
      rowCount: 42,
      columns: ['Tag number', 'Name', 'Animal type'],
      reimportable: true,
    },
    {
      fileName: 'weight-records.csv',
      entity: 'WeightRecords',
      rowCount: 42974,
      columns: ['Id', 'AnimalId', 'WeightKg'],
      reimportable: false,
    },
  ],
  excludedEntities: [
    {
      entity: 'RefreshToken',
      reason: 'A session credential. Exporting it would put a reusable login token in a file the user downloads and stores.',
    },
  ],
  notes: [
    'Raw records only. Computed and aggregated views are not included.',
    'Expenses and income records have no duplicate rule on import.',
  ],
  ...overrides,
});

const exportState = (overrides: Partial<FarmExport> = {}): FarmExport => ({
  id: 'export-1',
  farmId: 'farm-1',
  status: 'Completed',
  requestedByUserId: 'user-1',
  requestedAtUtc: '2026-09-24T08:59:00Z',
  startedAtUtc: '2026-09-24T08:59:01Z',
  completedAtUtc: '2026-09-24T09:00:00Z',
  fileName: 'e2e-test-farm-export-2026-09-24.zip',
  sizeBytes: 1793730,
  error: null,
  isReady: true,
  manifest: manifest(),
  ...overrides,
});

const renderPage = () =>
  render(
    <MemoryRouter>
      <DataExportPage />
    </MemoryRouter>,
  );

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(farmExportApi.status).mockResolvedValue({ data: null } as never);
  vi.mocked(farmExportApi.request).mockResolvedValue({ data: exportState() } as never);
});

afterEach(() => {
  vi.useRealTimers();
});

describe('DataExportPage', () => {
  it('explains what an export is before one exists', async () => {
    renderPage();

    expect(await screen.findByText('No export has been created yet')).toBeInTheDocument();
    expect(screen.getByText(/one CSV per record type/i)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Create export/ })).toBeInTheDocument();
    // Nothing to download until a build has produced an archive.
    expect(screen.getByRole('button', { name: /Download/ })).toBeDisabled();
  });

  it('queues a build and shows its state', async () => {
    vi.mocked(farmExportApi.request).mockResolvedValue({
      data: exportState({ status: 'Queued', isReady: false, manifest: null, sizeBytes: null }),
    } as never);

    renderPage();
    await userEvent.click(await screen.findByRole('button', { name: /Create export/ }));

    await waitFor(() => expect(farmExportApi.request).toHaveBeenCalledTimes(1));
    // The state tag is rendered twice — beside the card title and in its details — so the
    // assertion looks for the state rather than for a unique node.
    expect((await screen.findAllByText('Queued')).length).toBeGreaterThan(0);
    expect(screen.getByRole('button', { name: /Export again/ })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Download/ })).toBeDisabled();
  });

  it('renders the manifest the archive describes itself with', async () => {
    vi.mocked(farmExportApi.status).mockResolvedValue({ data: exportState() } as never);

    renderPage();

    expect(await screen.findByText('animals.csv')).toBeInTheDocument();
    expect(screen.getByText('weight-records.csv')).toBeInTheDocument();
    expect(screen.getByText('re-importable')).toBeInTheDocument();
    expect(screen.getByText('43,016')).toBeInTheDocument();

    // The exclusions and the disclosed limitations come from the server's words, so this
    // page cannot describe the archive differently from the archive itself.
    expect(screen.getByText('Not included')).toBeInTheDocument();
    expect(screen.getByText('RefreshToken')).toBeInTheDocument();
    expect(screen.getByText(/no duplicate rule on import/i)).toBeInTheDocument();
    expect(screen.getByText(/Raw records only/i)).toBeInTheDocument();
  });

  it('downloads the archive and names it from the response, not a guess', async () => {
    vi.mocked(farmExportApi.status).mockResolvedValue({ data: exportState() } as never);
    vi.mocked(farmExportApi.download).mockResolvedValue({
      data: new Blob(['zip']),
      headers: { 'content-disposition': 'attachment; filename="e2e-test-farm-export-2026-09-24.zip"' },
    } as never);

    renderPage();

    const download = await screen.findByRole('button', { name: /Download/ });
    expect(download).toBeEnabled();

    await userEvent.click(download);

    await waitFor(() => expect(farmExportApi.download).toHaveBeenCalledTimes(1));
    expect(downloadBlob).toHaveBeenCalledTimes(1);

    const [, name] = vi.mocked(downloadBlob).mock.calls[0];
    expect(name).toBe('e2e-test-farm-export-2026-09-24.zip');
  });

  it('reports a failed build without hiding the archive it still has', async () => {
    vi.mocked(farmExportApi.status).mockResolvedValue({
      data: exportState({ status: 'Failed', error: 'InvalidOperationException: disk full' }),
    } as never);

    renderPage();

    expect((await screen.findAllByText('Failed')).length).toBeGreaterThan(0);
    expect(screen.getByText(/disk full/)).toBeInTheDocument();
    expect(screen.getByText(/previously built archive is still available/i)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Download/ })).toBeEnabled();
  });

  it('surfaces a refused request rather than pretending a build was queued', async () => {
    vi.mocked(farmExportApi.request).mockRejectedValue({
      response: {
        status: 503,
        data: 'Full-farm export is unavailable because background jobs are disabled in this deployment',
      },
    });

    renderPage();
    await userEvent.click(await screen.findByRole('button', { name: /Create export/ }));

    expect(await screen.findByText(/background jobs are disabled/i)).toBeInTheDocument();
    expect(screen.queryByText('Built')).not.toBeInTheDocument();
  });
});
