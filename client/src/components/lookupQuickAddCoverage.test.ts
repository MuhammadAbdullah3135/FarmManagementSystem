/// <reference types="node" />
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { describe, expect, it } from 'vitest';
import type { LookupKind } from './lookupQuickAdd';

/*
 * Guards the inline quick-add rollout by reading the forms' source.
 *
 * Why source and not a rendered assertion: the failure this exists to catch is
 * a field silently reverting to a plain `<Select>` (or being wired to the wrong
 * kind, which the type system cannot see — `'supplier'` and `'customer'` are
 * both valid kinds). Rendering every one of these forms in jsdom to check the
 * same thing would cost minutes of antd overlay walking for a check that is
 * about the wiring, not the pixels. Same reasoning as AppLayout.css.test.ts.
 *
 * Each entry pins one configurable field to the lookup kind it must create:
 * `{ file, marker, kind }`, where `marker` is the source text of the field's
 * Form.Item. `kind` is typed as LookupKind, so renaming a kind fails to compile
 * here — which is the point: a rename must be reflected in the forms too.
 */
interface ExpectedField {
  file: string;
  marker: string;
  kind: LookupKind;
}

const EXPECTED: ExpectedField[] = [
  // Animals: the original quick-add surface.
  { file: 'pages/animals/AnimalsPage.tsx', marker: 'name="animalTypeId"', kind: 'animalType' },
  { file: 'pages/animals/AnimalsPage.tsx', marker: 'name="breedId"', kind: 'breed' },
  { file: 'pages/animals/AnimalsPage.tsx', marker: 'name="sexOptionId"', kind: 'sexOption' },
  { file: 'pages/animals/AnimalsPage.tsx', marker: 'name="animalStatusId"', kind: 'animalStatus' },
  { file: 'pages/animals/AnimalsPage.tsx', marker: 'name="locationId"', kind: 'location' },
  { file: 'pages/animals/AnimalsPage.tsx', marker: 'name="ageCategoryId"', kind: 'ageCategory' },

  // Finance: categories, payment methods and locations used to force a detour.
  { file: 'pages/finance/ExpensesPage.tsx', marker: 'name="expenseCategoryId"', kind: 'expenseCategory' },
  { file: 'pages/finance/ExpensesPage.tsx', marker: 'name="paymentMethodId"', kind: 'paymentMethod' },
  { file: 'pages/finance/ExpensesPage.tsx', marker: 'name="locationId"', kind: 'location' },
  { file: 'pages/finance/IncomesPage.tsx', marker: 'name="incomeCategoryId"', kind: 'incomeCategory' },
  { file: 'pages/finance/IncomesPage.tsx', marker: 'name="paymentMethodId"', kind: 'paymentMethod' },
  { file: 'pages/finance/IncomesPage.tsx', marker: 'name="locationId"', kind: 'location' },

  // Feed.
  { file: 'pages/feed/FeedRecordsPage.tsx', marker: 'name="feedTypeId"', kind: 'feedType' },
  { file: 'pages/feed/FeedRecordsPage.tsx', marker: 'name="locationId"', kind: 'location' },
  { file: 'pages/feed/DietPlansPage.tsx', marker: 'name="animalTypeId"', kind: 'animalType' },
  { file: 'pages/feed/DietPlansPage.tsx', marker: 'name="ageCategoryId"', kind: 'ageCategory' },
  { file: 'pages/feed/DietPlansPage.tsx', marker: 'name="feedTypeId"', kind: 'feedType' },

  // Health.
  { file: 'pages/health/VaccinationRecordsPage.tsx', marker: 'name="vaccineTypeId"', kind: 'vaccineType' },
  { file: 'pages/health/VaccinationSchedulePage.tsx', marker: 'name="vaccineTypeId"', kind: 'vaccineType' },
  { file: 'pages/health/VaccinationSchedulePage.tsx', marker: 'name="animalTypeId"', kind: 'animalType' },
  { file: 'pages/health/WeightCheckSchedulePage.tsx', marker: 'name="animalTypeId"', kind: 'animalType' },
  { file: 'pages/health/WeightCheckSchedulePage.tsx', marker: 'name="breedId"', kind: 'breed' },
  { file: 'pages/health/WeightCheckSchedulePage.tsx', marker: 'name="ageCategoryId"', kind: 'ageCategory' },
  { file: 'pages/health/VaccineTypesPage.tsx', marker: 'name="linkedMedicineId"', kind: 'medicine' },

  // HR.
  { file: 'pages/hr/EmployeesPage.tsx', marker: 'name="departmentId"', kind: 'department' },
  { file: 'pages/hr/EmployeesPage.tsx', marker: 'name="employeeRoleId"', kind: 'employeeRole' },

  // Breeding: each offspring row's sex.
  { file: 'pages/breeding/BirthRecordingPage.tsx', marker: "'sexOptionId'", kind: 'sexOption' },

  // Configuration: the page must not need a detour into itself.
  { file: 'pages/configuration/ConfigurationPage.tsx', marker: 'name="animalTypeId"', kind: 'animalType' },
  { file: 'pages/configuration/ConfigurationPage.tsx', marker: 'name="locationTypeId"', kind: 'locationType' },
  { file: 'pages/configuration/ConfigurationPage.tsx', marker: 'name="parentLocationId"', kind: 'location' },

  // Inventory: the purchase/sale forms, including the two dead category selects.
  { file: 'pages/inventory/SuppliersPage.tsx', marker: 'name="supplierId"', kind: 'supplier' },
  { file: 'pages/inventory/SuppliersPage.tsx', marker: 'name="expenseCategoryId"', kind: 'expenseCategory' },
  { file: 'pages/inventory/SuppliersPage.tsx', marker: 'name="paymentMethodId"', kind: 'paymentMethod' },
  { file: 'pages/inventory/CustomersPage.tsx', marker: 'name="customerId"', kind: 'customer' },
];

/** vitest runs with the vite root (client/) as its cwd, here and in CI. */
const source = (file: string) =>
  readFileSync(path.resolve(process.cwd(), 'src', file), 'utf8').replace(
    /\/\*[\s\S]*?\*\/|\/\/[^\n]*/g,
    '',
  );

/** The field's Form.Item block: from its name to its own closing tag. */
const fieldBlock = (src: string, marker: string, file: string): string => {
  const start = src.indexOf(marker);
  if (start < 0) throw new Error(`${file} no longer has a Form.Item matching ${marker}`);
  const end = src.indexOf('</Form.Item>', start);
  return src.slice(start, end < 0 ? start + 600 : end);
};

const files = [...new Set(EXPECTED.map((e) => e.file))];

describe('inline quick-add coverage', () => {
  it.each(files)('%s imports the quick-add select', (file) => {
    expect(source(file)).toContain("from '../../components/LookupQuickAddSelect'");
  });

  it.each(EXPECTED)('$file: $marker creates a $kind inline', ({ file, marker, kind }) => {
    const block = fieldBlock(source(file), marker, file);

    expect(block).toContain(`kind="${kind}"`);
    // The regression this guards: the field falling back to a plain select.
    expect(block).not.toMatch(/<Select[\s/>]/);
  });
});
