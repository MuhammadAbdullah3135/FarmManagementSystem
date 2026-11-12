import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import AuditLogPage from './AuditLogPage';
import { auditLogsApi } from '../../api/auditLogs';
import type { AuditLogEntry, PagedResult } from '../../api/auditLogs';
import { useFarmStore } from '../../stores/farmStore';

vi.mock('../../api/auditLogs', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../api/auditLogs')>();
  return {
    ...actual,
    auditLogsApi: { getLogs: vi.fn() },
  };
});

vi.mock('../../stores/farmStore', () => ({
  useFarmStore: vi.fn(),
}));

type LogFactoryOverrides = Partial<AuditLogEntry> & Partial<Pick<PagedResult<AuditLogEntry>, 'items' | 'totalCount' | 'page' | 'pageSize' | 'totalPages'>>;

const logPage = (overrides: LogFactoryOverrides = {}): PagedResult<AuditLogEntry> => {
  const items = overrides.items ?? [
    {
      id: 'log-1',
      timestamp: '2026-09-19T10:05:00Z',
      userEmail: 'vet@farm.example',
      action: 'Update',
      actionDisplay: 'Updated',
      entityType: 'Animal',
      entityId: 'A-101',
      oldValues: JSON.stringify({ weightKg: 42.0 }),
      newValues: JSON.stringify({ weightKg: 44.5 }),
      ipAddress: '127.0.0.1',
      farmId: 'farm-1',
      ...overrides,
    },
    {
      id: 'log-2',
      timestamp: '2026-09-19T09:30:00Z',
      userEmail: null,
      action: 'Create',
      actionDisplay: 'Created',
      entityType: 'VaccinationRecord',
      entityId: 'V-2026-09-19-001',
      oldValues: null,
      newValues: JSON.stringify({ product: 'Booster X', site: 'Left neck' }),
      ipAddress: '127.0.0.1',
      farmId: 'farm-1',
      ...overrides,
    },
    {
      id: 'log-3',
      timestamp: '2026-09-18T16:00:00Z',
      userEmail: 'manager@farm.example',
      action: 'Delete',
      actionDisplay: 'Deleted',
      entityType: 'FeedType',
      entityId: 'FT-DEMO-OUTDATED',
      oldValues: JSON.stringify({ name: 'Old ration mix' }),
      newValues: null,
      ipAddress: '127.0.0.1',
      farmId: 'farm-1',
      ...overrides,
    },
  ];

  return {
    items,
    totalCount: items.length,
    page: 1,
    pageSize: 10,
    totalPages: Math.max(1, Math.ceil(items.length / 10)),
    ...overrides,
  } as PagedResult<AuditLogEntry>;
};

const emptyState = (): PagedResult<AuditLogEntry> => ({
  items: [],
  totalCount: 0,
  page: 1,
  pageSize: 20,
  totalPages: 1,
});

beforeEach(() => {
  vi.clearAllMocks();
  (useFarmStore as unknown as vi.Mock).mockReturnValue({
    activeFarm: { id: 'farm-1', name: 'Demo Farm' },
  });
  vi.mocked(auditLogsApi.getLogs).mockResolvedValue({ data: logPage() });
});

describe('AuditLogPage', () => {
  it('renders the audit log table columns and the first page of entries', async () => {
    render(
      <MemoryRouter>
        <AuditLogPage />
      </MemoryRouter>,
    );

    await waitFor(() => expect(screen.getByText('log-1')).toBeInTheDocument());

    expect(screen.getByText('2026-09-19 10:05:00')).toBeInTheDocument();
    expect(screen.getByText('vet@farm.example')).toBeInTheDocument();
    expect(screen.getByText('A-101')).toBeInTheDocument();

    expect(screen.getByText('Updated')).toBeInTheDocument();
    expect(screen.getByText('VaccinationRecord')).toBeInTheDocument();
    expect(screen.getByText('V-2026-09-19-001')).toBeInTheDocument();
  });

  it('renders the action tooltip header as part of the action column', async () => {
    render(
      <MemoryRouter>
        <AuditLogPage />
      </MemoryRouter>,
    );

    await waitFor(() => expect(screen.getByText('Action')).toBeInTheDocument());
    expect(screen.getByTitle("Don't show me this again")).toBeInTheDocument();
  });

  it('renders the control bar with entity type, action, search and date range filters', async () => {
    render(
      <MemoryRouter>
        <AuditLogPage />
      </MemoryRouter>,
    );

    expect(screen.getByPlaceholderText('Entity type')).toBeInTheDocument();
    expect(screen.getByPlaceholderText('Action')).toBeInTheDocument();
    expect(screen.getByPlaceholderText('Search')).toBeInTheDocument();
    expect(screen.getByRole('group', { name: /date range/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /refresh/i })).toBeInTheDocument();
  });

  it('shows the pagination totals using the translated totalRecords message', async () => {
    render(
      <MemoryRouter>
        <AuditLogPage />
      </MemoryRouter>,
    );

    await waitFor(() =>
      expect(screen.getByText(/showing/i)).toBeInTheDocument(),
    );
  });

  it('renders an expanded row with the old and new values as a field table', async () => {
    const user = userEvent.setup();
    render(
      <MemoryRouter>
        <AuditLogPage />
      </MemoryRouter>,
    );

    await waitFor(() =>
      expect(screen.getByText('A-101')).toBeInTheDocument(),
    );

    const row = screen.getByText('A-101').closest('tr');
    expect(row).not.toBeNull();

    const expandButton = within(row!).getByRole('button', {
      name: /expand/i,
    });
    await user.click(expandButton);

    await waitFor(() =>
      expect(
        screen.getByText('weightKg'),
      ).toBeInTheDocument(),
    );
    expect(screen.getByText('42')).toBeInTheDocument();
    expect(screen.getByText('44.5')).toBeInTheDocument();
  });

  it('renders a row with only new values', async () => {
    render(
      <MemoryRouter>
        <AuditLogPage />
      </MemoryRouter>,
    );

    await waitFor(() =>
      expect(screen.getByText('V-2026-09-19-001')).toBeInTheDocument(),
    );

    const row = screen.getByText('V-2026-09-19-001').closest('tr');
    expect(row).not.toBeNull();

    const user = userEvent.setup();
    await user.click(within(row!).getByRole('button', { name: /expand/i }));

    await waitFor(() =>
      expect(screen.getByText('product')).toBeInTheDocument(),
    );
    expect(screen.getByText('Booster X')).toBeInTheDocument();
  });

  it('renders an expanded row that shows the formatted old value for a field', async () => {
    const user = userEvent.setup();
    render(
      <MemoryRouter>
        <AuditLogPage />
      </MemoryRouter>,
    );

    await waitFor(() =>
      expect(screen.getByText('FT-DEMO-OUTDATED')).toBeInTheDocument(),
    );

    const row = screen.getByText('FT-DEMO-OUTDATED').closest('tr');
    expect(row).not.toBeNull();

    await user.click(within(row!).getByRole('button', { name: /expand/i }));

    await waitFor(() =>
      expect(screen.getByText('name')).toBeInTheDocument(),
    );
    expect(screen.getByText('Old ration mix')).toBeInTheDocument();
  });

  it('uses the entityType filter to restrict the listed entity types', async () => {
    render(
      <MemoryRouter>
        <AuditLogPage />
      </MemoryRouter>,
    );

    const entitySelect = screen.getByPlaceholderText('Entity type');
    await userEvent.selectOptions(entitySelect, 'Animal');

    expect(auditLogsApi.getLogs).toHaveBeenCalledWith(
      'farm-1',
      expect.objectContaining({ entityType: 'Animal' }),
    );
  });

  it('uses the action filter when one is picked', async () => {
    render(
      <MemoryRouter>
        <AuditLogPage />
      </MemoryRouter>,
    );

    const actionSelect = screen.getByPlaceholderText('Action');
    await userEvent.selectOptions(actionSelect, 'Delete');

    expect(auditLogsApi.getLogs).toHaveBeenCalledWith(
      'farm-1',
      expect.objectContaining({ action: 'Delete' }),
    );
  });

  it('uses the written search term when the user presses enter', async () => {
    render(
      <MemoryRouter>
        <AuditLogPage />
      </MemoryRouter>,
    );

    const searchInput = screen.getByPlaceholderText('Search');
    await userEvent.clear(searchInput);
    await userEvent.type(searchInput, 'A-101{enter}');

    expect(auditLogsApi.getLogs).toHaveBeenCalledWith(
      'farm-1',
      expect.objectContaining({ search: 'A-101' }),
    );
  });

  it('refetches when the refresh button is pressed', async () => {
    render(
      <MemoryRouter>
        <AuditLogPage />
      </MemoryRouter>,
    );

    await waitFor(() =>
      expect(screen.getByText('log-1')).toBeInTheDocument(),
    );
    expect(auditLogsApi.getLogs).toHaveBeenCalledTimes(1);

    await userEvent.click(screen.getByRole('button', { name: /refresh/i }));
    await waitFor(() =>
      expect(auditLogsApi.getLogs).toHaveBeenCalledTimes(2),
    );
  });

  it('shows the empty audit trail text when the farm has no entries', async () => {
    vi.mocked(auditLogsApi.getLogs).mockResolvedValue({
      data: emptyState(),
    });

    render(
      <MemoryRouter>
        <AuditLogPage />
      </MemoryRouter>,
    );

    await waitFor(() =>
      expect(screen.queryByText('log-1')).not.toBeInTheDocument(),
    );
    expect(
      await screen.findByText(/no entries yet/i),
    ).toBeInTheDocument();
  });

  it('does nothing on mount when there is no active farm', async () => {
    (useFarmStore as unknown as vi.Mock).mockReturnValue({ activeFarm: null });

    render(
      <MemoryRouter>
        <AuditLogPage />
      </MemoryRouter>,
    );

    await waitFor(
      () => expect(auditLogsApi.getLogs).not.toHaveBeenCalled(),
      { timeout: 500 },
    );
  });
});
