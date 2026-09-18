import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, within, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import ConfigurationPage from './ConfigurationPage';
import { configurationApi } from '../../api/configuration';

vi.mock('../../api/configuration', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../api/configuration')>();
  return {
    ...actual,
    configurationApi: {
      ...actual.configurationApi,
      animalTypes: vi.fn(),
      breeds: vi.fn(),
      sexOptions: vi.fn(),
      ageCategories: vi.fn(),
      statuses: vi.fn(),
      locationTypes: vi.fn(),
      locations: vi.fn(),
      deleteAnimalType: vi.fn(),
      deleteLocationType: vi.fn(),
    },
  };
});

const res = (data: unknown) => ({ data }) as never;
type User = ReturnType<typeof userEvent.setup>;

const seedLists = () => {
  vi.mocked(configurationApi.animalTypes).mockResolvedValue(
    res([{ id: 'at1', name: 'Cattle', breeds: [] }]),
  );
  vi.mocked(configurationApi.breeds).mockResolvedValue(res([]));
  vi.mocked(configurationApi.sexOptions).mockResolvedValue(res([]));
  vi.mocked(configurationApi.ageCategories).mockResolvedValue(res([]));
  vi.mocked(configurationApi.statuses).mockResolvedValue(res([]));
  vi.mocked(configurationApi.locationTypes).mockResolvedValue(res([{ id: 'lt1', name: 'Shed' }]));
  vi.mocked(configurationApi.locations).mockResolvedValue(res([]));
};

beforeEach(() => {
  vi.resetAllMocks();
  seedLists();
});

const renderPage = () =>
  render(
    <MemoryRouter>
      <ConfigurationPage />
    </MemoryRouter>,
  );

/** Only the visible tab's table is addressable — antd keeps previously opened panes mounted. */
const activePane = () => document.querySelector('.ant-tabs-content-active') as HTMLElement;

const openTab = async (user: User, name: string) => {
  await user.click(await screen.findByRole('tab', { name }));
};

const confirmDelete = async (user: User) => {
  await user.click(within(activePane()).getByRole('button', { name: /delete/i }));
  await user.click(await screen.findByRole('button', { name: /^ok$/i }));
};

describe('ConfigurationPage delete feedback', () => {
  it('keeps the row and shows the server reason when a delete is refused', async () => {
    vi.mocked(configurationApi.deleteLocationType).mockRejectedValue({
      response: {
        data:
          "Cannot delete location type 'Shed': still used by 1 location. " +
          'Change those locations to another type first.',
      },
    });

    const user = userEvent.setup();
    renderPage();
    await openTab(user, 'Location Types');
    expect(within(activePane()).getByText('Shed')).toBeInTheDocument();

    await confirmDelete(user);

    expect(await screen.findByText(/still used by 1 location/i)).toBeInTheDocument();
    // A 409 must not remove the row: the delete really did not happen.
    expect(within(activePane()).getByText('Shed')).toBeInTheDocument();
  }, 20000);

  it('removes the row once the delete succeeds', async () => {
    vi.mocked(configurationApi.deleteLocationType).mockResolvedValue(res(undefined));
    // The refresh that follows the delete returns the now-empty list.
    vi.mocked(configurationApi.locationTypes)
      .mockResolvedValueOnce(res([{ id: 'lt1', name: 'Shed' }]))
      .mockResolvedValue(res([]));

    const user = userEvent.setup();
    renderPage();
    await openTab(user, 'Location Types');
    expect(within(activePane()).getByText('Shed')).toBeInTheDocument();

    await confirmDelete(user);

    expect(await screen.findByText('Location type deleted')).toBeInTheDocument();
    await waitFor(() => expect(within(activePane()).queryByText('Shed')).not.toBeInTheDocument());
  }, 20000);

  it('keeps the lists that loaded when one lookup fails', async () => {
    vi.mocked(configurationApi.locations).mockRejectedValue({
      response: { data: 'Location service unavailable' },
    });

    renderPage();

    // The other six lookups still populate their tables instead of the page going blank.
    expect(await screen.findByText('Cattle')).toBeInTheDocument();
    expect(await screen.findByText('Location service unavailable')).toBeInTheDocument();
  }, 20000);
});
