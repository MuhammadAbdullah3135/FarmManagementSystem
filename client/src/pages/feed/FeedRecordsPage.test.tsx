import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import FeedRecordsPage from './FeedRecordsPage';
import { feedRecordsApi, feedTypesApi } from '../../api/feed';
import { lookupsApi } from '../../api/attendance';

vi.mock('../../api/feed', () => ({
  feedRecordsApi: {
    list: vi.fn(),
    create: vi.fn(),
    update: vi.fn(),
    remove: vi.fn(),
  },
  feedTypesApi: { list: vi.fn() },
}));

vi.mock('../../api/attendance', () => ({
  lookupsApi: { animals: vi.fn(), locations: vi.fn(), locationTypes: vi.fn() },
}));

vi.mock('../../api/configuration', () => ({
  flattenLocations: () => [],
}));

const res = (data: unknown) => ({ data }) as never;

const formItem = (modal: HTMLElement, label: string) =>
  modal.querySelector(`.ant-form-item-label label[title="${label}"]`)?.closest('.ant-form-item') as HTMLElement;

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(feedTypesApi.list).mockResolvedValue(
    res([{ id: 'ft-1', name: 'Hay', category: 'Forage', categoryName: 'Forage', unit: 'Kilogram', unitName: 'Kilogram', costPerUnit: 5 }]),
  );
  vi.mocked(lookupsApi.animals).mockResolvedValue(res({ items: [{ id: 'a-1', tagNumber: 'T-1' }] }));
  vi.mocked(lookupsApi.locations).mockResolvedValue(res([]));
  vi.mocked(lookupsApi.locationTypes).mockResolvedValue(res([]));
  vi.mocked(feedRecordsApi.list).mockResolvedValue(res({ items: [], totalCount: 0 }));
  vi.mocked(feedRecordsApi.create).mockResolvedValue(res({ id: 'fr-1' }));
});

describe('FeedRecordsPage record-feeding modal', () => {
  it("shows the feed type's unit in the quantity box and preselects the course from its category", async () => {
    const user = userEvent.setup();
    render(<FeedRecordsPage />);

    await user.click(await screen.findByRole('button', { name: /Record Feeding/i }));
    const modal = (
      await screen.findByText('Record Feeding', { selector: '.ant-modal-title' }, { timeout: 20000 })
    ).closest('.ant-modal') as HTMLElement;

    const feedType = formItem(modal, 'Feed Type');
    await user.click(within(feedType).getByRole('combobox'));
    await user.click(await screen.findByText('Hay (Kilogram)', { selector: '.ant-select-item-option-content' }));

    // The unit rides in the quantity box, in its short form.
    expect(within(formItem(modal, 'Quantity')).getByText('kg')).toBeInTheDocument();

    // A forage feed is the main course until the user says otherwise.
    expect(within(formItem(modal, 'Feed role')).getByRole('radio', { name: 'Main' })).toBeChecked();

    const animal = formItem(modal, 'Animal');
    await user.click(within(animal).getByRole('combobox'));
    await user.click(await screen.findByText('T-1', { selector: '.ant-select-item-option-content' }));

    await user.type(within(formItem(modal, 'Quantity')).getByRole('spinbutton'), '2');

    // One click overrides the derived slot; the course stays as derived.
    await user.click(within(formItem(modal, 'Meal')).getByText('Evening'));

    await user.click(within(modal).getByRole('button', { name: 'OK' }));

    expect(feedRecordsApi.create).toHaveBeenCalledWith(
      expect.objectContaining({ feedTypeId: 'ft-1', animalId: 'a-1', quantity: 2, mealSlot: 'Evening', mealRole: 'Main' }),
    );
  }, 90000);
});
