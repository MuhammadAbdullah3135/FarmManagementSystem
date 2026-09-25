import { createImportApi } from './importApi';

/**
 * The income importer, on the shared import contract.
 *
 * Only the endpoint's base path and the template's wording are income-specific. As with
 * expenses, an income record has no identifier, so there is deliberately no duplicate
 * rule — see {@link INCOME_DUPLICATE_NOTICE}.
 */
export const incomeImportApi = createImportApi('/finance/income-records/import');

/**
 * The disclosed limitation: an income record has no natural identifier, so the importer
 * cannot reject duplicates without also rejecting legitimate repeated sales. The wizard
 * shows this before commit.
 */
export const INCOME_DUPLICATE_NOTICE =
  'An income record has no unique identifier, so this import does not check for ' +
  'duplicates. Two identical-looking rows (same date, amount and category) are both ' +
  'imported — check your file for repeated rows before committing.';

/**
 * The headers written into the downloadable template.
 *
 * Mirrors FMS.Application.Finance.Import.IncomeImportFields.All. Every header here is
 * matched by the server's auto-detection.
 */
export const TEMPLATE_HEADERS = [
  'Income date',
  'Amount',
  'Income category',
  'Payment method',
  'Animal tag',
  'Location',
  'Description',
] as const;

export const TEMPLATE_EXAMPLE_ROW = [
  '2026-01-20',
  '2500.00',
  'Milk sales',
  'Bank transfer',
  '',
  'Milking shed',
  'Morning milk sale',
];
