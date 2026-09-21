import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Routes, Route, useLocation } from 'react-router-dom';
import DashboardPage from './DashboardPage';
import { dashboardApi, type DashboardAlert, type DashboardSummary } from '../api/dashboard';

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

const alertFixture = (overrides: Partial<DashboardAlert> = {}): DashboardAlert => ({
  alertType: 'OverdueTask',
  severity: 'Warning',
  title: 'Overdue: Clean barn',
  message: 'Task was due Sep 01, 2026',
  dueDate: '2026-09-01T00:00:00Z',
  link: '/dashboard/tasks',
  ...overrides,
});

/** Renders the current path so a click can be proven to navigate, not just look clickable. */
const LocationProbe = () => {
  const location = useLocation();
  return <div data-testid="location">{location.pathname}</div>;
};

const renderDashboard = () =>
  render(
    <MemoryRouter initialEntries={['/dashboard']}>
      <LocationProbe />
      <Routes>
        <Route path="/dashboard" element={<DashboardPage />} />
        <Route path="*" element={<div>navigated</div>} />
      </Routes>
    </MemoryRouter>,
  );

beforeEach(() => {
  vi.mocked(dashboardApi.summary).mockResolvedValue({ data: summaryFixture } as never);
  vi.mocked(dashboardApi.alerts).mockResolvedValue({ data: [] } as never);
  vi.mocked(dashboardApi.charts).mockResolvedValue({
    data: { animalTrends: [], expenseBreakdown: null, feedConsumptionTrend: [], monthlyPL: [] },
  } as never);
});

describe('DashboardPage summary cards', () => {
  it('renders dueWeightCheckCount (not dueVaccinationCount) in the Due Weight Checks card', async () => {
    renderDashboard();

    // Weight check value appears once the summary request resolves.
    expect(await screen.findByText('3')).toBeInTheDocument();

    // The mis-bound vaccination count must not appear anywhere on the page.
    expect(screen.queryByText('7')).not.toBeInTheDocument();
  });

  it('renders the other summary metrics as labeled', async () => {
    renderDashboard();

    expect(await screen.findByText('8')).toBeInTheDocument(); // totalAnimals
    expect(screen.getByText('2')).toBeInTheDocument(); // sickCount
    expect(screen.getByText('Due Weight Checks')).toBeInTheDocument();
  });
});

describe('DashboardPage alerts', () => {
  it('renders a navigable action for an alert that carries a link', async () => {
    vi.mocked(dashboardApi.alerts).mockResolvedValue({ data: [alertFixture()] } as never);

    renderDashboard();

    expect(await screen.findByText('Overdue: Clean barn')).toBeInTheDocument();

    const link = screen.getByRole('link', { name: 'View' });
    expect(link).toHaveAttribute('href', '/dashboard/tasks');

    await userEvent.click(link);

    // Proves the action actually routes, rather than merely looking clickable.
    expect(screen.getByTestId('location')).toHaveTextContent('/dashboard/tasks');
  });

  it('renders safely with no dead action when an alert has no link', async () => {
    vi.mocked(dashboardApi.alerts).mockResolvedValue({
      data: [alertFixture({ link: undefined, title: 'Low stock: Hay Bales' })],
    } as never);

    renderDashboard();

    expect(await screen.findByText('Low stock: Hay Bales')).toBeInTheDocument();
    expect(screen.queryAllByRole('link', { name: 'View' })).toHaveLength(0);
  });
});
