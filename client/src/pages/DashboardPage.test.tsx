import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import DashboardPage from './DashboardPage';
import { dashboardApi, type DashboardSummary } from '../api/dashboard';

vi.mock('../api/dashboard', () => ({
  dashboardApi: {
    summary: vi.fn(),
    alerts: vi.fn(),
    charts: vi.fn(),
  },
}));

vi.mock('../stores/authStore', () => ({
  useAuthStore: vi.fn(() => ({ user: { firstName: 'Jane' } })),
}));

vi.mock('../stores/farmStore', () => ({
  useFarmStore: vi.fn(() => ({ activeFarm: { id: 'f1', name: 'Green Acres' } })),
}));

// The two counts must be distinct so a swapped binding cannot pass.
const summaryFixture: DashboardSummary = {
  totalAnimals: 8,
  animalsByType: { Cattle: 8 },
  animalsByStatus: { Healthy: 5 },
  pregnantCount: 1,
  sickCount: 2,
  dueVaccinationCount: 7,
  dueWeightCheckCount: 3,
  overdueTasks: 0,
  upcomingBirths: 0,
  totalFeedStockValue: 100,
  totalInventoryStockValue: 50,
  degradedMetrics: [],
};

beforeEach(() => {
  vi.mocked(dashboardApi.summary).mockResolvedValue({ data: summaryFixture } as never);
  vi.mocked(dashboardApi.alerts).mockResolvedValue({ data: [] } as never);
  vi.mocked(dashboardApi.charts).mockResolvedValue({
    data: { animalTrends: [], expenseBreakdown: null, feedConsumptionTrend: [], monthlyPL: [] },
  } as never);
});

describe('DashboardPage summary cards', () => {
  it('renders dueWeightCheckCount (not dueVaccinationCount) in the Due Weight Checks card', async () => {
    render(<DashboardPage />);

    // Weight check value appears once the summary request resolves.
    expect(await screen.findByText('3')).toBeInTheDocument();

    // The mis-bound vaccination count must not appear anywhere on the page.
    expect(screen.queryByText('7')).not.toBeInTheDocument();
  });

  it('renders the other summary metrics as labeled', async () => {
    render(<DashboardPage />);

    expect(await screen.findByText('8')).toBeInTheDocument(); // totalAnimals
    expect(screen.getByText('2')).toBeInTheDocument(); // sickCount
    expect(screen.getByText('Due Weight Checks')).toBeInTheDocument();
  });
});
