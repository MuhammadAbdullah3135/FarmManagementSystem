import { createImportApi } from './importApi';

/**
 * The expense importer, on the shared import contract.
 *
 * Only the endpoint's base path and the template's wording are expense-specific. The one
 * behaviour worth knowing: an expense has no identifier, so there is deliberately no
 * duplicate rule — see {@link EXPENSE_DUPLICATE_NOTICE}.
 */
export const expenseImportApi = createImportApi('/finance/expenses/import');

/**
 * The disclosed limitation: unlike suppliers, customers, inventory and employees, an
 * expense has no natural identifier, so the importer cannot reject duplicates without
 * also rejecting legitimate repeated transactions. The wizard shows this before commit.
 */
export const EXPENSE_DUPLICATE_NOTICE =
  'An expense has no unique identifier, so this import does not check for duplicates. ' +
  'Two identical-looking rows (same date, amount and category) are both imported — check ' +
  'your file for repeated rows before committing.';

/**
 * The headers written into the downloadable template.
 *
 * Mirrors FMS.Application.Finance.Import.ExpenseImportFields.All. Every header here is
 * matched by the server's auto-detection.
 */
export const TEMPLATE_HEADERS = [
  'Expense date',
  'Amount',
  'Expense category',
  'Payment method',
  'Animal tag',
  'Location',
  'Description',
] as const;

export const TEMPLATE_EXAMPLE_ROW = [
  '2026-01-15',
  '1500.50',
  'Feed',
  'Cash',
  'TAG-0001',
  'Store room',
  'Dairy meal purchase',
];
