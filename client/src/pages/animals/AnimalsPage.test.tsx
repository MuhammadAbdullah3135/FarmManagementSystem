import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, within, fireEvent, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, useLocation } from 'react-router-dom';
import AnimalsPage from './AnimalsPage';
import { animalsApi } from '../../api/animals';
import { lookupsApi } from '../../api/attendance';
import { configurationApi } from '../../api/configuration';
import { useAuthStore } from '../../stores/authStore';

vi.mock('../../api/animals', () => ({
        animalsApi: {
        list: vi.fn(),
        create: vi.fn(),
        update: vi.fn(),
        delete: vi.fn(),
        bulkChangeStatus: vi.fn(),
        bulkTransfer: vi.fn(),
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
  // Every test starts as a real signed-in user; the demo cases set the flag themselves.
  useAuthStore.setState({ user: null, isAuthenticated: false });
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

/** Shows the current query string, so a test can assert what the URL carries. */
const LocationProbe = () => {
  const location = useLocation();
  return <div data-testid="location-search">{location.search}</div>;
};

/** The comboboxes inside a region, by the role they carry rather than by class. */
const comboboxesIn = (root: ParentNode) =>
  Array.from(root.querySelectorAll<HTMLElement>('[role="combobox"]'));

const renderPage = (entry = '/dashboard/animals') =>
  render(
    <MemoryRouter initialEntries={[entry]}>
      <AnimalsPage />
      <LocationProbe />
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

/*
 * The parent pickers are half of the parentage rule the API enforces on save (a sire is male,
 * a dam is female, and an animal is never its own parent). They are asserted through the real
 * modal because the two halves have to agree: a form that offers a pairing the server refuses
 * only moves the failure to the end of a form the user has already filled in.
 */
const animalRow = (overrides: Record<string, unknown> = {}) => ({
  id: 'row-1',
  tagNumber: 'TAG-0079',
  name: 'Boocho',
  animalTypeId: 't1',
  animalTypeName: 'Cattle',
  sexOptionId: 's1',
  sexValue: 'Female',
  animalStatusId: 'st1',
  statusName: 'Healthy',
  statusCategory: 0,
  createdAt: '2026-01-01T00:00:00Z',
  ...overrides,
});

/** The options in the dropdown that is currently open — the last one antd has shown. */
const openDropdownOptions = () => {
  const open = [...document.querySelectorAll<HTMLElement>('.ant-select-dropdown')].filter(
    (dropdown) => !dropdown.className.includes('hidden'),
  );
  const last = open[open.length - 1];
  return [...last.querySelectorAll('.ant-select-item-option-content')].map((el) => el.textContent);
};

/** Opens the edit modal for the only row on the page (its second action button). */
const openEditAnimalModal = async (user: User) => {
  const row = await screen.findByText('TAG-0079');
  const tableRow = row.closest('tr') as HTMLElement;
  await user.click(within(tableRow).getAllByRole('button')[1]);
  return (await screen.findByText('Edit Animal', { selector: '.ant-modal-title' })).closest('.ant-modal') as HTMLElement;
};

describe('AnimalsPage parent pickers', () => {
  const herd = [
    { id: 'cow-1', tagNumber: 'COW-1', name: 'Bessie', sexValue: 'Female' },
    { id: 'bull-1', tagNumber: 'BULL-1', name: 'Rex', sexValue: 'Male' },
  ];

  it('offers only male animals as a sire and only females as a dam', async () => {
    const user = userEvent.setup();
    vi.mocked(lookupsApi.animals).mockResolvedValue(res({ items: herd, totalCount: 2 }));
    renderPage();

    const modal = await openAddAnimalModal(user);
    await openSelectFooter(user, modal, 'Sire (Father)');
    expect(openDropdownOptions()).toEqual(['BULL-1 - Rex']);

    // Opening the next select closes the previous dropdown, so both halves are the open one.
    await openSelectFooter(user, modal, 'Dam (Mother)');
    expect(openDropdownOptions()).toEqual(['COW-1 - Bessie']);
  }, 20000);

  it('never offers the animal being edited as its own parent', async () => {
    const user = userEvent.setup();
    vi.mocked(animalsApi.list).mockResolvedValue(
      res({ items: [animalRow({ id: 'bull-1', tagNumber: 'TAG-0079', sexValue: 'Male' })], totalCount: 1 }),
    );
    vi.mocked(lookupsApi.animals).mockResolvedValue(
      res({ items: [...herd, { id: 'bull-2', tagNumber: 'BULL-2', name: 'Star', sexValue: 'Male' }], totalCount: 3 }),
    );
    renderPage();

    const modal = await openEditAnimalModal(user);
    await openSelectFooter(user, modal, 'Sire (Father)');

    // BULL-1 is male, so the sex rule would have kept it on this list: only the rule against
    // being its own parent takes it off.
    const options = openDropdownOptions();
    expect(options).toEqual(['BULL-2 - Star']);
    expect(options.some((label) => label?.startsWith('BULL-1'))).toBe(false);
  }, 20000);

  it('shows the parent already on an impossible record, and keeps it selectable', async () => {
    const user = userEvent.setup();
    // The farm's real record: TAG-0058-2, a female, stored as the sire of 079 Boocho.
    vi.mocked(animalsApi.list).mockResolvedValue(
      res({
        items: [animalRow({ id: 'cow-1', sireId: 'cow-2', sireTagNumber: 'TAG-0058-2' })],
        totalCount: 1,
      }),
    );
    vi.mocked(lookupsApi.animals).mockResolvedValue(
      res({
        items: [...herd, { id: 'cow-2', tagNumber: 'TAG-0058-2', name: 'Seed Calf B', sexValue: 'Female' }],
        totalCount: 3,
      }),
    );
    renderPage();

    const modal = await openEditAnimalModal(user);
    const sireItem = await getFormItem(modal, 'Sire (Father)');

    // Shown by tag, not as the raw id the field would fall back to if the stored parent were
    // filtered out of the options.
    expect(sireItem.textContent).toContain('TAG-0058-2 - Seed Calf B');

    await openSelectFooter(user, modal, 'Sire (Father)');
    expect(openDropdownOptions()).toEqual(['TAG-0058-2 - Seed Calf B', 'BULL-1 - Rex']);
  }, 20000);

  it('leaves the list whole when the farm names its sexes something this app cannot read', async () => {
    const user = userEvent.setup();
    vi.mocked(lookupsApi.animals).mockResolvedValue(
      res({
        items: [
          { id: 'buck-1', tagNumber: 'BUCK-1', name: 'Rex', sexValue: 'Buck' },
          { id: 'doe-1', tagNumber: 'DOE-1', name: 'Bessie', sexValue: 'Doe' },
        ],
        totalCount: 2,
      }),
    );
    renderPage();

    const modal = await openAddAnimalModal(user);
    await openSelectFooter(user, modal, 'Sire (Father)');

    // Narrowing on a word the app does not know would empty the field; the API leaves such a
    // value alone too, so both layers keep working on that farm.
    expect(openDropdownOptions()).toEqual(['BUCK-1 - Rex', 'DOE-1 - Bessie']);
  }, 20000);

  it('offers nothing rather than the wrong sex when no animal of that sex is recorded', async () => {
    const user = userEvent.setup();
    vi.mocked(lookupsApi.animals).mockResolvedValue(
      res({
        items: [
          { id: 'cow-1', tagNumber: 'COW-1', name: 'Bessie', sexValue: 'Female' },
          { id: 'cow-2', tagNumber: 'COW-2', name: 'Daisy', sexValue: 'Female' },
        ],
        totalCount: 2,
      }),
    );
    renderPage();

    const modal = await openAddAnimalModal(user);
    await openSelectFooter(user, modal, 'Sire (Father)');

    // A herd of cows: the sire field offers none of them, because the server would refuse one,
    // and says so rather than opening onto an unexplained empty list.
    expect(openDropdownOptions()).toEqual([]);
    expect(await screen.findByText('No animal of this sex is recorded yet.')).toBeInTheDocument();

    await openSelectFooter(user, modal, 'Dam (Mother)');
    expect(openDropdownOptions()).toEqual(['COW-1 - Bessie', 'COW-2 - Daisy']);
  }, 20000);
});

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
    // Same 5s-default flake as the quick-add walks below: mounting this page's
    // seven always-mounted quick-add modals under full-suite parallel load
    // exceeded the default and failed the Pages deploy job.
  }, 20000);

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
  }, 60000);

  it('shows a hint instead of Add for Breed until an animal type is chosen', async () => {
    const user = userEvent.setup();
    renderPage();
    const modal = await openAddAnimalModal(user);

    // The Breed select stays openable before a type is chosen; only the
    // inline quick-add is gated, with a hint in the footer.
    await openSelectFooter(user, modal, 'Breed');
    expect(await screen.findByText(/select an animal type first, then add breeds for it/i)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /add breed/i })).not.toBeInTheDocument();
  }, 60000);

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

/*
 * The list controls and the two bulk writes.
 *
 * The filters are asserted through the request rather than the table: the page owns no filtering
 * of its own, so what matters is that the server is asked the question the user chose — and that
 * a filter change returns to page one instead of leaving the table on a page that no longer
 * exists. The bulk tests assert the single call the endpoints are for, because the point of the
 * feature is that 40 animals are one request rather than 40.
 */
describe('AnimalsPage list filters', () => {
  /*
   * A filter select carries its visible hint in `placeholder`, which is not an accessible name:
   * a screen reader announces the control with no label, and once a value is chosen even the hint
   * is gone. The filters sit outside any `Form.Item`, so nothing else names them.
   *
   * The count is asserted so that a filter added later cannot slip in unnamed.
   */
  it('names every filter select for a screen reader', async () => {
    renderPage();

    const row = document.querySelector('.fms-filter-row') as HTMLElement;
    expect(row).not.toBeNull();
    await waitFor(() => expect(comboboxesIn(row)).toHaveLength(4));

    expect(comboboxesIn(row).map((combobox) => combobox.getAttribute('aria-label'))).toEqual([
      'Filter by type',
      'Filter by sex',
      'Filter by status',
      'Filter by location',
    ]);
    comboboxesIn(row).forEach((combobox) => expect(combobox).toHaveAccessibleName());
  }, 20000);

  it('sends the search term to the API and returns to the first page', async () => {
    const user = userEvent.setup();
    renderPage();

    const search = await screen.findByPlaceholderText('Search by tag or name');
    await user.type(search, 'TAG-0079{enter}');

    await waitFor(() =>
      expect(animalsApi.list).toHaveBeenLastCalledWith({ page: 1, pageSize: 10, search: 'TAG-0079' }),
    );
  });

  it('filters by status and clears back to the unfiltered list', async () => {
    const user = userEvent.setup();
    renderPage();

    await user.click(await screen.findByLabelText('Filter by status'));
    selectOption('Healthy');
    await waitFor(() =>
      expect(animalsApi.list).toHaveBeenLastCalledWith({ page: 1, pageSize: 10, statusId: 'st1' }),
    );

    // Every filter is one request: clearing them all is a single unfiltered read, not five.
    await user.click(screen.getByRole('button', { name: 'Clear filters' }));
    await waitFor(() =>
      expect(animalsApi.list).toHaveBeenLastCalledWith({ page: 1, pageSize: 10 }),
    );
  });

  it('asks for a larger page when the page-size control changes', async () => {
    const user = userEvent.setup();
    vi.mocked(animalsApi.list).mockResolvedValue(res({ items: [animalRow()], totalCount: 120 }));
    renderPage();

    await screen.findByText('TAG-0079');
    const sizeSelect = document.querySelector('.ant-pagination-options .ant-select') as HTMLElement;
    await user.click(sizeSelect);
    selectOption('50 / page');

    await waitFor(() =>
      expect(animalsApi.list).toHaveBeenLastCalledWith({ page: 1, pageSize: 50 }),
    );
  }, 20000);
});

/*
 * The list's view state lives in the URL: a reload has to land on the same filters, page and page
 * size, so the first test renders a URL the way a refresh (or a shared link) presents it, and the
 * second asserts that using the controls is what puts that state there in the first place.
 */
describe('AnimalsPage list state in the URL', () => {
  it('restores the filters, page and page size a reloaded URL asks for', async () => {
    renderPage('/dashboard/animals?search=TAG-0079&sexOptionId=s1&page=3&pageSize=50');

    await waitFor(() =>
      expect(animalsApi.list).toHaveBeenLastCalledWith({
        page: 3,
        pageSize: 50,
        search: 'TAG-0079',
        sexOptionId: 's1',
      }),
    );

    // The box shows the applied search rather than opening empty over a filtered list.
    expect(screen.getByPlaceholderText('Search by tag or name')).toHaveValue('TAG-0079');
  });

  it('writes a chosen filter and page size into the URL, so a reload keeps them', async () => {
    const user = userEvent.setup();
    vi.mocked(animalsApi.list).mockResolvedValue(res({ items: [animalRow()], totalCount: 120 }));
    renderPage();

    await user.click(await screen.findByLabelText('Filter by status'));
    selectOption('Healthy');
    await waitFor(() =>
      expect(screen.getByTestId('location-search').textContent).toContain('statusId=st1'),
    );

    const sizeSelect = document.querySelector('.ant-pagination-options .ant-select') as HTMLElement;
    await user.click(sizeSelect);
    selectOption('50 / page');

    await waitFor(() => {
      const url = screen.getByTestId('location-search').textContent ?? '';
      expect(url).toContain('statusId=st1');
      expect(url).toContain('pageSize=50');
    });
  }, 20000);
});

/*
 * The row actions are icons only, so their aria-label is the whole name. It has to carry the
 * animal: on a full page the column repeats the same three actions once per row, and a name of
 * just "Edit" leaves a screen reader with two dozen buttons it cannot tell apart. Asserting the
 * tag is in the name — rather than that an aria-label merely exists — is what fails if someone
 * later fills the label with a constant string.
 */
describe('AnimalsPage row actions', () => {
  it('names each row action after the animal on that row', async () => {
    vi.mocked(animalsApi.list).mockResolvedValue(
      res({
        items: [animalRow(), animalRow({ id: 'row-2', tagNumber: 'TAG-0080' })],
        totalCount: 2,
      }),
    );
    renderPage();

    const actionNames = async (tag: string) => {
      const row = (await screen.findByText(tag)).closest('tr') as HTMLElement;
      return within(row).getAllByRole('button').map((button) => button.getAttribute('aria-label') ?? '');
    };

    const first = await actionNames('TAG-0079');
    const second = await actionNames('TAG-0080');

    // View, edit and delete.
    expect(first).toHaveLength(3);
    expect(second).toHaveLength(3);
    first.forEach((name) => expect(name).toContain('TAG-0079'));
    second.forEach((name) => expect(name).toContain('TAG-0080'));
    // No name leaks the other row's animal, so the two rows' actions are genuinely distinct.
    first.forEach((name) => expect(name).not.toContain('TAG-0080'));
    expect(first).not.toEqual(second);
  }, 20000);
});

describe('AnimalsPage bulk actions', () => {
  const twoAnimals = () =>
    vi.mocked(animalsApi.list).mockResolvedValue(
      res({
        items: [animalRow(), animalRow({ id: 'row-2', tagNumber: 'TAG-0080' })],
        totalCount: 2,
      }),
    );

  /** Ticks both rows and waits for the selection bar. */
  const selectBothRows = async (user: User) => {
    const boxes = screen.getAllByRole('checkbox');
    // [0] is the header's select-all; the two rows follow it.
    await user.click(boxes[1]);
    await user.click(boxes[2]);
    expect(await screen.findByText('2 selected')).toBeInTheDocument();
  };

  it('changes the status of every selected animal in one request', async () => {
    const user = userEvent.setup();
    twoAnimals();
    vi.mocked(animalsApi.bulkChangeStatus).mockResolvedValue(
      res({ requestedCount: 2, successCount: 2, failures: [] }),
    );
    renderPage();

    await screen.findByText('TAG-0079');
    await selectBothRows(user);

    await user.click(screen.getByRole('button', { name: /change status/i }));
    const modal = (await screen.findByText('Change status (2 selected)', { selector: '.ant-modal-title' }))
      .closest('.ant-modal') as HTMLElement;
    await user.click(within(modal).getByRole('combobox'));
    selectOption('Healthy');
    await user.click(within(modal).getByRole('button', { name: /^apply$/i }));

    await waitFor(() =>
      expect(animalsApi.bulkChangeStatus).toHaveBeenCalledWith({
        animalIds: ['row-1', 'row-2'],
        newStatusId: 'st1',
        reason: undefined,
      }),
    );
  }, 20000);

  it('transfers every selected animal to the chosen location in one request', async () => {
    const user = userEvent.setup();
    twoAnimals();
    vi.mocked(animalsApi.bulkTransfer).mockResolvedValue(
      res({ requestedCount: 2, successCount: 2, failures: [] }),
    );
    renderPage();

    await screen.findByText('TAG-0079');
    await selectBothRows(user);

    await user.click(screen.getByRole('button', { name: /transfer to location/i }));
    const modal = (await screen.findByText('Transfer (2 selected)', { selector: '.ant-modal-title' }))
      .closest('.ant-modal') as HTMLElement;
    await user.click(within(modal).getByRole('combobox'));
    selectOption('Barn');
    await user.click(within(modal).getByRole('button', { name: /^apply$/i }));

    await waitFor(() =>
      expect(animalsApi.bulkTransfer).toHaveBeenCalledWith({
        animalIds: ['row-1', 'row-2'],
        toLocationId: 'l1',
        reason: undefined,
      }),
    );
  }, 20000);

  it('disables the bulk actions and says why for the demo account', async () => {
    const user = userEvent.setup();
    twoAnimals();
    useAuthStore.setState({
      isAuthenticated: true,
      user: {
        userId: 'demo-1',
        accountId: 'acct-1',
        email: 'demo@farm.local',
        firstName: 'Demo',
        lastName: 'User',
        roles: ['SystemOwner'],
        isDemo: true,
      },
    });
    renderPage();

    await screen.findByText('TAG-0079');
    await selectBothRows(user);

    // The server refuses these writes for the demo account, so the buttons are disabled with the
    // reason attached rather than left to fail after the visitor has chosen a status.
    const changeStatus = screen.getByRole('button', { name: /change status/i });
    expect(changeStatus).toBeDisabled();
    expect(screen.getByRole('button', { name: /transfer to location/i })).toBeDisabled();

    await user.hover(changeStatus);
    expect(
      await screen.findByText('Bulk changes are disabled for the demo account.'),
    ).toBeInTheDocument();
  }, 20000);

  it('reports a partial result and keeps the reason the user typed', async () => {
    const user = userEvent.setup();
    twoAnimals();
    vi.mocked(animalsApi.bulkChangeStatus).mockResolvedValue(
      res({ requestedCount: 2, successCount: 1, failures: [{ animalId: 'row-2', reason: 'Sold' }] }),
    );
    renderPage();

    await screen.findByText('TAG-0079');
    await selectBothRows(user);

    await user.click(screen.getByRole('button', { name: /change status/i }));
    const modal = (await screen.findByText('Change status (2 selected)', { selector: '.ant-modal-title' }))
      .closest('.ant-modal') as HTMLElement;
    await user.click(within(modal).getByRole('combobox'));
    selectOption('Healthy');
    await user.type(within(modal).getByRole('textbox'), 'Herd sale');
    await user.click(within(modal).getByRole('button', { name: /^apply$/i }));

    // A refusal is per animal, so the answer is "1 of 2" plus the count the server refused —
    // never a silent success and never a lost reason.
    expect(await screen.findByText('1 of 2 animals updated')).toBeInTheDocument();
    expect(await screen.findByText('1 were refused by the server')).toBeInTheDocument();
    expect(animalsApi.bulkChangeStatus).toHaveBeenCalledWith({
      animalIds: ['row-1', 'row-2'],
      newStatusId: 'st1',
      reason: 'Herd sale',
    });
  }, 20000);
});
