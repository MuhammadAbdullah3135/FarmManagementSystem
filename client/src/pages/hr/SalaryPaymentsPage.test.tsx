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

const outerColumns = (root: HTMLElement) => [...([...root.querySelectorAll('.ant-row')][0].children as unknown as HTMLElement[])];

/** Columns sized by a fixed `span={n}` keep their desktop width at every viewport. */
const fixedSpanColumns = (cols: HTMLElement[]) =>
  cols.filter((col) => [...col.classList].some((cls) => /^ant-col-\d+$/.test(cls)));

describe('SalaryPaymentsPage mobile layout', () => {
  // `span={16}` + `span={8}` left the payroll card 118px wide on a 412px phone with its table and
  // stats crushed into it. The two cards must stack until the desktop breakpoint, and then keep
  // the 2:1 split they have on a laptop. (The stats inside the payroll card stay half/half at every
  // width by design, so they are not part of this assertion.)
  it('sizes the two cards through breakpoints instead of a fixed span', async () => {
    const { container } = render(<SalaryPaymentsPage />);
    await waitFor(() => expect(screen.getByText('Payroll Report')).toBeInTheDocument());

    const cols = outerColumns(container);
    expect(fixedSpanColumns(cols)).toHaveLength(0);
    expect(cols).toHaveLength(2);
    expect(cols[0].classList.contains('ant-col-xs-24')).toBe(true);
    expect(cols[0].classList.contains('ant-col-lg-16')).toBe(true);
    expect(cols[1].classList.contains('ant-col-xs-24')).toBe(true);
    expect(cols[1].classList.contains('ant-col-lg-8')).toBe(true);
  });
});
