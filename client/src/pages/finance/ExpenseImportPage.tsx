import ImportWizard from '../../components/ImportWizard';
import {
  EXPENSE_DUPLICATE_NOTICE,
  expenseImportApi,
  TEMPLATE_EXAMPLE_ROW,
  TEMPLATE_HEADERS,
} from '../../api/expenseImport';
import { useTranslation } from 'react-i18next';

/**
 * Bulk expense import.
 *
 * Same wizard as every other importer, with one deliberate difference: an expense has no
 * identifier, so — unlike suppliers, customers, inventory items and employees — there is
 * no duplicate rule. The limitation is disclosed on the review step before commit rather
 * than invented as a rule the add form does not have.
 */
const ExpenseImportPage: React.FC = () => { const { t } = useTranslation('imports'); return (
  <ImportWizard
    title={t('importExpenses')}
    entityName="expense"
    listPath="/dashboard/finance/expenses"
    listLabel="View expenses"
    templateFileName="expense-import-template"
    templateHeaders={TEMPLATE_HEADERS}
    templateExampleRow={TEMPLATE_EXAMPLE_ROW}
    api={expenseImportApi}
    notice={EXPENSE_DUPLICATE_NOTICE}
  />
); };

export default ExpenseImportPage;
