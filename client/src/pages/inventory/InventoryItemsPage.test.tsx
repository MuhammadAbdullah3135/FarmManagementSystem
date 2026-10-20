import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, within } from '@testing-library/react';
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
    await user.type(within(formItem(modal, 'Unit')).getByRole('textbox'), 'rolls');

    const location = formItem(modal, 'Location');
    await user.click(within(location).getByRole('combobox'));
    await user.click(await screen.findByText('Main Barn', { selector: '.ant-select-item-option-content' }));

    await user.click(within(modal).getByRole('button', { name: 'OK' }));

    expect(inventoryApi.create).toHaveBeenCalledWith(
      expect.objectContaining({ name: 'Fence wire', unit: 'rolls', location: 'Main Barn' }),
    );
  }, 90000);
});
