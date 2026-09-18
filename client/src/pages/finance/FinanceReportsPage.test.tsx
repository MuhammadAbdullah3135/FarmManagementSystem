import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import FinanceReportsPage from './FinanceReportsPage';
import { financeReportsApi } from '../../api/finance';

vi.mock('../../api/finance', () => ({
  financeReportsApi: {
    profitLoss: vi.fn(),
    expenseBreakdown: vi.fn(),
    incomeBreakdown: vi.fn(),
    monthlySummary: vi.fn(),
  },
}));

const MONTHS = ['Jan', 'Feb', 'Mar'];

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(financeReportsApi.profitLoss).mockResolvedValue({
    data: { totalIncome: 128450.75, totalExpenses: 92180.4, netProfit: 36270.35 },
  } as never);
  vi.mocked(financeReportsApi.expenseBreakdown).mockResolvedValue({
    data: {
      items: [
        { categoryId: 'c1', categoryName: 'Premium Dairy Feed & Supplements', total: 38210.5, percentage: 41.45 },
        { categoryId: 'c2', categoryName: 'Veterinary Care & Medicines', total: 21450.9, percentage: 23.27 },
      ],
      grandTotal: 92180.4,
    },
  } as never);
  vi.mocked(financeReportsApi.incomeBreakdown).mockResolvedValue({
    data: {
      items: [{ categoryId: 'i1', categoryName: 'Milk Sales — Cooperative', total: 84230.25, percentage: 65.57 }],
      grandTotal: 128450.75,
    },
  } as never);
  vi.mocked(financeReportsApi.monthlySummary).mockResolvedValue({
    data: MONTHS.map((monthName, i) => ({ year: 2026, month: i + 1, monthName, income: 9000 + i, expenses: 7000 + i, net: 2000 })),
  } as never);
});

/** Columns sized by a fixed `span={n}` keep their desktop width at every viewport. */
const fixedSpanColumns = (root: HTMLElement) =>
  [...root.querySelectorAll('.ant-col')].filter((col) => [...col.classList].some((cls) => /^ant-col-\d+$/.test(cls)));

const columnClasses = (root: HTMLElement) => [...root.querySelectorAll('.ant-col')].map((col) => [...col.classList]);

describe('FinanceReportsPage mobile layout', () => {
  // Three `span={8}` P&L cards and two `span={12}` breakdown cards left a 412px phone with ~120px
  // cards: values broke over two lines and each breakdown table showed a third of a row. They must
  // reflow through breakpoints instead.
  it('sizes every card through a breakpoint instead of a fixed span', async () => {
    const { container } = render(<FinanceReportsPage />);
    await waitFor(() => expect(screen.getByText('Premium Dairy Feed & Supplements')).toBeInTheDocument());

    expect(fixedSpanColumns(container)).toHaveLength(0);
    // DOM order: the 2 filter columns, the 3 P&L cards, then the 2 breakdown cards.
    const cols = columnClasses(container);
    expect(cols).toHaveLength(7);
    for (const col of cols.slice(2, 5)) {
      expect(col).toContain('ant-col-xs-12');
      expect(col).toContain('ant-col-sm-8');
    }
    for (const col of cols.slice(5)) {
      expect(col).toContain('ant-col-xs-24');
      expect(col).toContain('ant-col-lg-12');
    }
  });

  // The filter row keeps auto-sized columns (so a laptop's date picker is untouched); the phone
  // rules in AppLayout.css turn `fms-filter-row` into one full-width control per line.
  it('marks the filter row so the phone styles can stack it', async () => {
    const { container } = render(<FinanceReportsPage />);
    await waitFor(() => expect(container.querySelector('.fms-filter-row')).not.toBeNull());

    const row = container.querySelector('.fms-filter-row')!;
    expect(row.querySelectorAll('.ant-picker-range')).toHaveLength(1);
    expect(row.querySelectorAll('.ant-select')).toHaveLength(1);
    expect(row.querySelectorAll('.ant-col')).toHaveLength(2);
    expect(fixedSpanColumns(row as HTMLElement)).toHaveLength(0);
  });

  // The test setup reports every media query as unmatched, i.e. a phone: the breakdown tables drop
  // their `%` column (the pie above them already prints each share) and the summary rows have to
  // drop the matching cell, or the Total row would be one cell wider than its header.
  it('drops the percent column and its summary cell together on a phone', async () => {
    const { container } = render(<FinanceReportsPage />);
    await waitFor(() => expect(screen.getByText('Premium Dairy Feed & Supplements')).toBeInTheDocument());

    const headersPerTable = [...container.querySelectorAll('.ant-table')].map((table) =>
      [...table.querySelectorAll('.ant-table-thead th')].map((th) => th.textContent?.trim()),
    );
    expect(headersPerTable[0]).toEqual(['Category', 'Amount']);
    expect(headersPerTable[1]).toEqual(['Category', 'Amount']);
    // Monthly summary keeps all four of its columns.
    expect(headersPerTable[2]).toEqual(['Month', 'Income', 'Expenses', 'Net']);

    const summaryCellsPerTable = [...container.querySelectorAll('.ant-table')].map((table) =>
      [...table.querySelectorAll('.ant-table-summary td')].map((td) => td.textContent?.trim()),
    );
    expect(summaryCellsPerTable[0]).toEqual(['Total', '$92,180.40']);
    expect(summaryCellsPerTable[1]).toEqual(['Total', '$128,450.75']);
    expect(summaryCellsPerTable[2]).toHaveLength(4);
  });

  // Four financial columns cannot fit 360px, so the month has to stay put while the numbers are
  // dragged into view — and the pinned cell must carry rc-table's fix-start class in the header,
  // the body and the summary row, or the columns drift out of line mid-scroll.
  it('pins the month column of the monthly summary', async () => {
    const { container } = render(<FinanceReportsPage />);
    await waitFor(() => expect(screen.getByText('Premium Dairy Feed & Supplements')).toBeInTheDocument());

    const monthly = [...container.querySelectorAll('.ant-table')][2];
    for (const cell of monthly.querySelectorAll('.ant-table-thead th:first-child, .ant-table-tbody tr.ant-table-row:first-child td:first-child, .ant-table-summary tr td:first-child')) {
      expect(cell.className).toContain('ant-table-cell-fix-start');
    }
    expect(monthly.querySelector('.ant-table-thead th')?.getAttribute('style')).toContain('inset-inline-start: 0');
  });
});
