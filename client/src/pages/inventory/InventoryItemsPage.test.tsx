import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import InventoryItemsPage from './InventoryItemsPage';
import { inventoryApi } from '../../api/inventory';
import { configurationApi } from '../../api/configuration';

vi.mock('../../api/inventory', () => ({
  inventoryApi: {
    list: vi.fn(),
    create: vi.fn(),
    update: vi.fn(),
    remove: vi.fn(),
    recordMovement: vi.fn(),
    categories: vi.fn(),
    createCategory: vi.fn(),
    updateCategory: vi.fn(),
  },
}));

vi.mock('../../api/configuration', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../api/configuration')>();
  return {
    ...actual,
    configurationApi: {
      ...actual.configurationApi,
      locations: vi.fn(),
    },
  };
});

const res = (data: unknown) => ({ data }) as never;
const formItem = (modal: HTMLElement, label: string) =>
  modal.querySelector(`.ant-form-item-label label[title="${label}"]`)?.closest('.ant-form-item') as HTMLElement;

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(inventoryApi.list).mockResolvedValue(res({ items: [], totalCount: 0 }));
  vi.mocked(inventoryApi.create).mockResolvedValue(res({ id: 'item-1' }));
  vi.mocked(inventoryApi.categories).mockResolvedValue(res([]));
  vi.mocked(inventoryApi.createCategory).mockResolvedValue(res({ id: 'cat-new' }));
  vi.mocked(inventoryApi.updateCategory).mockResolvedValue(res({ id: 'cat-1' }));
  vi.mocked(configurationApi.locations).mockResolvedValue(
    res([
      { id: 'loc-1', name: 'Main Barn', locationTypeId: 'lt-1', childLocations: [] },
    ]),
  );
});

describe('InventoryItemsPage location picker', () => {
  it('loads configuration locations, stores the selected name, and sends it on create', async () => {
    const user = userEvent.setup();
    render(<MemoryRouter><InventoryItemsPage /></MemoryRouter>);

    await user.click(await screen.findByRole('button', { name: /Add item/i }));

    const modal = (
      await screen.findByText(/Add inventory item/i, { selector: '.ant-modal-title' })
    ).closest('.ant-modal') as HTMLElement;

    expect(configurationApi.locations).toHaveBeenCalled();

    await user.type(within(formItem(modal, 'Name')).getByRole('textbox'), 'Fence wire');

    const unit = formItem(modal, 'Unit');
    await user.click(within(unit).getByRole('combobox'));
    await user.click(await screen.findByText('Roll', { selector: '.ant-select-item-option-content' }));

    const location = formItem(modal, 'Location');
    await user.click(within(location).getByRole('combobox'));
    await user.click(await screen.findByText('Main Barn', { selector: '.ant-select-item-option-content' }));

    await user.click(within(modal).getByRole('button', { name: 'OK' }));

    expect(inventoryApi.create).toHaveBeenCalledWith(
      expect.objectContaining({ name: 'Fence wire', unit: 'Roll', location: 'Main Barn' }),
    );
  }, 90000);

  it('stores a preset adjustment reason on the stock movement', async () => {
    vi.mocked(inventoryApi.list).mockResolvedValue(res({
      items: [{
        id: 'item-1',
        name: 'Fence wire',
        category: 'Supplies',
        unit: 'Roll',
        quantity: 4,
        reorderLevel: 1,
        unitCost: 10,
        location: 'Main Barn',
        stockValue: 40,
        isLowStock: false,
      }],
      totalCount: 1,
    }));
    const user = userEvent.setup();
    render(<MemoryRouter><InventoryItemsPage /></MemoryRouter>);

    await user.click(await screen.findByRole('button', { name: /Adjust Stock/i }));
    const modal = (
      await screen.findByText(/Adjust stock/i, { selector: '.ant-modal-title' })
    ).closest('.ant-modal') as HTMLElement;

    await user.type(within(formItem(modal, 'Quantity')).getByRole('spinbutton'), '-1');

    const suggestedReason = formItem(modal, 'Suggested reason');
    await user.click(within(suggestedReason).getByRole('combobox'));
    await user.click(await screen.findByText('Damaged', { selector: '.ant-select-item-option-content' }));

    // The original free-text reason field is still there and receives the preset.
    expect((within(formItem(modal, 'Reason')).getByRole('textbox') as HTMLTextAreaElement).value)
      .toBe('Damaged');

    await user.click(within(modal).getByRole('button', { name: 'OK' }));

    expect(inventoryApi.recordMovement).toHaveBeenCalledWith(
      'item-1',
      expect.objectContaining({ reason: 'Damaged', quantity: -1 }),
    );
  }, 90000);

  it('loads managed categories and sends both the id and the name on save', async () => {
    vi.mocked(inventoryApi.categories).mockResolvedValue(res([{
      id: 'cat-1',
      farmId: 'farm-1',
      name: 'Supplies',
      isActive: true,
      itemCount: 0,
      createdAt: '2026-10-20T00:00:00Z',
    }]));

    const user = userEvent.setup();
    render(<MemoryRouter><InventoryItemsPage /></MemoryRouter>);

    await screen.findByText('Supplies', { selector: '.ant-table-cell span' });
    await user.click(await screen.findByRole('button', { name: /Add item/i }));

    const modal = (
      await screen.findByText(/Add inventory item/i, { selector: '.ant-modal-title' })
    ).closest('.ant-modal') as HTMLElement;

    await user.type(within(formItem(modal, 'Name')).getByRole('textbox'), 'Fence wire');

    const unit = formItem(modal, 'Unit');
    await user.click(within(unit).getByRole('combobox'));
    await user.click(await screen.findByText('Roll', { selector: '.ant-select-item-option-content' }));

    const category = formItem(modal, 'Category');
    await user.click(within(category).getByRole('combobox'));
    await user.click(await screen.findByText('Supplies', { selector: '.ant-select-item-option-content' }));

    await waitFor(() => {
      expect((within(category).getByPlaceholderText('Or type a category') as HTMLInputElement).value)
        .toBe('Supplies');
    });

    await user.click(within(modal).getByRole('button', { name: 'OK' }));

    expect(inventoryApi.create).toHaveBeenCalledWith(
      expect.objectContaining({
        name: 'Fence wire',
        unit: 'Roll',
        categoryId: 'cat-1',
        category: 'Supplies',
      }),
    );
  }, 90000);

  it('adds, renames, and deactivates categories from the management section', async () => {
    const category = {
      id: 'cat-1',
      farmId: 'farm-1',
      name: 'Supplies',
      description: 'Day-to-day stock',
      isActive: true,
      itemCount: 2,
      createdAt: '2026-10-20T00:00:00Z',
    };
    vi.mocked(inventoryApi.categories).mockResolvedValue(res([category]));

    const user = userEvent.setup();
    render(<MemoryRouter><InventoryItemsPage /></MemoryRouter>);

    await screen.findByText('Supplies', { selector: '.ant-table-cell span' });

    await user.click(screen.getByRole('button', { name: /Add category/i }));
    let modal = (
      await screen.findByText(/Add category/i, { selector: '.ant-modal-title' })
    ).closest('.ant-modal') as HTMLElement;
    await user.type(within(formItem(modal, 'Name')).getByRole('textbox'), 'Tools');
    await user.click(within(modal).getByRole('button', { name: 'OK' }));
    expect(inventoryApi.createCategory).toHaveBeenCalledWith(
      expect.objectContaining({ name: 'Tools' }),
    );

    await user.click(await screen.findByRole('button', { name: 'Edit' }));
    modal = (
      await screen.findByText(/Edit category/i, { selector: '.ant-modal-title' })
    ).closest('.ant-modal') as HTMLElement;
    const nameInput = within(formItem(modal, 'Name')).getByRole('textbox');
    await user.clear(nameInput);
    await user.type(nameInput, 'Tools and supplies');
    await user.click(within(modal).getByRole('button', { name: 'OK' }));
    expect(inventoryApi.updateCategory).toHaveBeenCalledWith(
      'cat-1',
      expect.objectContaining({ name: 'Tools and supplies', isActive: true }),
    );

    await user.click(await screen.findByRole('button', { name: 'Deactivate' }));
    expect(inventoryApi.updateCategory).toHaveBeenCalledWith(
      'cat-1',
      expect.objectContaining({ name: 'Supplies', isActive: false }),
    );
  }, 90000);
});
