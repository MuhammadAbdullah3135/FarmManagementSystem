import { describe, it, expect, vi, beforeEach } from 'vitest';
import type { ReactElement } from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ConfigProvider } from 'antd';
import FeedReportsPage from './FeedReportsPage';
import { feedReportsApi } from '../../api/feed';
import type { FeedCostSummary } from '../../types';

vi.mock('../../api/feed', () => ({
  feedReportsApi: {
    consumptionTrend: vi.fn(),
    byFeedType: vi.fn(),
    byAnimal: vi.fn(),
    byLocation: vi.fn(),
    costSummary: vi.fn(),
  },
}));

const summaryFixture = (overrides: Partial<FeedCostSummary> = {}): FeedCostSummary => ({
  from: '2026-08-19T00:00:00.000Z',
  to: '2026-09-18T00:00:00.000Z',
  totalConsumedQuantity: 125.5,
  totalConsumedCost: 1000,
  totalPurchasedQuantity: 500,
  totalPurchasedCost: 2500.75,
  currentInventoryValue: 3200,
  ...overrides,
});

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(feedReportsApi.consumptionTrend).mockResolvedValue({ data: [] } as never);
  vi.mocked(feedReportsApi.byFeedType).mockResolvedValue({ data: [] } as never);
  vi.mocked(feedReportsApi.byAnimal).mockResolvedValue({ data: [] } as never);
  vi.mocked(feedReportsApi.byLocation).mockResolvedValue({ data: [] } as never);
  vi.mocked(feedReportsApi.costSummary).mockResolvedValue({ data: summaryFixture() } as never);
});

/** Columns whose width comes from a fixed `span={n}` and can therefore never reflow. */
const fixedSpanColumns = (root: HTMLElement) =>
  [...root.querySelectorAll('.ant-col')].filter((col) =>
    [...col.classList].some((cls) => /^ant-col-\d+$/.test(cls)),
  );

// Mirrors the `table={{ scroll: { x: 'max-content' } }}` ConfigProvider default in App.tsx:
// a pinned column only actually pins when scroll.x is set, so a bare render would pass this test
// for the wrong reason.
const renderAsAppDoes = (ui: ReactElement) =>
  render(<ConfigProvider table={{ scroll: { x: 'max-content' } }}>{ui}</ConfigProvider>);

describe('FeedReportsPage mobile layout', () => {
  // A `span={4}`/`span={6}` column keeps its desktop width at every viewport, which is what
  // squeezed six stat cards into a single phone-width row and clipped their labels.
  it('sizes every column through a breakpoint so phones reflow instead of squeezing', async () => {
    const user = userEvent.setup();
    const { container } = render(<FeedReportsPage />);

    await user.click(screen.getByRole('tab', { name: 'Cost Summary' }));
    await waitFor(() => expect(container.querySelectorAll('.ant-statistic').length).toBe(10));

    expect(fixedSpanColumns(container)).toHaveLength(0);
    // Two half-width cards per row on phones: six in the summary row, four in the cost tab.
    expect(container.querySelectorAll('.ant-col-xs-12')).toHaveLength(10);
  });

  it('keeps all five report tabs reachable and the top summary row unchanged', async () => {
    const user = userEvent.setup();
    const { container } = render(<FeedReportsPage />);

    for (const label of ['Trend', 'By Type', 'By Animal', 'By Location', 'Cost Summary']) {
      expect(screen.getAllByRole('tab', { name: label }).length).toBeGreaterThan(0);
    }

    // The summary row only appears once the cost summary has loaded — unchanged behaviour.
    expect(container.querySelectorAll('.ant-statistic')).toHaveLength(0);
    await user.click(screen.getByRole('tab', { name: 'Cost Summary' }));
    await waitFor(() => expect(container.querySelectorAll('.ant-statistic').length).toBe(10));
  });

  // The first column stays put while the rest of the row scrolls sideways, so you can still tell
  // which animal/location/feed type a row belongs to on a phone.
  it('pins the row-identity column of the wide tables', async () => {
    vi.mocked(feedReportsApi.byAnimal).mockResolvedValue({
      data: [
        { animalId: 'a1', tagNumber: 'COW-001', name: 'Bella', quantity: 320.5, cost: 2560.5 },
        { animalId: 'a2', tagNumber: 'COW-002', name: 'Daisy', quantity: 280.25, cost: 2241.99 },
      ],
    } as never);

    const user = userEvent.setup();
    const { container } = renderAsAppDoes(<FeedReportsPage />);

    await user.click(screen.getByRole('tab', { name: 'By Animal' }));
    await screen.findByText('COW-001');

    // antd v6 marks pinned cells `-fix-start` (v5's `-fix-left` is gone); 'left' as a column
    // value type-checks but never pins, which is exactly what this guards.
    const pinnedCells = [...container.querySelectorAll('.ant-table-cell-fix-start')].map((cell) => cell.textContent);
    expect(pinnedCells).toEqual(expect.arrayContaining(['Tag', 'COW-001', 'COW-002']));
    // The rest of the row scrolls inside this container, header included.
    expect(container.querySelector('.ant-table-content')).not.toBeNull();
  });

  it('stacks the date range and period filters full width on phones', () => {
    render(<FeedReportsPage />);

    // A plain Space never wraps, so the period select used to be pushed past the right edge.
    const filterRow = document.querySelector('.ant-picker-range')?.closest('.ant-row');
    expect(filterRow).not.toBeNull();

    const columns = [...(filterRow as HTMLElement).children];
    expect(columns).toHaveLength(2);
    expect(columns.every((column) => column.classList.contains('ant-col-xs-24'))).toBe(true);

    expect(document.querySelector('.ant-picker-range')).toHaveStyle({ width: '100%' });
    expect(document.querySelector('.ant-select')).toHaveStyle({ width: '100%' });
  });
});
