import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import AnimalsPage from './AnimalsPage';
import { animalsApi } from '../../api/animals';
import { lookupsApi } from '../../api/attendance';

vi.mock('../../api/animals', () => ({
  animalsApi: {
    list: vi.fn(),
    create: vi.fn(),
    update: vi.fn(),
    delete: vi.fn(),
  },
}));

vi.mock('../../api/attendance', () => ({
  lookupsApi: {
    animalTypes: vi.fn(),
    sexOptions: vi.fn(),
    statuses: vi.fn(),
    locations: vi.fn(),
    ageCategories: vi.fn(),
    animals: vi.fn(),
    breeds: vi.fn(),
  },
}));

const res = (data: unknown) => ({ data }) as never;

beforeEach(() => {
  vi.mocked(animalsApi.list).mockResolvedValue(res({ items: [], totalCount: 0 }));
  vi.mocked(lookupsApi.animalTypes).mockResolvedValue(res([{ id: 't1', name: 'Cattle' }]));
  vi.mocked(lookupsApi.sexOptions).mockResolvedValue(res([{ id: 's1', value: 'Female' }]));
  vi.mocked(lookupsApi.statuses).mockResolvedValue(res([{ id: 'st1', name: 'Healthy' }]));
  vi.mocked(lookupsApi.locations).mockResolvedValue(res([{ id: 'l1', name: 'Barn' }]));
  vi.mocked(lookupsApi.ageCategories).mockResolvedValue(res([
    { id: 'ac-1', name: 'Calf' },
    { id: 'ac-2', name: 'Adult' },
  ]));
  vi.mocked(lookupsApi.animals).mockResolvedValue(res({ items: [], totalCount: 0 }));
  vi.mocked(lookupsApi.breeds).mockResolvedValue(res([]));
});

describe('AnimalsPage create form', () => {
  it('loads age category options into the Age Category selector', async () => {
    const user = userEvent.setup();
    render(
      <MemoryRouter>
        <AnimalsPage />
      </MemoryRouter>
    );

    // Open the create-animal modal.
    await user.click(await screen.findByRole('button', { name: /add animal/i }));

    // Open the Age Category select inside its form item.
    const ageLabel = await screen.findByText('Age Category');
    const formItem = ageLabel.closest('.ant-form-item') as HTMLElement;
    expect(formItem).not.toBeNull();
    await user.click(within(formItem).getByRole('combobox'));

    // The lookup options must render in the dropdown.
    expect(await screen.findByRole('option', { name: 'Calf' })).toBeInTheDocument();
    expect(screen.getByRole('option', { name: 'Adult' })).toBeInTheDocument();
  });
});
