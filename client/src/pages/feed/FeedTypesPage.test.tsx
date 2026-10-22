import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import FeedTypesPage from './FeedTypesPage';
import { feedTypesApi, feedInventoryApi } from '../../api/feed';
import { suppliersApi } from '../../api/suppliers';

vi.mock('../../api/feed', () => ({
  feedTypesApi: {
    list: vi.fn(),
    create: vi.fn(),
    update: vi.fn(),
    remove: vi.fn(),
  },
  feedInventoryApi: {
    stock: vi.fn(),
    movements: vi.fn(),
    recordMovement: vi.fn(),
  },
}));

vi.mock('../../api/suppliers', () => ({
  suppliersApi: { list: vi.fn() },
}));

const res = (data: unknown) => ({ data }) as never;
const formItem = (modal: HTMLElement, label: string) =>
  modal.querySelector(`.ant-form-item-label label[title="${label}"]`)?.closest('.ant-form-item') as HTMLElement;

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(feedTypesApi.list).mockResolvedValue(
    res([{ id: 'ft-1', name: 'Starter', categoryName: 'Concentrate', unitName: 'kg', costPerUnit: 1, notes: '' }]),
  );
  vi.mocked(feedInventoryApi.stock).mockResolvedValue(res([]));
  vi.mocked(feedInventoryApi.movements).mockResolvedValue(res({ items: [], totalCount: 0 }));
  vi.mocked(feedInventoryApi.recordMovement).mockResolvedValue(res({ id: 'mv-1' }));
  vi.mocked(suppliersApi.list).mockResolvedValue(
    res({ items: [{ id: 'sup-1', name: 'Feed Co', purchaseCount: 0, totalPurchased: 0 }], totalCount: 1 }),
  );
});

describe('FeedTypesPage supplier picker', () => {
  it('loads suppliers, stores the selected name, and sends it with a stock movement', async () => {
    const user = userEvent.setup();
    render(<MemoryRouter><FeedTypesPage /></MemoryRouter>);

    await user.click(await screen.findByRole('button', { name: /Adjust Stock/i }));

    const modal = (
      await screen.findByText(/Adjust Stock/, { selector: '.ant-modal-title' }, { timeout: 20000 })
    ).closest('.ant-modal') as HTMLElement;

    expect(suppliersApi.list).toHaveBeenCalledWith({ pageSize: 100 });

    await user.type(within(formItem(modal, 'Quantity')).getByRole('spinbutton'), '25');

    const supplier = formItem(modal, 'Supplier');
    await user.click(within(supplier).getByRole('combobox'));
    await user.click(await screen.findByText('Feed Co', { selector: '.ant-select-item-option-content' }));

    await user.click(within(modal).getByRole('button', { name: 'OK' }));

    expect(feedInventoryApi.recordMovement).toHaveBeenCalledWith(
      expect.objectContaining({ feedTypeId: 'ft-1', supplier: 'Feed Co', quantity: 25 }),
    );
  }, 90000);
});
