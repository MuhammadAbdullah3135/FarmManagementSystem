import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import SalaryPaymentsPage from './SalaryPaymentsPage';
import { salaryPaymentsApi, payrollApi, employeesApi } from '../../api/hr';

vi.mock('../../api/hr', () => ({
  salaryPaymentsApi: { list: vi.fn(), record: vi.fn(), remove: vi.fn() },
  payrollApi: { report: vi.fn() },
  employeesApi: { list: vi.fn() },
}));

const EMPLOYEES = [
  { id: 'e1', firstName: 'Ayesha', lastName: 'Khan', phone: '0300', email: 'a@example.com', hireDate: '2025-01-15', salaryType: 'Monthly', salaryRate: 50000, isActive: true },
];

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(employeesApi.list).mockResolvedValue({ data: { items: EMPLOYEES, totalCount: 1, page: 1, pageSize: 100 } } as never);
  vi.mocked(payrollApi.report).mockResolvedValue({
    data: { lines: [], totalGross: 50000, totalNet: 47500, totalPaid: 112000, paymentCount: 3, expectedMonthlyPayroll: 250000 },
  } as never);
  vi.mocked(salaryPaymentsApi.list).mockResolvedValue({ data: { items: [], totalCount: 0, page: 1, pageSize: 10 } } as never);
});

/**
 * The grid column holding the card with this title. Addressed by title rather than by `.ant-row`
 * index, so the filter row above the cards cannot shift what this test is looking at.
 */
const columnOf = (title: string) => {
  const col = screen.getByText(title).closest('.ant-col');
  if (!col) throw new Error(`the "${title}" card is not inside a Column`);
  return col as HTMLElement;
};

/** Columns sized by a fixed `span={n}` keep their desktop width at every viewport. */
const fixedSpanColumns = (cols: HTMLElement[]) =>
  cols.filter((col) => [...col.classList].some((cls) => /^ant-col-\d+$/.test(cls)));

describe('SalaryPaymentsPage mobile layout', () => {
  // `span={16}` + `span={8}` left the payroll card 118px wide on a 412px phone with its table and
  // stats crushed into it. The two cards must stack until the desktop breakpoint, and then keep
  // the 2:1 split they have on a laptop. (The stats inside the payroll card stay half/half at every
  // width by design, so they are not part of this assertion.)
  it('sizes the two cards through breakpoints instead of a fixed span', async () => {
    render(<SalaryPaymentsPage />);
    await waitFor(() => expect(screen.getByText('Payroll Report')).toBeInTheDocument());

    const cols = [columnOf('Salary Payments'), columnOf('Payroll Report')];
    expect(fixedSpanColumns(cols)).toHaveLength(0);
    expect(cols[0].classList.contains('ant-col-xs-24')).toBe(true);
    expect(cols[0].classList.contains('ant-col-lg-16')).toBe(true);
    expect(cols[1].classList.contains('ant-col-xs-24')).toBe(true);
    expect(cols[1].classList.contains('ant-col-lg-8')).toBe(true);
  });
});

/*
 * The payroll range picker is ~340px wide and the report card is a third of the row (lg={8}), so a
 * picker in the card's `extra` squeezed antd's `flex: 1` title down to "P…" — and hung past the
 * card's border — on every window narrower than about 1800px. The range belongs in the page's
 * filter row (which already stacks full width on a phone); only the small Refresh button stays in
 * the head. Encoded as "no wide control is in the head", which is the property that broke.
 */
describe('SalaryPaymentsPage card heads', () => {
  it('keeps the wide date range out of the Payroll Report card head', async () => {
    const { container } = render(<SalaryPaymentsPage />);
    await waitFor(() => expect(screen.getByText('Payroll Report')).toBeInTheDocument());

    const filterRow = container.querySelector('.fms-filter-row');
    expect(filterRow).not.toBeNull();
    expect(filterRow!.querySelectorAll('.ant-picker-range')).toHaveLength(1);

    const head = screen.getByText('Payroll Report').closest('.ant-card-head');
    expect(head).not.toBeNull();
    expect(head!.querySelectorAll('.ant-picker-range')).toHaveLength(0);
    expect([...head!.querySelectorAll('.ant-card-extra button')].map((btn) => btn.textContent))
      .toEqual(['Refresh']);
  });
});
