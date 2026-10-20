import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import MedicinesPage from './MedicinesPage';
import { medicinesApi } from '../../api/health';
import { suppliersApi } from '../../api/suppliers';

vi.mock('../../api/health', () => ({
  medicinesApi: {
    list: vi.fn(),
    getStock: vi.fn(),
    addStock: vi.fn(),
    remove: vi.fn(),
    get: vi.fn(),
    create: vi.fn(),
    update: vi.fn(),
    deleteStock: vi.fn(),
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
  vi.mocked(medicinesApi.list).mockResolvedValue(
    res({
      items: [{ id: 'm-1', name: 'Ivermectin', unit: 'ml', totalQuantity: 10, lowStockThreshold: 2, batchCount: 1 }],
      totalCount: 1,
    }),
  );
  vi.mocked(medicinesApi.getStock).mockResolvedValue(res([]));
  vi.mocked(medicinesApi.addStock).mockResolvedValue(res({ id: 's-1' }));
  vi.mocked(suppliersApi.list).mockResolvedValue(
    res({ items: [{ id: 'sup-1', name: 'Agro Vet', purchaseCount: 0, totalPurchased: 0 }], totalCount: 1 }),
  );
});

describe('MedicinesPage unit picker', () => {
  it('offers units already in use, stores the selected unit, and sends it on save', async () => {
    const user = userEvent.setup();
    render(<MemoryRouter><MedicinesPage /></MemoryRouter>);

    await user.click(await screen.findByRole('button', { name: /Add Medicine/i }));

    const modal = (
      await screen.findByText('Add Medicine', { selector: '.ant-modal-title' })
    ).closest('.ant-modal') as HTMLElement;

    await user.type(within(formItem(modal, 'Name')).getByRole('textbox'), 'Penicillin');

    const unit = formItem(modal, 'Unit');
    await user.click(within(unit).getByRole('combobox'));
    await user.click(await screen.findByText('ml', { selector: '.ant-select-item-option-content' }));

    await user.click(within(modal).getByRole('button', { name: 'OK' }));

    expect(medicinesApi.create).toHaveBeenCalledWith(
      expect.objectContaining({ name: 'Penicillin', unit: 'ml' }),
    );
  }, 30000);
});

describe('MedicinesPage supplier picker', () => {
  it('loads suppliers, stores the selected name, and sends it when adding a batch', async () => {
    const user = userEvent.setup();
    render(<MemoryRouter><MedicinesPage /></MemoryRouter>);

    await user.click(await screen.findByRole('button', { name: 'Stock' }));
    await user.click(await screen.findByRole('button', { name: /Add Batch/i }));

    const modal = (
      await screen.findByText('Add Stock Batch', { selector: '.ant-modal-title' })
    ).closest('.ant-modal') as HTMLElement;

    expect(suppliersApi.list).toHaveBeenCalledWith({ pageSize: 100 });

    await user.type(within(formItem(modal, 'Batch Number')).getByRole('textbox'), 'B-100');
    await user.type(within(formItem(modal, 'Quantity')).getByRole('spinbutton'), '5');
    await user.type(within(formItem(modal, 'Unit Cost ($)')).getByRole('spinbutton'), '3');

    const expiry = formItem(modal, 'Expiry Date').querySelector('input') as HTMLInputElement;
    await user.click(expiry);
    await user.type(expiry, '2030-01-01');
    await user.keyboard('{Enter}');

    const supplier = formItem(modal, 'Supplier');
    await user.click(within(supplier).getByRole('combobox'));
    await user.click(await screen.findByText('Agro Vet', { selector: '.ant-select-item-option-content' }));

    await user.click(within(modal).getByRole('button', { name: 'OK' }));

    expect(medicinesApi.addStock).toHaveBeenCalledWith(
      'm-1',
      expect.objectContaining({ batchNumber: 'B-100', supplier: 'Agro Vet', quantity: 5 }),
    );
  }, 30000);
});
