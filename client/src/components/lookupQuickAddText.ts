import type { TFunction } from 'i18next';

/**
 * Copy authored in `lookupQuickAdd.ts`, translated at the point of display.
 *
 * The registry is plain data: it also defines API payloads and is unit-tested in
 * English, so it cannot call `useTranslation`. Keeping the mapping here means one
 * change translates the footer, the modal and every page that uses the picker —
 * not just the feed record form that exposed the gap.
 *
 * Only fixed chrome belongs in this table. A farm's own lookup names ("Shed A",
 * a supplier, a seeded status) pass through untouched: they are data, not copy,
 * and must keep the spelling the farm gave them in every language.
 */
const TEXT_KEYS: Record<string, string> = {
  // Frames built around the lookup label.
  'Select an Animal Type first, then add breeds for it': 'common:quickAddBreedNeedsAnimalType',

  // Lookup names (also used for the footer, modal title and toasts).
  'Animal Type': 'common:quickAddAnimalType',
  Breed: 'common:quickAddBreed',
  Sex: 'common:quickAddSex',
  'Age Category': 'common:quickAddAgeCategory',
  Status: 'common:quickAddStatus',
  'Location Type': 'common:quickAddLocationType',
  Location: 'common:quickAddLocation',
  'Feed Type': 'common:quickAddFeedType',
  'Vaccine Type': 'common:quickAddVaccineType',
  Medicine: 'common:quickAddMedicine',
  'Expense Category': 'common:quickAddExpenseCategory',
  'Income Category': 'common:quickAddIncomeCategory',
  'Payment Method': 'common:quickAddPaymentMethod',
  Department: 'common:quickAddDepartment',
  Role: 'common:quickAddRole',
  Supplier: 'common:quickAddSupplier',
  Customer: 'common:quickAddCustomer',

  // Field labels and fixed option values.
  Name: 'common:quickAddName',
  Description: 'common:quickAddDescription',
  'Average Gestation (days)': 'common:quickAddAverageGestationDays',
  'Min Age (days)': 'common:quickAddMinAgeDays',
  'Max Age (days)': 'common:quickAddMaxAgeDays',
  Value: 'common:quickAddValue',
  Category: 'common:quickAddCategory',
  Active: 'feed:active',
  Inactive: 'feed:inactive',
  Terminal: 'common:quickAddTerminal',
  Unit: 'common:quickAddUnit',
  'Cost per Unit': 'common:quickAddCostPerUnit',
  Notes: 'common:quickAddNotes',
  'Parent Location (optional)': 'common:quickAddParentLocationOptional',
  'Default Dosage': 'common:quickAddDefaultDosage',
  'Contact information': 'common:quickAddContactInformation',
  'Products supplied': 'common:quickAddProductsSupplied',
  'Select location type': 'common:quickAddSelectLocationType',
  'None (top level)': 'common:quickAddNoneTopLevel',

  // Input examples (whole placeholders, so the example reads naturally in RTL).
  'e.g. Cattle': 'common:quickAddExampleAnimalType',
  'e.g. Sahiwal': 'common:quickAddExampleBreed',
  'e.g. Female': 'common:quickAddExampleSex',
  'e.g. Calf': 'common:quickAddExampleAgeCategory',
  'e.g. Quarantined': 'common:quickAddExampleStatus',
  'e.g. Shed': 'common:quickAddExampleLocationType',
  'e.g. Shed A': 'common:quickAddExampleLocationTop',
  'e.g. Shed B': 'common:quickAddExampleLocationNested',
  'e.g. Alfalfa Hay': 'common:quickAddExampleFeedType',
  'e.g. FMD Vaccine': 'common:quickAddExampleVaccineType',
  'e.g. Ivermectin': 'common:quickAddExampleMedicine',
  'e.g. Feed, Utilities, Medicine, Equipment': 'common:quickAddExampleExpenseCategory',
  'e.g. Crop Sales, Livestock Sales, Dairy': 'common:quickAddExampleIncomeCategory',
  'e.g. Cash, Bank Transfer, Mobile Money, Check': 'common:quickAddExamplePaymentMethod',
  'e.g. Milking': 'common:quickAddExampleDepartment',
  'e.g. Milker': 'common:quickAddExampleRole',
  'e.g. Agri Supplies Ltd': 'common:quickAddExampleSupplier',
  'e.g. Local Market': 'common:quickAddExampleCustomer',
  'e.g. 5ml': 'common:quickAddExampleDosage',
  'e.g. doses, ml, tablets': 'common:quickAddExampleMedicineUnit',

  // Preset feed categories and units. The wire value stays English; only the
  // label handed to the Select is translated.
  Forage: 'feed:categoryForage',
  Concentrate: 'feed:categoryConcentrate',
  Mineral: 'feed:categoryMineral',
  Supplement: 'feed:categorySupplement',
  Additive: 'feed:categoryAdditive',
  Other: 'feed:categoryOther',
  Kilogram: 'feed:optionUnitKilogram',
  Gram: 'feed:optionUnitGram',
  Ton: 'feed:optionUnitTon',
  Liter: 'feed:optionUnitLiter',
  Bale: 'feed:optionUnitBale',
  Bag: 'feed:optionUnitBag',
};

/** Translate one piece of registry chrome, leaving farm-owned data as it is. */
export const quickAddText = (t: TFunction, text?: string): string => {
  if (!text) return '';
  const key = TEXT_KEYS[text];
  return key ? t(key) : text;
};
