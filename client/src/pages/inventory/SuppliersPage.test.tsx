import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, within, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import SuppliersPage from './SuppliersPage';
import { inventoryApi } from '../../api/inventory';
import { suppliersApi } from '../../api/suppliers';
import { expenseCategoriesApi, paymentMethodsApi } from '../../api/finance';

vi.mock('../../api/inventory', () => ({ inventoryApi: { list: vi.fn() } }));
vi.mock('../../api/suppliers', () => ({
  suppliersApi: {
    list: vi.fn(),
    purchases: vi.fn(),
    create: vi.fn(),
    update: vi.fn(),
    remove: vi.fn(),
    createPurchase: vi.fn(),
  },
}));
vi.mock('../../api/finance', () => ({
  expenseCategoriesApi: { list: vi.fn() },
  paymentMethodsApi: { list: vi.fn() },
}));

const res = (data: unknown) => ({ data }) as never;
type User = ReturnType<typeof userEvent.setup>;

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(suppliersApi.list).mockResolvedValue(
    res({
      items: [
        { id: 'sup-1', name: 'Agri Supplies', purchaseCount: 3, totalPurchased: 120.5 },
      ],
      totalCount: 1,
    }),
  );
  vi.mocked(suppliersApi.purchases).mockResolvedValue(res({ items: [], totalCount: 0 }));
  vi.mocked(inventoryApi.list).mockResolvedValue(res({ items: [], totalCount: 0 }));
  vi.mocked(expenseCategoriesApi.list).mockResolvedValue(res([{ id: 'ec-1', name: 'Veterinary Services' }]));
  vi.mocked(paymentMethodsApi.list).mockResolvedValue(res([{ id: 'pm-1', name: 'Bank Transfer' }]));
});

const purchaseModal = async (user: User) => {
  await user.click(await screen.findByRole('button', { name: /Record purchase/i }));
  return (
    await screen.findByText('Record supplier purchase', { selector: '.ant-modal-title' })
  ).closest('.ant-modal') as HTMLElement;
};

const formItem = (modal: HTMLElement, label: string) =>
  modal.querySelector(`.ant-form-item-label label[title="${label}"]`)?.closest('.ant-form-item') as HTMLElement;

/**
 * The quick-add footer button, looked up inside the open dropdown portals only:
 * the page's toolbar has its own "Add supplier" button with a similar name.
 */
const footerButton = (name: RegExp) =>
  waitFor(() => {
    for (const dropdown of document.querySelectorAll('.ant-select-dropdown')) {
      const button = within(dropdown as HTMLElement).queryByRole('button', { name });
      if (button) return button;
    }
    throw new Error(`no dropdown footer button matching ${String(name)} yet`);
  });

/** Options live in the select's dropdown portal, so the select must be open. */
const optionLabels = () =>
  [...document.querySelectorAll('.ant-select-item-option-content')].map((el) => el.textContent);

describe('SuppliersPage purchase form', () => {
  it('loads the configurable fields instead of rendering empty selects', async () => {
    const user = userEvent.setup();
    render(<SuppliersPage />);
    const modal = await purchaseModal(user);

    // Both lists used to be rendered with `options={[]}`, so the two fields were
    // impossible to fill even though the API accepts them.
    expect(expenseCategoriesApi.list).toHaveBeenCalled();
    expect(paymentMethodsApi.list).toHaveBeenCalled();

    await user.click(within(formItem(modal, 'Expense category (optional)')).getByRole('combobox'));
    expect(optionLabels()).toContain('Veterinary Services');

    await user.click(within(formItem(modal, 'Payment method (optional)')).getByRole('combobox'));
    expect(optionLabels()).toContain('Bank Transfer');
  }, 30000);

  it('lets a missing supplier, category or payment method be created from the purchase form', async () => {
    const user = userEvent.setup();
    render(<SuppliersPage />);
    const modal = await purchaseModal(user);

    await user.click(within(formItem(modal, 'Supplier')).getByRole('combobox'));
    expect(await footerButton(/Add Supplier/i)).toBeInTheDocument();

    await user.click(within(formItem(modal, 'Expense category (optional)')).getByRole('combobox'));
    expect(await footerButton(/Add Expense Category/i)).toBeInTheDocument();

    await user.click(within(formItem(modal, 'Payment method (optional)')).getByRole('combobox'));
    expect(await footerButton(/Add Payment Method/i)).toBeInTheDocument();
  }, 30000);
});
