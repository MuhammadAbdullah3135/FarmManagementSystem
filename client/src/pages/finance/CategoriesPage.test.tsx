import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import CategoriesPage from './CategoriesPage';
import { expenseCategoriesApi, incomeCategoriesApi, paymentMethodsApi } from '../../api/finance';

vi.mock('../../api/finance', () => ({
  expenseCategoriesApi: { list: vi.fn(), create: vi.fn(), update: vi.fn(), remove: vi.fn() },
  incomeCategoriesApi: { list: vi.fn(), create: vi.fn(), update: vi.fn(), remove: vi.fn() },
  paymentMethodsApi: { list: vi.fn(), create: vi.fn(), update: vi.fn(), remove: vi.fn() },
}));

const CATEGORIES = [
  { id: 'ec1', name: 'Veterinary', description: 'Animal health', expenseCount: 12 },
  { id: 'ec2', name: 'Feed', description: 'Feed purchases', expenseCount: 34 },
];
const INCOME_CATEGORIES = [{ id: 'ic1', name: 'Meat', description: 'Livestock sales', incomeRecordCount: 8 }];
const METHODS = [{ id: 'pm1', name: 'Cash', description: 'Default method', expenseCount: 21 }];

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(expenseCategoriesApi.list).mockResolvedValue({ data: CATEGORIES } as never);
  vi.mocked(incomeCategoriesApi.list).mockResolvedValue({ data: INCOME_CATEGORIES } as never);
  vi.mocked(paymentMethodsApi.list).mockResolvedValue({ data: METHODS } as never);
});

/** Columns sized by a fixed `span={n}` keep their desktop width at every viewport. */
const fixedSpanColumns = (root: HTMLElement) =>
  [...root.querySelectorAll('.ant-col')].filter((col) =>
    [...col.classList].some((cls) => /^ant-col-\d+$/.test(cls)),
  );

describe('CategoriesPage mobile layout', () => {
  // Three `span={8}` cards side by side left each table about 92px of a 412px phone — roughly a
  // fifth of one row per card, with a drag needed in each. They must reflow to one per row.
  it('sizes the three cards through a breakpoint instead of a fixed span', async () => {
    const { container } = render(<CategoriesPage />);
    await waitFor(() => expect(screen.getByText('Veterinary')).toBeInTheDocument());

    expect(fixedSpanColumns(container)).toHaveLength(0);
    const cardColumns = [...container.querySelectorAll('.ant-col')];
    expect(cardColumns).toHaveLength(3);
    for (const col of cardColumns) {
      expect(col.classList.contains('ant-col-xs-24')).toBe(true);
      expect(col.classList.contains('ant-col-md-8')).toBe(true);
    }
  });

  // The test setup reports every media query as unmatched, i.e. a phone: the widest column gives
  // way so the table fits without horizontal scrolling, and the columns that carry meaning — the
  // usage count (which gates Delete) and the row actions — stay.
  it('drops only the description on a phone and keeps the count and actions', async () => {
    const { container } = render(<CategoriesPage />);
    await waitFor(() => expect(screen.getByText('Veterinary')).toBeInTheDocument());

    // Plain DOM queries: the columns are what matters here, and a role query over three tables
    // drags testing-library's accessible-name computation over the whole tree.
    const headers = [...container.querySelectorAll('.ant-table-thead th')].map((th) => th.textContent?.trim());
    expect(headers).not.toContain('Description');
    expect(headers).toContain('Expenses');
    expect(headers).toContain('Records');
    expect(headers).toContain('Uses');

    const actions = [...container.querySelectorAll('.ant-table-tbody .ant-btn')].map((b) => b.textContent?.trim());
    expect(actions).toContain('Edit');
    expect(actions).toContain('Delete');
  });
});
