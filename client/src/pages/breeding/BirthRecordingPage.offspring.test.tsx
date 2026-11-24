import { describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import '../../i18n/index';
import BirthRecordingPage from './BirthRecordingPage';

vi.mock('../../api/attendance', () => ({
  lookupsApi: {
    animals: vi.fn(() =>
      Promise.resolve({ data: { items: [{ id: 'dam-1', tagNumber: 'SR-2001', name: 'Fadila' }] } }),
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

const openModal = async (user: ReturnType<typeof userEvent.setup>) => {
  await user.click(screen.getByRole('button', { name: /plus/i }));
  const dialog = await screen.findByRole('dialog');
  await waitFor(() => expect(dialog.textContent).toContain('Add Offspring'));
  return dialog;
};

const offspringHeading = (dialog: HTMLElement) =>
  Array.from(dialog.querySelectorAll('.ant-card-head-title'))
    .map((el) => el.textContent ?? '')
    .find((text) => text.startsWith('Offspring ('));

describe('the birth form offspring rows', () => {
  it('starts with one row and adds or removes rows from the Add Offspring button', async () => {
    const user = userEvent.setup();
    render(
      <MemoryRouter initialEntries={['/dashboard/breeding/births']}>
        <BirthRecordingPage />
      </MemoryRouter>,
    );

    const dialog = await openModal(user);
    await waitFor(() => expect(offspringHeading(dialog)).toBe('Offspring (1)'));

    await user.click(screen.getByRole('button', { name: /add offspring/i }));
    await waitFor(() => expect(offspringHeading(dialog)).toBe('Offspring (2)'));

    await user.click(screen.getByRole('button', { name: /add offspring/i }));
    await waitFor(() => expect(offspringHeading(dialog)).toBe('Offspring (3)'));

    await user.click(screen.getByRole('button', { name: /remove offspring 2/i }));
    await waitFor(() => expect(offspringHeading(dialog)).toBe('Offspring (2)'));
  }, 30000);
});
