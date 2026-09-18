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
      createLocationType: vi.fn(),
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
  vi.mocked(configurationApi.createLocationType).mockReset();
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

/** Finds an option's content element across all mounted select dropdowns,
 *  searching most-recently-opened first. In antd v6 the role=option elements are a
 *  hidden a11y mirror without click handlers, so options must be addressed by
 *  their content class. */
const findOptionContent = (label: RegExp | string) => {
  for (const dropdown of [...document.querySelectorAll('.ant-select-dropdown')].reverse()) {
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

/** Clicks a footer button inside the most recently opened dropdown that shows
 *  the given option (nested quick-add footers live in the dropdown portal). */
const clickFooterInDropdownWith = async (user: User, optionLabel: RegExp | string, buttonName: RegExp | string) => {
  for (const dropdown of [...document.querySelectorAll('.ant-select-dropdown')].reverse()) {
    const hasOption = within(dropdown as HTMLElement).queryAllByText(optionLabel, { selector: '.ant-select-item-option-content' });
    if (hasOption.length > 0) {
      await user.click(within(dropdown as HTMLElement).getByRole('button', { name: buttonName }));
      return;
    }
  }
  throw new Error(`No open dropdown contains option ${String(optionLabel)}`);
};

const isHiddenByInlineStyle = (el: HTMLElement) => {
  let node: HTMLElement | null = el;
  while (node) {
    if (node.style.display === 'none') return true;
    node = node.parentElement;
  }
  return false;
};

/** Nested quick-add modals pre-render hidden with identical titles, so locate
 *  the open one by unique content instead of by title. */
const findVisibleModal = (predicate: (modal: HTMLElement) => boolean) => {
  const visible = Array.from(document.querySelectorAll<HTMLElement>('.ant-modal'))
    .filter((m) => !isHiddenByInlineStyle(m))
    .find(predicate);
  expect(visible).toBeDefined();
  return visible as HTMLElement;
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
  }, 20000);

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
    // Drives several antd overlays through userEvent, which takes ~3s on an idle machine and
    // exceeded vitest's 5s default under parallel load (it blocked the Pages deploy job).
  }, 20000);

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
    // Same 5s-default flake as the tests above: the modal walk plus the rejected request
    // exceeded the default under parallel load, which fails the Pages deploy job.
  }, 20000);

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
    // Heaviest quick-add walk in the file (two nested select footers); 5s was not enough.
  }, 20000);

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

  it('lists location types in the Add Location modal and supports adding one inline', async () => {
    vi.mocked(lookupsApi.locationTypes).mockResolvedValue(res([
      { id: 'lt1', name: 'Shed' },
      { id: 'lt2', name: 'Barn' },
      { id: 'lt3', name: 'Paddock' },
    ]));
    vi.mocked(configurationApi.createLocationType).mockResolvedValue(res({ id: 'lt-new', name: 'Aviary' }));
    const user = userEvent.setup();
    renderPage();
    const modal = await openAddAnimalModal(user);

    await openSelectFooter(user, modal, 'Location');
    await user.click(await screen.findByRole('button', { name: /add location/i }));

    const addLocationModal = findVisibleModal((m) => !!m.querySelector('input[placeholder="e.g. Shed A"]'));

    // The Location Type field is pre-filled with the first type and lists all of them.
    const typeItem = ((await within(addLocationModal).findByText('Location Type', { selector: 'label' })).closest('.ant-form-item')) as HTMLElement;
    expect(within(typeItem).getByText('Shed')).toBeInTheDocument();
    await user.click(within(typeItem).getByRole('combobox'));
    expect(findOptionContent('Barn')).not.toBeNull();
    expect(findOptionContent('Paddock')).not.toBeNull();

    // A missing location type can be created right from inside the modal.
    await user.click(await screen.findByRole('button', { name: /add location type/i }));
    const addTypeModal = findVisibleModal((m) => !!m.querySelector('input[placeholder="e.g. Shed"]'));
    await user.type(within(addTypeModal).getByPlaceholderText('e.g. Shed'), 'Aviary');
    await user.click(within(addTypeModal).getByRole('button', { name: /add location type/i }));

    expect(configurationApi.createLocationType).toHaveBeenCalledWith({ name: 'Aviary' });
    expect(await screen.findByText('Location Type "Aviary" created')).toBeInTheDocument();
    expect(within(typeItem).getByText('Aviary')).toBeInTheDocument();
  }, 20000);

  it('lists locations in Parent Location and supports adding one inline', async () => {
    const locationsData = [{ id: 'l1', name: 'Main Farm', childLocations: [] as unknown[] }];
    vi.mocked(lookupsApi.locations).mockResolvedValue(res(locationsData));
    vi.mocked(configurationApi.createLocation).mockImplementation(async () => {
      // The refetch inside the handler must return the created location.
      locationsData[0].childLocations.push({ id: 'l-new', name: 'Shed B', childLocations: [] });
      return res({ id: 'l-new', name: 'Shed B', locationTypeId: 'lt1' });
    });
    const user = userEvent.setup();
    renderPage();
    const modal = await openAddAnimalModal(user);

    await openSelectFooter(user, modal, 'Location');
    await user.click(await screen.findByRole('button', { name: /add location/i }));

    const addLocationModal = findVisibleModal((m) => !!m.querySelector('input[placeholder="e.g. Shed A"]'));

    // Parent Location lists existing locations.
    const parentItem = ((await within(addLocationModal).findByText('Parent Location (optional)', { selector: 'label' })).closest('.ant-form-item')) as HTMLElement;
    await user.click(within(parentItem).getByRole('combobox'));
    expect(findOptionContent('Main Farm')).not.toBeNull();

    // A missing parent location can be created inline (with its own inline location type).
    await clickFooterInDropdownWith(user, 'Main Farm', /add location/i);
    const nestedModal = findVisibleModal((m) => !!m.querySelector('input[placeholder="e.g. Shed B"]'));
    await user.type(within(nestedModal).getByPlaceholderText('e.g. Shed B'), 'Shed B');
    await user.click(within(nestedModal).getByRole('button', { name: 'Add Location' }));

    expect(configurationApi.createLocation).toHaveBeenCalledWith({ name: 'Shed B', locationTypeId: 'lt1' });
    expect(await screen.findByText('Location "Shed B" created')).toBeInTheDocument();
    expect(within(parentItem).getByText(/shed b/i)).toBeInTheDocument();
  }, 20000);
});
