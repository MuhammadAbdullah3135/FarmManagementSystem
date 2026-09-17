import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, within, fireEvent } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import AnimalsPage from './AnimalsPage';
import { animalsApi } from '../../api/animals';
import { lookupsApi } from '../../api/attendance';
import { configurationApi } from '../../api/configuration';

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
    locationTypes: vi.fn(),
    ageCategories: vi.fn(),
    animals: vi.fn(),
    breeds: vi.fn(),
  },
}));

vi.mock('../../api/configuration', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../api/configuration')>();
  return {
    ...actual,
    configurationApi: {
      ...actual.configurationApi,
      createAnimalType: vi.fn(),
      createBreed: vi.fn(),
      createSexOption: vi.fn(),
      createAgeCategory: vi.fn(),
      createStatus: vi.fn(),
      createLocation: vi.fn(),
    },
  };
});

const res = (data: unknown) => ({ data }) as never;
type User = ReturnType<typeof userEvent.setup>;

beforeEach(() => {
  vi.mocked(animalsApi.list).mockResolvedValue(res({ items: [], totalCount: 0 }));
  vi.mocked(lookupsApi.animalTypes).mockResolvedValue(res([{ id: 't1', name: 'Cattle' }]));
  vi.mocked(lookupsApi.sexOptions).mockResolvedValue(res([{ id: 's1', value: 'Female' }]));
  vi.mocked(lookupsApi.statuses).mockResolvedValue(res([{ id: 'st1', name: 'Healthy' }]));
  vi.mocked(lookupsApi.locations).mockResolvedValue(res([{ id: 'l1', name: 'Barn' }]));
  vi.mocked(lookupsApi.locationTypes).mockResolvedValue(res([{ id: 'lt1', name: 'Shed' }]));
  vi.mocked(lookupsApi.ageCategories).mockResolvedValue(res([
    { id: 'ac-1', name: 'Calf' },
    { id: 'ac-2', name: 'Adult' },
  ]));
  vi.mocked(lookupsApi.animals).mockResolvedValue(res({ items: [], totalCount: 0 }));
  vi.mocked(lookupsApi.breeds).mockResolvedValue(res([]));
  vi.mocked(configurationApi.createAnimalType).mockReset();
  vi.mocked(configurationApi.createBreed).mockReset();
  vi.mocked(configurationApi.createSexOption).mockReset();
  vi.mocked(configurationApi.createAgeCategory).mockReset();
  vi.mocked(configurationApi.createStatus).mockReset();
  vi.mocked(configurationApi.createLocation).mockReset();
});

const renderPage = () =>
  render(
    <MemoryRouter>
      <AnimalsPage />
    </MemoryRouter>,
  );

const openAddAnimalModal = async (user: User) => {
  await user.click(await screen.findByRole('button', { name: /add animal/i }));
  return (await screen.findByText('Add Animal', { selector: '.ant-modal-title' })).closest('.ant-modal') as HTMLElement;
};

const getFormItem = async (modal: HTMLElement, labelText: string) => {
  const labelNode = await within(modal).findByText(labelText, { selector: 'label' });
  return labelNode.closest('.ant-form-item') as HTMLElement;
};

const openSelectFooter = async (user: User, modal: HTMLElement, labelText: string) => {
  await user.click(within(await getFormItem(modal, labelText)).getByRole('combobox'));
};

/** Finds an option's content element across all mounted select dropdowns.
 *  In antd v6 the role=option elements are a hidden a11y mirror without click handlers,
 *  so options must be addressed by their content class. */
const findOptionContent = (label: RegExp | string) => {
  for (const dropdown of document.querySelectorAll('.ant-select-dropdown')) {
    const match = within(dropdown as HTMLElement).queryAllByText(label, { selector: '.ant-select-item-option-content' });
    if (match.length > 0) return match[0];
  }
  return null;
};

/** Selects an option. Uses fireEvent because jsdom leaves the virtual list
 *  with pointer-events: none, which userEvent treats as an error. */
const selectOption = (label: RegExp | string) => {
  const content = findOptionContent(label);
  expect(content).not.toBeNull();
  fireEvent.click(content!.closest('.ant-select-item-option') as HTMLElement);
};

describe('AnimalsPage create form', () => {
  it('loads age category options into the Age Category selector', async () => {
    const user = userEvent.setup();
    renderPage();

    // Open the create-animal modal.
    const modal = await openAddAnimalModal(user);

    // Open the Age Category select inside its form item.
    await openSelectFooter(user, modal, 'Age Category');

    // The lookup options must render in the dropdown.
    expect(await screen.findByRole('option', { name: 'Calf' })).toBeInTheDocument();
    expect(screen.getByRole('option', { name: 'Adult' })).toBeInTheDocument();
  });

  it('offers an inline Add footer for every lookup dropdown', async () => {
    const user = userEvent.setup();
    renderPage();
    const modal = await openAddAnimalModal(user);

    // Pick a type first — Breed options (and its Add footer) depend on it.
    await openSelectFooter(user, modal, 'Animal Type');
    expect(await screen.findByRole('button', { name: /add animal type/i })).toBeInTheDocument();
    selectOption('Cattle');

    // Opening the next select closes the previous dropdown, so no Escape needed
    // (Escape leaves antd's closed select input with pointer-events: none).
    await openSelectFooter(user, modal, 'Breed');
    expect(await screen.findByRole('button', { name: /add breed/i })).toBeInTheDocument();

    await openSelectFooter(user, modal, 'Sex');
    expect(await screen.findByRole('button', { name: /add sex/i })).toBeInTheDocument();

    await openSelectFooter(user, modal, 'Status');
    expect(await screen.findByRole('button', { name: /add status/i })).toBeInTheDocument();

    await openSelectFooter(user, modal, 'Location');
    expect(await screen.findByRole('button', { name: /add location/i })).toBeInTheDocument();

    await openSelectFooter(user, modal, 'Age Category');
    expect(await screen.findByRole('button', { name: /add age category/i })).toBeInTheDocument();
  });

  it('shows a hint instead of Add for Breed until an animal type is chosen', async () => {
    const user = userEvent.setup();
    renderPage();
    const modal = await openAddAnimalModal(user);

    // The Breed select stays openable before a type is chosen; only the
    // inline quick-add is gated, with a hint in the footer.
    await openSelectFooter(user, modal, 'Breed');
    expect(await screen.findByText(/select an animal type first, then add breeds for it/i)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /add breed/i })).not.toBeInTheDocument();
  });

  it('creates a sex option via quick-add, selects it and appends the option', async () => {
    vi.mocked(configurationApi.createSexOption).mockResolvedValue(res({ id: 'sex-new', value: 'Unknown' }));
    const user = userEvent.setup();
    renderPage();
    const modal = await openAddAnimalModal(user);

    await openSelectFooter(user, modal, 'Sex');
    await user.click(await screen.findByRole('button', { name: /add sex/i }));

    const addSexModal = (await screen.findByText('Add Sex', { selector: '.ant-modal-title' })).closest('.ant-modal') as HTMLElement;
    await user.type(within(addSexModal).getByLabelText('Value'), 'Unknown');
    await user.click(within(addSexModal).getByRole('button', { name: /add sex/i }));

    expect(configurationApi.createSexOption).toHaveBeenCalledWith({ value: 'Unknown' });
    expect(await screen.findByText('Sex "Unknown" created')).toBeInTheDocument();

    // The created option is selected in the Sex select and appended to the list.
    const sexForm = await getFormItem(modal, 'Sex');
    expect(within(sexForm).getByText('Unknown')).toBeInTheDocument();
    await openSelectFooter(user, modal, 'Sex');
    expect(findOptionContent('Female')).not.toBeNull();
    expect(findOptionContent('Unknown')).not.toBeNull();
  });

  it('keeps the quick-add modal open and shows the error when creation fails', async () => {
    vi.mocked(configurationApi.createSexOption).mockRejectedValue({
      response: { data: 'Sex option already exists' },
    });
    const user = userEvent.setup();
    renderPage();
    const modal = await openAddAnimalModal(user);

    await openSelectFooter(user, modal, 'Sex');
    await user.click(await screen.findByRole('button', { name: /add sex/i }));

    const addSexModal = (await screen.findByText('Add Sex', { selector: '.ant-modal-title' })).closest('.ant-modal') as HTMLElement;
    await user.type(within(addSexModal).getByLabelText('Value'), 'Unknown');
    await user.click(within(addSexModal).getByRole('button', { name: /add sex/i }));

    // Server error surfaced, modal stays open, input preserved.
    expect(await screen.findByText('Sex option already exists')).toBeInTheDocument();
    expect(within(addSexModal).getByLabelText('Value')).toHaveValue('Unknown');
    expect(configurationApi.createSexOption).toHaveBeenCalledTimes(1);
  });

  it('creates a breed via quick-add for the selected animal type', async () => {
    vi.mocked(configurationApi.createBreed).mockResolvedValue(res({
      id: 'br-new',
      name: 'Test Breed',
      animalTypeId: 't1',
      averageGestationDays: 283,
    }));
    const user = userEvent.setup();
    renderPage();
    const modal = await openAddAnimalModal(user);

    await openSelectFooter(user, modal, 'Animal Type');
    selectOption('Cattle');

    await openSelectFooter(user, modal, 'Breed');
    await user.click(await screen.findByRole('button', { name: /add breed/i }));

    const addBreedModal = (await screen.findByText('Add Breed', { selector: '.ant-modal-title' })).closest('.ant-modal') as HTMLElement;
    await user.type(within(addBreedModal).getByPlaceholderText('e.g. Sahiwal'), 'Test Breed');
    await user.click(within(addBreedModal).getByRole('button', { name: /add breed/i }));

    expect(configurationApi.createBreed).toHaveBeenCalledWith({
      name: 'Test Breed',
      animalTypeId: 't1',
      averageGestationDays: 283,
    });
    expect(await screen.findByText('Breed "Test Breed" created')).toBeInTheDocument();
    const breedForm = await getFormItem(modal, 'Breed');
    expect(within(breedForm).getByText('Test Breed')).toBeInTheDocument();
  });

  it('renders nested child locations in the Location select', async () => {
    vi.mocked(lookupsApi.locations).mockResolvedValue(res([
      {
        id: 'l1',
        name: 'Main Barn',
        childLocations: [{ id: 'l2', name: 'Shed A', childLocations: [] }],
      },
    ]));
    const user = userEvent.setup();
    renderPage();
    const modal = await openAddAnimalModal(user);

    await openSelectFooter(user, modal, 'Location');

    expect(findOptionContent(/main barn/i)).not.toBeNull();
    expect(findOptionContent(/shed a/i)).not.toBeNull();
  });
});
