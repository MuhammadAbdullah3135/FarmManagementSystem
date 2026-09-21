import { describe, it, expect, vi, beforeEach } from 'vitest';
import { getQuickAddSpec, type LookupKind, type QuickAddContext } from './lookupQuickAdd';
import { configurationApi } from '../api/configuration';
import { expenseCategoriesApi, incomeCategoriesApi, paymentMethodsApi } from '../api/finance';
import { feedTypesApi } from '../api/feed';
import { medicinesApi, vaccineTypesApi } from '../api/health';
import { departmentsApi, employeeRolesApi } from '../api/hr';
import { suppliersApi } from '../api/suppliers';
import { customersApi } from '../api/customers';

vi.mock('../api/configuration', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../api/configuration')>();
  return {
    ...actual,
    configurationApi: {
      ...actual.configurationApi,
      createAnimalType: vi.fn(),
      createBreed: vi.fn(),
      createSexOption: vi.fn(),
      createAgeCategory: vi.fn(),
      createStatus: vi.fn(),
      createLocationType: vi.fn(),
      createLocation: vi.fn(),
    },
  };
});

vi.mock('../api/finance', () => ({
  expenseCategoriesApi: { create: vi.fn() },
  incomeCategoriesApi: { create: vi.fn() },
  paymentMethodsApi: { create: vi.fn() },
}));

vi.mock('../api/feed', () => ({ feedTypesApi: { create: vi.fn() } }));

vi.mock('../api/health', () => ({
  medicinesApi: { create: vi.fn() },
  vaccineTypesApi: { create: vi.fn() },
}));

vi.mock('../api/hr', () => ({
  departmentsApi: { create: vi.fn() },
  employeeRolesApi: { create: vi.fn() },
}));

vi.mock('../api/suppliers', () => ({ suppliersApi: { create: vi.fn() } }));
vi.mock('../api/customers', () => ({ customersApi: { create: vi.fn() } }));

const res = (data: unknown) => ({ data }) as never;

/**
 * Every kind, typed as a Record so adding a kind to `LookupKind` fails to
 * compile until it is listed (and therefore tested) here.
 */
const KIND_MAP: Record<LookupKind, true> = {
  animalType: true,
  breed: true,
  sexOption: true,
  ageCategory: true,
  animalStatus: true,
  locationType: true,
  location: true,
  feedType: true,
  vaccineType: true,
  medicine: true,
  expenseCategory: true,
  incomeCategory: true,
  paymentMethod: true,
  department: true,
  employeeRole: true,
  supplier: true,
  customer: true,
};
const ALL_KINDS = Object.keys(KIND_MAP) as LookupKind[];

/** Location needs its option lists; everything else needs nothing. */
const LOCATION_CTX: QuickAddContext = {
  locationTypes: [
    { value: 'lt-1', label: 'Shed' },
    { value: 'lt-2', label: 'Barn' },
  ],
  locations: [{ value: 'loc-0', label: 'Main Farm' }],
};

beforeEach(() => {
  vi.clearAllMocks();
});

describe('lookupQuickAdd registry', () => {
  it('exposes a label and at least one field for every kind', () => {
    for (const kind of ALL_KINDS) {
      const spec = getQuickAddSpec(kind, LOCATION_CTX);
      expect(spec.label, kind).toBeTruthy();
      expect(spec.fields.length, kind).toBeGreaterThan(0);
      expect(typeof spec.create, kind).toBe('function');
    }
  });

  it('keeps the display field first in every kind, because the toast reads fields[0]', () => {
    for (const kind of ALL_KINDS) {
      const spec = getQuickAddSpec(kind, LOCATION_CTX);
      expect(spec.fields[0].name, kind).toBe(kind === 'sexOption' ? 'value' : 'name');
    }
  });

  it('wires an animal type to the configuration API', async () => {
    vi.mocked(configurationApi.createAnimalType).mockResolvedValue(res({ id: 'at-1', name: 'Cattle' }));

    const spec = getQuickAddSpec('animalType');
    await expect(spec.create({ name: 'Cattle' }, {})).resolves.toEqual({ id: 'at-1', label: 'Cattle' });
    expect(configurationApi.createAnimalType).toHaveBeenCalledWith({ name: 'Cattle' });
  });

  it('wires an age category, defaulting the day range like the configuration page', async () => {
    vi.mocked(configurationApi.createAgeCategory).mockResolvedValue(res({ id: 'ac-1', name: 'Calf' }));

    const spec = getQuickAddSpec('ageCategory');
    await expect(spec.create({ name: 'Calf' }, {})).resolves.toEqual({ id: 'ac-1', label: 'Calf' });
    expect(configurationApi.createAgeCategory).toHaveBeenCalledWith({
      name: 'Calf',
      minDays: 0,
      maxDays: 99999,
    });

    await spec.create({ name: 'Adult', minDays: 400, maxDays: 900 }, {});
    expect(configurationApi.createAgeCategory).toHaveBeenLastCalledWith({
      name: 'Adult',
      minDays: 400,
      maxDays: 900,
    });
  });

  it('wires a status with its category and active defaults', async () => {
    vi.mocked(configurationApi.createStatus).mockResolvedValue(res({ id: 'st-1', name: 'Quarantined' }));

    const spec = getQuickAddSpec('animalStatus');
    await expect(spec.create({ name: 'Quarantined' }, {})).resolves.toEqual({
      id: 'st-1',
      label: 'Quarantined',
    });
    expect(configurationApi.createStatus).toHaveBeenCalledWith({
      name: 'Quarantined',
      isActive: true,
      category: 0,
    });

    await spec.create({ name: 'Sold', isActive: false, category: 2 }, {});
    expect(configurationApi.createStatus).toHaveBeenLastCalledWith({
      name: 'Sold',
      isActive: false,
      category: 2,
    });
  });

  it('labels a sex option from its value, not its name', async () => {
    vi.mocked(configurationApi.createSexOption).mockResolvedValue(res({ id: 'sex-1', value: 'Female' }));

    const spec = getQuickAddSpec('sexOption');
    await expect(spec.create({ value: 'Female' }, {})).resolves.toEqual({ id: 'sex-1', label: 'Female' });
    expect(configurationApi.createSexOption).toHaveBeenCalledWith({ value: 'Female' });
  });

  it('wires a location type to the configuration API', async () => {
    vi.mocked(configurationApi.createLocationType).mockResolvedValue(res({ id: 'lt-1', name: 'Shed' }));

    const spec = getQuickAddSpec('locationType');
    await expect(spec.create({ name: 'Shed' }, {})).resolves.toEqual({ id: 'lt-1', label: 'Shed' });
    expect(configurationApi.createLocationType).toHaveBeenCalledWith({ name: 'Shed' });
  });

  describe('breed', () => {
    it('posts the breed with the ctx animal type and the gestation default', async () => {
      vi.mocked(configurationApi.createBreed).mockResolvedValue(
        res({ id: 'br-1', name: 'Sahiwal', animalTypeId: 'at-1', averageGestationDays: 283 }),
      );

      const spec = getQuickAddSpec('breed', { animalTypeId: 'at-1' });
      await expect(spec.create({ name: 'Sahiwal' }, { animalTypeId: 'at-1' })).resolves.toEqual({
        id: 'br-1',
        label: 'Sahiwal',
      });
      expect(configurationApi.createBreed).toHaveBeenCalledWith({
        name: 'Sahiwal',
        animalTypeId: 'at-1',
        averageGestationDays: 283,
      });
    });

    it('carries an edited gestation period through', async () => {
      vi.mocked(configurationApi.createBreed).mockResolvedValue(
        res({ id: 'br-2', name: 'Jersey', animalTypeId: 'at-1', averageGestationDays: 279 }),
      );

      const spec = getQuickAddSpec('breed', { animalTypeId: 'at-1' });
      await spec.create({ name: 'Jersey', averageGestationDays: 279 }, { animalTypeId: 'at-1' });
      expect(configurationApi.createBreed).toHaveBeenCalledWith({
        name: 'Jersey',
        animalTypeId: 'at-1',
        averageGestationDays: 279,
      });
    });

    it('explains itself and refuses to post without an animal type', async () => {
      const spec = getQuickAddSpec('breed', {});

      expect(spec.disabledReason?.({})).toBe('Select an Animal Type first, then add breeds for it');
      expect(spec.disabledReason?.({ animalTypeId: 'at-1' })).toBeUndefined();

      await expect(spec.create({ name: 'Sahiwal' }, {})).rejects.toThrow('Select an animal type first');
      expect(configurationApi.createBreed).not.toHaveBeenCalled();
    });
  });

  describe('location', () => {
    it('posts the new location against its type, omitting a blank parent', async () => {
      vi.mocked(configurationApi.createLocation).mockResolvedValue(res({ id: 'loc-1', name: 'Shed A' }));

      const spec = getQuickAddSpec('location', LOCATION_CTX);
      await expect(
        spec.create({ name: 'Shed A', locationTypeId: 'lt-1' }, LOCATION_CTX),
      ).resolves.toEqual({ id: 'loc-1', label: 'Shed A' });
      expect(configurationApi.createLocation).toHaveBeenCalledWith({
        name: 'Shed A',
        locationTypeId: 'lt-1',
        parentLocationId: undefined,
      });
    });

    it('posts the chosen parent when one is given', async () => {
      vi.mocked(configurationApi.createLocation).mockResolvedValue(res({ id: 'loc-2', name: 'Shed B' }));

      const spec = getQuickAddSpec('location', LOCATION_CTX);
      await spec.create({ name: 'Shed B', locationTypeId: 'lt-1', parentLocationId: 'loc-0' }, LOCATION_CTX);
      expect(configurationApi.createLocation).toHaveBeenCalledWith({
        name: 'Shed B',
        locationTypeId: 'lt-1',
        parentLocationId: 'loc-0',
      });
    });

    it('builds the top-level fields: name, prefilled type with inline add, and a parent', () => {
      const spec = getQuickAddSpec('location', LOCATION_CTX);

      expect(spec.fields.map((f) => f.name)).toEqual(['name', 'locationTypeId', 'parentLocationId']);
      const [name, type, parent] = spec.fields;
      expect(name.placeholder).toBe('e.g. Shed A');
      expect(type.initialValue).toBe('lt-1');
      expect(type.options).toEqual(LOCATION_CTX.locationTypes);
      expect(type.nestedQuickAdd).toEqual({ label: 'Location Type', kind: 'locationType' });
      expect(parent.options).toEqual(LOCATION_CTX.locations);
      expect(parent.nestedQuickAdd).toMatchObject({
        label: 'Location',
        kind: 'location',
        variant: 'nested',
      });
    });

    it('drops the parent select and the type nesting for a created parent location', () => {
      const spec = getQuickAddSpec('location', LOCATION_CTX, 'nested');

      expect(spec.fields.map((f) => f.name)).toEqual(['name', 'locationTypeId']);
      expect(spec.fields[0].placeholder).toBe('e.g. Shed B');
      // No third level: creating a parent's parent is not offered.
      expect(spec.fields[1].nestedQuickAdd).toBeUndefined();
    });
  });

  it('wires a feed type with every field the server requires', async () => {
    vi.mocked(feedTypesApi.create).mockResolvedValue(
      res({ id: 'ft-1', name: 'Alfalfa Hay', unitName: 'Bale' }),
    );

    const spec = getQuickAddSpec('feedType');
    expect(spec.fields.map((f) => f.name)).toEqual(['name', 'category', 'unit', 'costPerUnit', 'notes']);

    await expect(
      spec.create(
        { name: 'Alfalfa Hay', category: 'Forage', unit: 'Bale', costPerUnit: '12.5' },
        {},
      ),
    ).resolves.toEqual({ id: 'ft-1', label: 'Alfalfa Hay' });
    expect(feedTypesApi.create).toHaveBeenCalledWith({
      name: 'Alfalfa Hay',
      category: 'Forage',
      unit: 'Bale',
      costPerUnit: 12.5,
      notes: undefined,
    });
  });

  it('wires a vaccine type', async () => {
    vi.mocked(vaccineTypesApi.create).mockResolvedValue(res({ id: 'vt-1', name: 'FMD Vaccine' }));

    const spec = getQuickAddSpec('vaccineType');
    await expect(spec.create({ name: 'FMD Vaccine', defaultDosage: '5ml' }, {})).resolves.toEqual({
      id: 'vt-1',
      label: 'FMD Vaccine',
    });
    expect(vaccineTypesApi.create).toHaveBeenCalledWith({
      name: 'FMD Vaccine',
      defaultDosage: '5ml',
      notes: undefined,
    });
  });

  it('wires a medicine with the unit the server requires', async () => {
    vi.mocked(medicinesApi.create).mockResolvedValue(res({ id: 'med-1', name: 'Ivermectin' }));

    const spec = getQuickAddSpec('medicine');
    await expect(spec.create({ name: 'Ivermectin', unit: 'ml' }, {})).resolves.toEqual({
      id: 'med-1',
      label: 'Ivermectin',
    });
    expect(medicinesApi.create).toHaveBeenCalledWith({
      name: 'Ivermectin',
      description: undefined,
      unit: 'ml',
    });
  });

  it('wires the finance categories and payment methods', async () => {
    vi.mocked(expenseCategoriesApi.create).mockResolvedValue(res({ id: 'ec-1', name: 'Utilities' }));
    vi.mocked(incomeCategoriesApi.create).mockResolvedValue(res({ id: 'ic-1', name: 'Milk Sales' }));
    vi.mocked(paymentMethodsApi.create).mockResolvedValue(res({ id: 'pm-1', name: 'Cash' }));

    await expect(getQuickAddSpec('expenseCategory').create({ name: 'Utilities' }, {})).resolves.toEqual({
      id: 'ec-1',
      label: 'Utilities',
    });
    expect(expenseCategoriesApi.create).toHaveBeenCalledWith({
      name: 'Utilities',
      description: undefined,
    });

    await getQuickAddSpec('incomeCategory').create({ name: 'Milk Sales' }, {});
    expect(incomeCategoriesApi.create).toHaveBeenCalledWith({
      name: 'Milk Sales',
      description: undefined,
    });

    await getQuickAddSpec('paymentMethod').create({ name: 'Cash' }, {});
    expect(paymentMethodsApi.create).toHaveBeenCalledWith({ name: 'Cash', description: undefined });
  });

  it('wires departments and roles', async () => {
    vi.mocked(departmentsApi.create).mockResolvedValue(res({ id: 'dep-1', name: 'Milking' }));
    vi.mocked(employeeRolesApi.create).mockResolvedValue(res({ id: 'role-1', name: 'Milker' }));

    await expect(
      getQuickAddSpec('department').create({ name: 'Milking', description: 'Dairy unit' }, {}),
    ).resolves.toEqual({ id: 'dep-1', label: 'Milking' });
    expect(departmentsApi.create).toHaveBeenCalledWith({
      name: 'Milking',
      description: 'Dairy unit',
    });

    await getQuickAddSpec('employeeRole').create({ name: 'Milker' }, {});
    expect(employeeRolesApi.create).toHaveBeenCalledWith({ name: 'Milker', description: undefined });
  });

  it('wires suppliers and customers', async () => {
    vi.mocked(suppliersApi.create).mockResolvedValue(res({ id: 'sup-1', name: 'Agri Supplies' }));
    vi.mocked(customersApi.create).mockResolvedValue(res({ id: 'cus-1', name: 'Local Market' }));

    await expect(
      getQuickAddSpec('supplier').create({ name: 'Agri Supplies', contactInfo: '0700' }, {}),
    ).resolves.toEqual({ id: 'sup-1', label: 'Agri Supplies' });
    expect(suppliersApi.create).toHaveBeenCalledWith({
      name: 'Agri Supplies',
      contactInfo: '0700',
      productsSupplied: undefined,
    });

    await getQuickAddSpec('customer').create({ name: 'Local Market' }, {});
    expect(customersApi.create).toHaveBeenCalledWith({
      name: 'Local Market',
      contactInfo: undefined,
    });
  });
});
