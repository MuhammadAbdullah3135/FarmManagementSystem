import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, within, waitFor, fireEvent } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import ExpensesPage from './ExpensesPage';
import { expensesApi, expenseCategoriesApi, paymentMethodsApi } from '../../api/finance';
import { lookupsApi } from '../../api/attendance';

vi.mock('../../api/finance', () => ({
  expensesApi: { list: vi.fn(), create: vi.fn(), update: vi.fn(), remove: vi.fn() },
  expenseCategoriesApi: { list: vi.fn(), create: vi.fn() },
  paymentMethodsApi: { list: vi.fn(), create: vi.fn() },
}));

vi.mock('../../api/attendance', () => ({
  lookupsApi: { animals: vi.fn(), locations: vi.fn(), locationTypes: vi.fn() },
}));

const res = (data: unknown) => ({ data }) as never;
type User = ReturnType<typeof userEvent.setup>;

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(expensesApi.list).mockResolvedValue(res({ items: [], totalCount: 0 }));
  vi.mocked(expensesApi.create).mockResolvedValue(res({ id: 'exp-new' }));
  // The scenario this page was reported for: nothing is configured yet, so the
  // category list is empty and the only way forward is to create one here.
  vi.mocked(expenseCategoriesApi.list).mockResolvedValue(res([]));
  vi.mocked(paymentMethodsApi.list).mockResolvedValue(res([{ id: 'pm-1', name: 'Cash' }]));
  vi.mocked(lookupsApi.animals).mockResolvedValue(res({ items: [], totalCount: 0 }));
  vi.mocked(lookupsApi.locations).mockResolvedValue(res([]));
  vi.mocked(lookupsApi.locationTypes).mockResolvedValue(res([]));
});

const addExpenseModal = async (user: User) => {
  await user.click(await screen.findByRole('button', { name: /Add Expense/i }));
  return (await screen.findByText('Add Expense', { selector: '.ant-modal-title' })).closest(
    '.ant-modal',
  ) as HTMLElement;
};

const formItem = (modal: HTMLElement, label: string) =>
  (within(modal).getByText(label, { selector: 'label' }).closest('.ant-form-item') as HTMLElement);

const openSelect = async (user: User, modal: HTMLElement, label: string) => {
  await user.click(within(formItem(modal, label)).getByRole('combobox'));
};

/**
 * The page's own modals do not set `okText`, so their confirm button carries
 * antd's locale label. Address the footer's primary button instead of its text.
 */
const confirm = (modal: HTMLElement) => {
  const button = modal.querySelector('.ant-modal-footer .ant-btn-primary');
  expect(button).toBeTruthy();
  return button as HTMLElement;
};

/** antd v6 renders options as hidden a11y mirrors; click the content element. */
const selectOption = (label: string) => {
  const content = [...document.querySelectorAll('.ant-select-item-option-content')].find(
    (el) => el.textContent === label,
  );
  expect(content).toBeTruthy();
  fireEvent.click(content!.closest('.ant-select-item-option') as HTMLElement);
};

describe('ExpensesPage inline quick-add', () => {
  it('creates a category from the empty form, selects it, and saves the expense against it', async () => {
    vi.mocked(expenseCategoriesApi.create).mockResolvedValue(res({ id: 'ec-1', name: 'Veterinary Services' }));
    // After creation the refetch returns the new category, as the server would.
    vi.mocked(expenseCategoriesApi.list)
      .mockResolvedValueOnce(res([]))
      .mockResolvedValueOnce(res([{ id: 'ec-1', name: 'Veterinary Services' }]));

    const user = userEvent.setup();
    render(<ExpensesPage />);
    const modal = await addExpenseModal(user);

    // The category list is empty, so the field's own footer is the way in.
    await openSelect(user, modal, 'Category');
    await user.click(await screen.findByRole('button', { name: /Add Expense Category/i }));

    const addCategory = (
      await screen.findByText('Add Expense Category', { selector: '.ant-modal-title' })
    ).closest('.ant-modal') as HTMLElement;
    await user.type(within(addCategory).getByLabelText('Name'), 'Veterinary Services');
    await user.click(within(addCategory).getByRole('button', { name: 'Add Expense Category' }));

    expect(expenseCategoriesApi.create).toHaveBeenCalledWith({
      name: 'Veterinary Services',
      description: undefined,
    });
    // The page refetched so the new option survives the next render.
    await waitFor(() => expect(expenseCategoriesApi.list).toHaveBeenCalledTimes(2));
    // ...and it is selected in place: this label can only render from the options
    // list once the form value is the new id.
    expect(within(formItem(modal, 'Category')).getByText('Veterinary Services')).toBeInTheDocument();

    // Finish the expense and confirm the created category is what gets posted.
    await user.type(within(formItem(modal, 'Amount')).getByRole('spinbutton'), '250');
    await openSelect(user, modal, 'Payment Method');
    selectOption('Cash');
    await user.click(confirm(modal));

    await waitFor(() => expect(expensesApi.create).toHaveBeenCalled());
    expect(expensesApi.create).toHaveBeenCalledWith(
      expect.objectContaining({
        amount: 250,
        expenseCategoryId: 'ec-1',
        paymentMethodId: 'pm-1',
      }),
    );
  }, 30000);

  it('offers inline creation on every configured field of the expense form', async () => {
    const user = userEvent.setup();
    render(<ExpensesPage />);
    const modal = await addExpenseModal(user);

    await openSelect(user, modal, 'Category');
    expect(await screen.findByRole('button', { name: /Add Expense Category/i })).toBeInTheDocument();

    await openSelect(user, modal, 'Payment Method');
    expect(await screen.findByRole('button', { name: /Add Payment Method/i })).toBeInTheDocument();

    // Location's inline add is what makes its nested Location Type/status chain reachable.
    await openSelect(user, modal, 'Location (optional)');
    expect(await screen.findByRole('button', { name: /Add Location/i })).toBeInTheDocument();
  }, 30000);
});
