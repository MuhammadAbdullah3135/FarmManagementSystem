import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor, fireEvent, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import SalaryPaymentsPage from './SalaryPaymentsPage';
import { salaryPaymentsApi, payrollApi, employeesApi } from '../../api/hr';
import type { SalaryPayment } from '../../types';

vi.mock('../../api/hr', () => ({
  salaryPaymentsApi: { list: vi.fn(), record: vi.fn(), remove: vi.fn() },
  payrollApi: { report: vi.fn() },
  employeesApi: { list: vi.fn() },
}));

const EMPLOYEES = [
  { id: 'e1', firstName: 'Ayesha', lastName: 'Khan', phone: '0300', email: 'a@example.com', hireDate: '2025-01-15', salaryType: 'Monthly', salaryRate: 50000, isActive: true },
];

const PAYMENT: SalaryPayment = {
  id: 'p1',
  employeeId: 'e1',
  employeeName: 'Ayesha Khan',
  amount: 50000,
  paymentDate: '2026-09-01T00:00:00Z',
  salaryType: 'Monthly',
  salaryTypeName: 'Monthly',
  paymentType: 'BankTransfer',
  paymentTypeName: 'BankTransfer',
  reference: 'TXN-991',
  periodCovered: '2026-08-01T00:00:00Z',
  notes: 'August wages',
  recordedByName: 'Fatima Noor',
  recordedByEmail: 'bursar@farm.test',
  recordedAt: '2026-09-01T09:00:00Z',
};

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

/*
 * The ledger's whole point is answering "who was paid, by whom, how". A payment row must show
 * its method, reference, covered period and — critically — the person who recorded it, and the
 * record-payment modal must send those fields. Deletion must demand a reason (the server
 * rejects an empty one) and passing it on.
 */

const modal = () => document.querySelector('.ant-modal') as HTMLElement;

const formItem = (label: string) =>
  screen.getByText(label, { selector: 'label' }).closest('.ant-form-item') as HTMLElement;

/** antd v6 renders select options as hidden a11y mirrors; click the content element. */
const findOptionContent = (label: string) => {
  for (const dropdown of [...document.querySelectorAll('.ant-select-dropdown')].reverse()) {
    const match = within(dropdown as HTMLElement).queryAllByText(label, { selector: '.ant-select-item-option-content' });
    if (match.length > 0) return match[0];
  }
  return null;
};

const selectOption = async (label: string) => {
  await waitFor(() => expect(findOptionContent(label)).not.toBeNull());
  fireEvent.click(findOptionContent(label)!.closest('.ant-select-item-option') as HTMLElement);
};

/** The employee picker lives in the Salary Payments card head; the table needs it chosen. */
const selectEmployee = async (user: { click: (el: Element) => Promise<void> }) => {
  const head = screen.getByText('Salary Payments').closest('.ant-card-head') as HTMLElement;
  await user.click(within(head).getByRole('combobox'));
  await selectOption('Ayesha Khan');
  await waitFor(() => expect(salaryPaymentsApi.list).toHaveBeenCalled());
};

describe('SalaryPaymentsPage ledger record', () => {
  it('shows payment method, reference, period and who recorded each payment', async () => {
    vi.mocked(salaryPaymentsApi.list).mockResolvedValue({
      data: { items: [PAYMENT], totalCount: 1, page: 1, pageSize: 10 },
    } as never);
    const user = userEvent.setup();
    render(<SalaryPaymentsPage />);

    await selectEmployee(user);

    expect(await screen.findByText('Bank Transfer')).toBeInTheDocument();
    expect(screen.getByText('TXN-991')).toBeInTheDocument();
    expect(screen.getByText('Fatima Noor')).toBeInTheDocument();
  });

  it('records a payment with the ledger fields and refreshes the payroll report', async () => {
    const user = userEvent.setup();
    render(<SalaryPaymentsPage />);

    await selectEmployee(user);
    await user.click(await screen.findByRole('button', { name: /record payment/i }));

    await user.type(await screen.findByLabelText('Amount'), '50000');
    await user.click(within(formItem('Payment Method')).getByRole('combobox'));
    await selectOption('Bank Transfer');
    await user.type(screen.getByLabelText('Reference'), 'TXN-777');

    await user.click(modal().querySelector('.ant-modal-footer .ant-btn-primary') as HTMLElement);

    await waitFor(() => expect(salaryPaymentsApi.record).toHaveBeenCalled());
    const payload = vi.mocked(salaryPaymentsApi.record).mock.calls[0][1];
    expect(payload.amount).toBe(50000);
    expect(payload.paymentType).toBe('BankTransfer');
    expect(payload.reference).toBe('TXN-777');

    // The report reloads after the write, so its totals are never stale.
    await waitFor(() =>
      expect(vi.mocked(payrollApi.report).mock.calls.length).toBeGreaterThanOrEqual(2));
  });

  it('demands a reason before deleting a payment and sends it', async () => {
    vi.mocked(salaryPaymentsApi.list).mockResolvedValue({
      data: { items: [PAYMENT], totalCount: 1, page: 1, pageSize: 10 },
    } as never);
    const user = userEvent.setup();
    render(<SalaryPaymentsPage />);

    await selectEmployee(user);
    await user.click(await screen.findByRole('button', { name: 'Delete' }));

    // With no reason typed, the popover's OK stays disabled and the API is untouched.
    expect(await screen.findByRole('button', { name: 'OK' })).toBeDisabled();
    expect(salaryPaymentsApi.remove).not.toHaveBeenCalled();

    fireEvent.change(document.querySelector('[data-testid="delete-reason-p1"]')!, {
      target: { value: 'Wrong employee' },
    });
    await waitFor(() => expect(screen.getByRole('button', { name: 'OK' })).toBeEnabled());
    await user.click(screen.getByRole('button', { name: 'OK' }));

    await waitFor(() => expect(salaryPaymentsApi.remove).toHaveBeenCalledWith('e1', 'p1', 'Wrong employee'));
    // The popover interaction is slow in jsdom under a fully parallel suite, so
    // this one test gets more headroom than the 5s default (see MembersPage.test.tsx).
  }, 20000);
});
