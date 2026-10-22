import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import SuppliersPage from './SuppliersPage';
import { suppliersApi } from '../../api/suppliers';
import { suggestionsApi } from '../../api/suggestions';
import { inventoryApi } from '../../api/inventory';
import { expenseCategoriesApi, paymentMethodsApi } from '../../api/finance';

vi.mock('../../api/suppliers', () => ({
  suppliersApi: {
    list: vi.fn(),
    purchases: vi.fn(),
    create: vi.fn(),
    update: vi.fn(),
    remove: vi.fn(),
  },
}));

vi.mock('../../api/inventory', () => ({
  inventoryApi: { list: vi.fn() },
}));

vi.mock('../../api/finance', () => ({
  expenseCategoriesApi: { list: vi.fn() },
  paymentMethodsApi: { list: vi.fn() },
}));

vi.mock('../../api/suggestions', () => ({
  suggestionsApi: { list: vi.fn() },
}));

const res = (data: unknown) => ({ data }) as never;
const formItem = (modal: HTMLElement, label: string) =>
  modal.querySelector(`.ant-form-item-label label[title="${label}"]`)?.closest('.ant-form-item') as HTMLElement;

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(suppliersApi.list).mockResolvedValue(res({ items: [], totalCount: 0 }) as never);
  vi.mocked(suppliersApi.purchases).mockResolvedValue(res({ items: [], totalCount: 0 }) as never);
  vi.mocked(suppliersApi.create).mockResolvedValue(res({ id: 'sup-1' }) as never);
  vi.mocked(inventoryApi.list).mockResolvedValue(res({ items: [], totalCount: 0 }) as never);
  vi.mocked(expenseCategoriesApi.list).mockResolvedValue(res([]) as never);
  vi.mocked(paymentMethodsApi.list).mockResolvedValue(res([]) as never);
  vi.mocked(suggestionsApi.list).mockResolvedValue(res(['Vaccines, Dewormers']) as never);
});

describe('SuppliersPage products picker', () => {
  it('loads recent products, stores the chosen value, and sends it on save', async () => {
    const user = userEvent.setup();
    render(<MemoryRouter><SuppliersPage /></MemoryRouter>);

    await user.click(await screen.findByRole('button', { name: /Add supplier/i }));

    const modal = (
      await screen.findByText('Add supplier', { selector: '.ant-modal-title' })
    ).closest('.ant-modal') as HTMLElement;

    await user.type(within(formItem(modal, 'Name')).getByRole('textbox'), 'Agro Vet');

    expect(suggestionsApi.list).toHaveBeenCalledWith('supplierProductsSupplied');
    const products = formItem(modal, 'Products supplied');
    await user.click(within(products).getByRole('combobox'));
    await user.click(
      await screen.findByText('Vaccines, Dewormers', { selector: '.ant-select-item-option-content' }),
    );

    await user.click(within(modal).getByRole('button', { name: 'OK' }));

    expect(suppliersApi.create).toHaveBeenCalledWith(
      expect.objectContaining({ name: 'Agro Vet', productsSupplied: 'Vaccines, Dewormers' }),
    );
  }, 30000);
});
