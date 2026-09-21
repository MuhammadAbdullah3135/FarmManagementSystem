/**
 * Inline quick-add registry.
 *
 * Maps each configurable lookup to the modal fields and create call used by
 * `LookupQuickAddSelect`, so a user can create an entry (a category, breed,
 * location, …) from inside the form that needs it instead of abandoning a
 * half-filled form to visit Configuration and come back.
 *
 * The field specs and payloads below are transcribed from the canonical
 * creation forms they mirror — `AnimalsPage` (animal type, breed, sex, status,
 * location, age category), `FeedTypesPage`, `MedicinesPage`, `VaccineTypesPage`,
 * `CategoriesPage`, `DepartmentsRolesPage`, `SuppliersPage` and `CustomersPage`.
 * Where a change here would alter an existing payload, that is a bug: the
 * requirements/defaults are the source of truth, not this module.
 *
 * Deliberate scope calls, so they are visible rather than surprising:
 * - Optional numeric tuning knobs (medicine `lowStockThreshold` /
 *   `expiringSoonDays`) are omitted — they can be set later on the owning page.
 * - `vaccineType` omits `linkedMedicineId`: including it would force a
 *   medicines list into two pages that do not otherwise need one.
 * Both omissions mirror the same fields being optional on the server.
 */
import { configurationApi } from '../api/configuration';
import { expenseCategoriesApi, incomeCategoriesApi, paymentMethodsApi } from '../api/finance';
import { feedTypesApi } from '../api/feed';
import { medicinesApi, vaccineTypesApi } from '../api/health';
import { departmentsApi, employeeRolesApi } from '../api/hr';
import { suppliersApi } from '../api/suppliers';
import { customersApi } from '../api/customers';
import { FEED_CATEGORIES, FEED_UNITS } from '../utils/feedOptions';

/** Every configurable lookup that can be created inline. */
export type LookupKind =
  | 'animalType'
  | 'breed'
  | 'sexOption'
  | 'ageCategory'
  | 'animalStatus'
  | 'locationType'
  | 'location'
  | 'feedType'
  | 'vaccineType'
  | 'medicine'
  | 'expenseCategory'
  | 'incomeCategory'
  | 'paymentMethod'
  | 'department'
  | 'employeeRole'
  | 'supplier'
  | 'customer';

/** Only the location chain differs between the top-level and nested forms. */
export type QuickAddVariant = 'top' | 'nested';

export interface SelectOption {
  value: string;
  label: string;
}

/**
 * The little state a create needs beyond its own form values.
 *
 * Every key exists because the current creation logic requires it: a breed
 * cannot be posted without its animal type, and a location's type/parent
 * selects need their option lists. Nothing page-specific belongs here.
 */
export interface QuickAddContext {
  animalTypeId?: string;
  locationTypes?: SelectOption[];
  locations?: SelectOption[];
}

/**
 * A nested quick-add, either registry-backed (`kind`) or explicit
 * (`fields` + `onQuickAdd`), matching the component's own two modes.
 */
export type NestedQuickAddSpec =
  | {
      /** Shown on the footer button and as the nested modal title. */
      label: string;
      kind: LookupKind;
      ctx?: QuickAddContext;
      variant?: QuickAddVariant;
    }
  | {
      label: string;
      fields: QuickAddFieldSpec[];
      onQuickAdd: (values: Record<string, unknown>) => Promise<string>;
    };

export interface QuickAddFieldSpec {
  name: string;
  label: string;
  widget: 'input' | 'textarea' | 'number' | 'select' | 'switch';
  required?: boolean;
  maxLength?: number;
  placeholder?: string;
  min?: number;
  max?: number;
  options?: { value: string | number; label: string }[];
  initialValue?: unknown;
  /**
   * Renders a select with its own inline quick-add. Either registry-backed
   * (`kind`) or explicit (`fields` + `onQuickAdd`).
   */
  nestedQuickAdd?: NestedQuickAddSpec;
}

export interface QuickAddSpec {
  label: string;
  fields: QuickAddFieldSpec[];
  /**
   * Creates the lookup and returns it normalised. Must let API errors
   * propagate untouched — the caller surfaces them and keeps the modal open.
   */
  create: (
    values: Record<string, unknown>,
    ctx: QuickAddContext,
  ) => Promise<{ id: string; label: string }>;
  /** Why inline creation is unavailable, or undefined when it is allowed. */
  disabledReason?: (ctx: QuickAddContext) => string | undefined;
}

interface QuickAddDefinition
  extends Omit<QuickAddSpec, 'fields'> {
  fields: (ctx: QuickAddContext, variant: QuickAddVariant) => QuickAddFieldSpec[];
}

const STATUS_CATEGORY_LABELS: Record<number, string> = {
  0: 'Active',
  1: 'Inactive',
  2: 'Terminal',
};

const selectOptions = (values: readonly (string | number)[]): { value: string | number; label: string }[] =>
  values.map((v) => ({ value: v, label: String(v) }));

/** A field list that does not depend on context or variant. */
const fixed =
  (fields: QuickAddFieldSpec[]) =>
  (): QuickAddFieldSpec[] =>
    fields;

const nameField = (label: string, placeholder: string, maxLength = 100): QuickAddFieldSpec => ({
  name: 'name',
  label,
  widget: 'input',
  required: true,
  maxLength,
  placeholder,
});

const descriptionField = (): QuickAddFieldSpec => ({
  name: 'description',
  label: 'Description',
  widget: 'input',
  maxLength: 500,
});

// ─── Location chain ───────────────────────────────────────────────────────
// A location needs a type, and the type can itself be created from here. The
// nested variant (used when creating a *parent* location from inside the Add
// Location modal) deliberately offers no further nesting.

const locationTypeField = (ctx: QuickAddContext, withNestedAdd: boolean): QuickAddFieldSpec => ({
  name: 'locationTypeId',
  label: 'Location Type',
  widget: 'select',
  required: true,
  placeholder: 'Select location type',
  initialValue: ctx.locationTypes?.[0]?.value,
  options: ctx.locationTypes ?? [],
  ...(withNestedAdd
    ? { nestedQuickAdd: { label: 'Location Type', kind: 'locationType' } satisfies NestedQuickAddSpec }
    : {}),
});

const parentLocationField = (ctx: QuickAddContext): QuickAddFieldSpec => ({
  name: 'parentLocationId',
  label: 'Parent Location (optional)',
  widget: 'select',
  placeholder: 'None (top level)',
  options: ctx.locations ?? [],
  nestedQuickAdd: {
    label: 'Location',
    kind: 'location',
    variant: 'nested',
    ctx: { locationTypes: ctx.locationTypes },
  },
});

const definitions: Record<LookupKind, QuickAddDefinition> = {
  animalType: {
    label: 'Animal Type',
    fields: fixed([nameField('Name', 'e.g. Cattle')]),
    create: async (values) => {
      const res = await configurationApi.createAnimalType({ name: String(values.name) });
      const created = res.data;
      return { id: created.id, label: created.name };
    },
  },

  breed: {
    label: 'Breed',
    fields: fixed([
      nameField('Name', 'e.g. Sahiwal'),
      {
        name: 'averageGestationDays',
        label: 'Average Gestation (days)',
        widget: 'number',
        min: 1,
        max: 999,
        initialValue: 283,
      },
    ]),
    disabledReason: (ctx) =>
      ctx.animalTypeId ? undefined : 'Select an Animal Type first, then add breeds for it',
    create: async (values, ctx) => {
      if (!ctx.animalTypeId) throw new Error('Select an animal type first');
      const res = await configurationApi.createBreed({
        name: String(values.name),
        animalTypeId: ctx.animalTypeId,
        averageGestationDays: Number(values.averageGestationDays ?? 283),
      });
      const created = res.data;
      return { id: created.id, label: created.name };
    },
  },

  sexOption: {
    label: 'Sex',
    fields: fixed([
      {
        name: 'value',
        label: 'Value',
        widget: 'input',
        required: true,
        maxLength: 50,
        placeholder: 'e.g. Female',
      },
    ]),
    create: async (values) => {
      const res = await configurationApi.createSexOption({ value: String(values.value) });
      const created = res.data;
      return { id: created.id, label: created.value };
    },
  },

  ageCategory: {
    label: 'Age Category',
    fields: fixed([
      nameField('Name', 'e.g. Calf'),
      { name: 'minDays', label: 'Min Age (days)', widget: 'number', min: 0, initialValue: 0 },
      { name: 'maxDays', label: 'Max Age (days)', widget: 'number', min: 0, initialValue: 99999 },
    ]),
    create: async (values) => {
      const res = await configurationApi.createAgeCategory({
        name: String(values.name),
        minDays: Number(values.minDays ?? 0),
        maxDays: Number(values.maxDays ?? 99999),
      });
      const created = res.data;
      return { id: created.id, label: created.name };
    },
  },

  animalStatus: {
    label: 'Status',
    fields: fixed([
      nameField('Name', 'e.g. Quarantined'),
      {
        name: 'category',
        label: 'Category',
        widget: 'select',
        initialValue: 0,
        options: Object.keys(STATUS_CATEGORY_LABELS).map((k) => ({
          value: Number(k),
          label: STATUS_CATEGORY_LABELS[Number(k)],
        })),
      },
      { name: 'isActive', label: 'Active', widget: 'switch', initialValue: true },
    ]),
    create: async (values) => {
      const res = await configurationApi.createStatus({
        name: String(values.name),
        isActive: Boolean(values.isActive ?? true),
        category: Number(values.category ?? 0),
      });
      const created = res.data as { id: string; name: string };
      return { id: created.id, label: created.name };
    },
  },

  locationType: {
    label: 'Location Type',
    fields: fixed([nameField('Name', 'e.g. Shed')]),
    create: async (values) => {
      const res = await configurationApi.createLocationType({ name: String(values.name) });
      const created = res.data as { id: string; name: string };
      return { id: created.id, label: created.name };
    },
  },

  location: {
    label: 'Location',
    fields: (ctx, variant) =>
      variant === 'nested'
        ? [nameField('Name', 'e.g. Shed B', 200), locationTypeField(ctx, false)]
        : [
            nameField('Name', 'e.g. Shed A', 200),
            locationTypeField(ctx, true),
            parentLocationField(ctx),
          ],
    create: async (values) => {
      const res = await configurationApi.createLocation({
        name: String(values.name),
        locationTypeId: String(values.locationTypeId),
        parentLocationId: values.parentLocationId ? String(values.parentLocationId) : undefined,
      });
      const created = res.data;
      return { id: created.id, label: created.name };
    },
  },

  feedType: {
    label: 'Feed Type',
    fields: fixed([
      nameField('Name', 'e.g. Alfalfa Hay'),
      {
        name: 'category',
        label: 'Category',
        widget: 'select',
        required: true,
        options: selectOptions(FEED_CATEGORIES),
      },
      {
        name: 'unit',
        label: 'Unit',
        widget: 'select',
        required: true,
        options: selectOptions(FEED_UNITS),
      },
      { name: 'costPerUnit', label: 'Cost per Unit', widget: 'number', required: true, min: 0 },
      { name: 'notes', label: 'Notes', widget: 'textarea', maxLength: 500 },
    ]),
    create: async (values) => {
      const res = await feedTypesApi.create({
        name: String(values.name),
        category: String(values.category),
        unit: String(values.unit),
        costPerUnit: Number(values.costPerUnit),
        notes: values.notes ? String(values.notes) : undefined,
      });
      const created = res.data;
      return { id: created.id, label: created.name };
    },
  },

  vaccineType: {
    label: 'Vaccine Type',
    fields: fixed([
      nameField('Name', 'e.g. FMD Vaccine', 200),
      {
        name: 'defaultDosage',
        label: 'Default Dosage',
        widget: 'input',
        maxLength: 200,
        placeholder: 'e.g. 5ml',
      },
      { name: 'notes', label: 'Notes', widget: 'textarea', maxLength: 1000 },
    ]),
    create: async (values) => {
      const res = await vaccineTypesApi.create({
        name: String(values.name),
        defaultDosage: values.defaultDosage ? String(values.defaultDosage) : undefined,
        notes: values.notes ? String(values.notes) : undefined,
      });
      const created = res.data;
      return { id: created.id, label: created.name };
    },
  },

  medicine: {
    label: 'Medicine',
    fields: fixed([
      nameField('Name', 'e.g. Ivermectin', 200),
      { name: 'description', label: 'Description', widget: 'textarea', maxLength: 1000 },
      {
        name: 'unit',
        label: 'Unit',
        widget: 'input',
        required: true,
        maxLength: 50,
        placeholder: 'e.g. doses, ml, tablets',
      },
    ]),
    create: async (values) => {
      const res = await medicinesApi.create({
        name: String(values.name),
        description: values.description ? String(values.description) : undefined,
        unit: String(values.unit),
      });
      const created = res.data;
      return { id: created.id, label: created.name };
    },
  },

  expenseCategory: {
    label: 'Expense Category',
    fields: fixed([nameField('Name', 'e.g. Feed, Utilities, Medicine, Equipment'), descriptionField()]),
    create: async (values) => {
      const res = await expenseCategoriesApi.create({
        name: String(values.name),
        description: values.description ? String(values.description) : undefined,
      });
      const created = res.data;
      return { id: created.id, label: created.name };
    },
  },

  incomeCategory: {
    label: 'Income Category',
    fields: fixed([
      nameField('Name', 'e.g. Crop Sales, Livestock Sales, Dairy'),
      descriptionField(),
    ]),
    create: async (values) => {
      const res = await incomeCategoriesApi.create({
        name: String(values.name),
        description: values.description ? String(values.description) : undefined,
      });
      const created = res.data;
      return { id: created.id, label: created.name };
    },
  },

  paymentMethod: {
    label: 'Payment Method',
    fields: fixed([
      nameField('Name', 'e.g. Cash, Bank Transfer, Mobile Money, Check'),
      descriptionField(),
    ]),
    create: async (values) => {
      const res = await paymentMethodsApi.create({
        name: String(values.name),
        description: values.description ? String(values.description) : undefined,
      });
      const created = res.data;
      return { id: created.id, label: created.name };
    },
  },

  department: {
    label: 'Department',
    fields: fixed([nameField('Name', 'e.g. Milking'), descriptionField()]),
    create: async (values) => {
      const res = await departmentsApi.create({
        name: String(values.name),
        description: values.description ? String(values.description) : undefined,
      });
      const created = res.data as { id: string; name: string };
      return { id: created.id, label: created.name };
    },
  },

  employeeRole: {
    label: 'Role',
    fields: fixed([nameField('Name', 'e.g. Milker'), descriptionField()]),
    create: async (values) => {
      const res = await employeeRolesApi.create({
        name: String(values.name),
        description: values.description ? String(values.description) : undefined,
      });
      const created = res.data as { id: string; name: string };
      return { id: created.id, label: created.name };
    },
  },

  supplier: {
    label: 'Supplier',
    fields: fixed([
      nameField('Name', 'e.g. Agri Supplies Ltd', 200),
      { name: 'contactInfo', label: 'Contact information', widget: 'textarea', maxLength: 1000 },
      { name: 'productsSupplied', label: 'Products supplied', widget: 'textarea', maxLength: 2000 },
    ]),
    create: async (values) => {
      const res = await suppliersApi.create({
        name: String(values.name),
        contactInfo: values.contactInfo ? String(values.contactInfo) : undefined,
        productsSupplied: values.productsSupplied ? String(values.productsSupplied) : undefined,
      });
      const created = res.data;
      return { id: created.id, label: created.name };
    },
  },

  customer: {
    label: 'Customer',
    fields: fixed([
      nameField('Name', 'e.g. Local Market', 200),
      { name: 'contactInfo', label: 'Contact information', widget: 'textarea', maxLength: 1000 },
    ]),
    create: async (values) => {
      const res = await customersApi.create({
        name: String(values.name),
        contactInfo: values.contactInfo ? String(values.contactInfo) : undefined,
      });
      const created = res.data;
      return { id: created.id, label: created.name };
    },
  },
};

/**
 * Resolves a lookup kind to the field list, create call and any gating reason.
 * `ctx` supplies the values the current logic depends on (see
 * {@link QuickAddContext}); `variant` only affects the location chain.
 */
export const getQuickAddSpec = (
  kind: LookupKind,
  ctx: QuickAddContext = {},
  variant: QuickAddVariant = 'top',
): QuickAddSpec => {
  const definition = definitions[kind];
  return {
    label: definition.label,
    fields: definition.fields(ctx, variant),
    create: definition.create,
    disabledReason: definition.disabledReason,
  };
};
