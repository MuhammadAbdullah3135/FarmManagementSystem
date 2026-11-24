import { describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import '../../i18n/index';
import BirthRecordingPage from './BirthRecordingPage';
import { breedingRecordsApi, gestationApi } from '../../api/breeding';

vi.mock('../../api/attendance', () => ({
  lookupsApi: {
    animals: vi.fn(() =>
      Promise.resolve({
        data: {
          items: [
            { id: 'dam-1', tagNumber: 'SR-1001', name: 'Duna' },
            { id: 'dam-2', tagNumber: 'SR-1002', name: 'Fadila' },
          ],
        },
      }),
    ),
    sexOptions: vi.fn(() =>
      Promise.resolve({ data: [{ id: 'female', value: 'Female' }, { id: 'male', value: 'Male' }] }),
    ),
  },
}));

vi.mock('../../api/breeding', () => ({
  birthsApi: {
    list: vi.fn(() => Promise.resolve({ data: { items: [], totalCount: 0 } })),
    delete: vi.fn(() => Promise.resolve({})),
    create: vi.fn(() => Promise.resolve({})),
  },
  gestationApi: { list: vi.fn(() => Promise.resolve({ data: { items: [] } })) },
  breedingRecordsApi: { list: vi.fn(() => Promise.resolve({ data: { items: [] } })) },
}));

describe('the birth form lineage links', () => {
  it('asks for the dam first and only loads links that belong to the chosen dam', async () => {
    const user = userEvent.setup();
    render(
      <MemoryRouter initialEntries={['/dashboard/breeding/births']}>
        <BirthRecordingPage />
      </MemoryRouter>,
    );

    await user.click(screen.getByRole('button', { name: /plus/i }));
    const dialog = await screen.findByRole('dialog');
    await waitFor(() => expect(dialog.textContent).toContain('Add Offspring'));

    // The rule the server enforces is stated up front instead of only in an error toast.
    expect(dialog.textContent).toContain('Link at least one of these two records');

    // Nothing can be linked before a dam is chosen, so nothing is requested either.
    expect(gestationApi.list).not.toHaveBeenCalled();
    expect(breedingRecordsApi.list).not.toHaveBeenCalled();

    const damSelect = screen.getAllByRole('combobox')[0];
    await user.click(damSelect);
    await user.click(await screen.findByText('SR-1001 - Duna'));

    await waitFor(() =>
      expect(gestationApi.list).toHaveBeenCalledWith(
        expect.objectContaining({ damId: 'dam-1', activeOnly: true }),
      ),
    );
    expect(breedingRecordsApi.list).toHaveBeenCalledWith(
      expect.objectContaining({ damId: 'dam-1' }),
    );

    // Switching the dam reloads the links for the new dam instead of keeping the old ones.
    await user.click(screen.getAllByRole('combobox')[0]);
    await user.click(await screen.findByText('SR-1002 - Fadila'));
    await waitFor(() =>
      expect(gestationApi.list).toHaveBeenLastCalledWith(
        expect.objectContaining({ damId: 'dam-2' }),
      ),
    );
  }, 30000);
});
