import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import CostPerAnimalPage from './CostPerAnimalPage';
import { reportsApi, type CostPerAnimalReport } from '../../api/reports';
import { exportCsv } from '../../utils/export';

vi.mock('../../api/reports', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../api/reports')>();
  return {
    ...actual,
    reportsApi: { costPerAnimalReport: vi.fn() },
  };
});

vi.mock('../../utils/export', () => ({
  exportCsv: vi.fn(),
  exportExcel: vi.fn(),
  exportPdf: vi.fn(),
}));

const report = (overrides: Partial<CostPerAnimalReport> = {}): CostPerAnimalReport => ({
  from: '2025-06-01T00:00:00Z',
  to: '2025-06-30T00:00:00Z',
  totalAnimalDays: 60,
  animals: [
    {
      animalId: 'a-1',
      tagNumber: 'A-001',
      name: 'Bessie',
      locationId: 'loc-1',
      locationName: 'Barn 1',
      presentFrom: '2025-06-01T00:00:00Z',
      animalDays: 30,
      shareOfFarmDays: 0.5,
      costs: [
        {
          key: 'feed.direct',
          label: 'Feed (this animal)',
          source: 'Feed records naming this animal',
          method: 'direct',
          amount: 20,
          recordCount: 2,
          roundingAdjustment: 0,
        },
        {
          key: 'labour.farm',
          label: 'Labour (shared)',
          source: 'Salary payments for the farm',
          method: 'farm-animal-days',
          amount: 300,
          recordCount: 0,
          poolAmount: 600,
          allocatedDays: 30,
          poolDays: 60,
          roundingAdjustment: 0,
        },
      ],
      revenue: [],
      totalCost: 320,
      totalRevenue: 0,
      margin: -320,
      warnings: [],
    },
    {
      animalId: 'a-2',
      tagNumber: 'A-002',
      name: 'Daisy',
      locationId: 'loc-1',
      locationName: 'Barn 1',
      presentFrom: '2025-06-01T00:00:00Z',
      animalDays: 30,
      shareOfFarmDays: 0.5,
      costs: [],
      revenue: [
        {
          key: 'income.direct',
          label: 'Income against this animal',
          source: 'Income records naming this animal',
          method: 'direct',
          amount: 500,
          recordCount: 1,
          roundingAdjustment: 0,
        },
      ],
      totalCost: 0,
      totalRevenue: 500,
      margin: 500,
      warnings: ['animal.departure-unknown'],
    },
  ],
  herds: [
    { locationId: 'loc-1', locationName: 'Barn 1', animalCount: 2, animalDays: 60, totalCost: 320, totalRevenue: 500, margin: 180 },
  ],
  farm: { totalCost: 320, totalRevenue: 500, margin: 180, costPerAnimalDay: 5.33 },
  rules: [
    { key: 'direct', title: 'Records that name the animal are used as they are', description: 'Nothing is shared.' },
    { key: 'farm-animal-days', title: 'Whole-farm costs are shared by animal-days', description: 'Split by days present.' },
  ],
  warnings: [],
  reconciliation: {
    farmExpensesTotal: 0,
    healthLinkedExpenses: 0,
    expensesAttributedToAnimals: 0,
    expensesAllocatedFromLocations: 0,
    expensesAllocatedFromFarmPool: 0,
    expensesUnallocated: 0,
    expensesReconcile: true,
    feedConsumedTotal: 20,
    feedAttributedToAnimals: 20,
    feedAllocatedFromLocations: 0,
    feedUnallocated: 0,
    feedReconciles: true,
    healthRecordsTotal: 0,
    healthAttributedToAnimals: 0,
    healthCostOutsideTheExpenseLedger: 0,
    healthReconciles: true,
    labourTotal: 600,
    costOutsideTheExpenseLedger: 620,
  },
  ...overrides,
});

const renderPage = () =>
  render(
    <MemoryRouter>
      <CostPerAnimalPage />
    </MemoryRouter>,
  );

const mockReport = (data: CostPerAnimalReport) =>
  vi.mocked(reportsApi.costPerAnimalReport).mockResolvedValue({ data } as never);

describe('CostPerAnimalPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('shows each animal with its cost, revenue and animal-days', async () => {
    mockReport(report());
    renderPage();

    await waitFor(() => expect(screen.getByText('A-001')).toBeInTheDocument());

    expect(screen.getByText('Bessie')).toBeInTheDocument();
    expect(screen.getByText('A-002')).toBeInTheDocument();
    // Two animal rows in the herd plus the herd row itself.
    expect(screen.getAllByText('Barn 1')).toHaveLength(3);
    // Summary cards read from the report's own totals, not from a re-summed client copy.
    expect(screen.getByText('Cost per animal-day')).toBeInTheDocument();
  });

  it('shows the arithmetic behind an allocated figure instead of just a number', async () => {
    mockReport(report());
    const user = userEvent.setup();
    renderPage();

    await waitFor(() => expect(screen.getByText('A-001')).toBeInTheDocument());
    await user.click(screen.getByText('A-001').closest('tr')!.querySelector('.ant-table-row-expand-icon') as HTMLElement);

    // 30 / 60 days × 600.00 is the whole point of the breakdown: the reader can check it.
    expect(await screen.findByText(/30 \/ 60 days × 600\.00/)).toBeInTheDocument();
    expect(screen.getByText('Labour (shared)')).toBeInTheDocument();
    expect(screen.getByText('Feed (this animal)')).toBeInTheDocument();
  });

  it('puts the caveats above the numbers when the underlying data is incomplete', async () => {
    mockReport(report({
      warnings: [
        {
          code: 'feed.not-recorded',
          message: 'No feed consumption was recorded in this range, so feed cost is missing from these figures.',
          affectedCount: 0,
        },
        {
          code: 'pool.unallocated',
          message: 'Some shared cost could not be attributed to an animal.',
          affectedCount: 0,
          amount: 600,
        },
      ],
    }));
    renderPage();

    await waitFor(() => expect(screen.getByText('Read these numbers with the following in mind')).toBeInTheDocument());
    expect(screen.getByText(/No feed consumption was recorded in this range/)).toBeInTheDocument();
    expect(screen.getByText('(600.00)')).toBeInTheDocument();
  });

  it('does not show a caveat box when there is nothing to caveat', async () => {
    mockReport(report());
    renderPage();

    await waitFor(() => expect(screen.getByText('A-001')).toBeInTheDocument());
    expect(screen.queryByText('Read these numbers with the following in mind')).not.toBeInTheDocument();
  });

  it('shows the reconciliation so the totals can be checked, not just trusted', async () => {
    mockReport(report());
    renderPage();

    // "Reconciliation" is a static card title, so wait on the data behind it instead.
    await waitFor(() => expect(screen.getByText('Expenses add up')).toBeInTheDocument());

    expect(screen.getByText('Feed adds up')).toBeInTheDocument();
    expect(screen.getByText('Health adds up')).toBeInTheDocument();
    expect(screen.getByText('Cost with no expense behind it')).toBeInTheDocument();
    expect(screen.getByText('620.00')).toBeInTheDocument();
  });

  it('states the allocation rules it applied', async () => {
    mockReport(report());
    renderPage();

    await waitFor(() => expect(screen.getByText('A-001')).toBeInTheDocument());

    expect(screen.getByText('Whole-farm costs are shared by animal-days')).toBeInTheDocument();
    expect(screen.getByText('Split by days present.')).toBeInTheDocument();
  });

  it('exports the animal rows', async () => {
    mockReport(report());
    const user = userEvent.setup();
    renderPage();

    await waitFor(() => expect(screen.getByText('A-001')).toBeInTheDocument());
    await user.click(screen.getByRole('button', { name: /Export/i }));
    await user.click(await screen.findByText('Export CSV'));

    expect(exportCsv).toHaveBeenCalledTimes(1);
    const [filename, headers, rows] = vi.mocked(exportCsv).mock.calls[0];
    expect(filename).toBe('cost-per-animal');
    expect(headers).toContain('Animal-days');
    expect(rows[0]).toEqual(['A-001', 'Bessie', 'Barn 1', 30, 320, 0, -320]);
  });
});
