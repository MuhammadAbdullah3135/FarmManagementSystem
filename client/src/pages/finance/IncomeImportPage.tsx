import ImportWizard from '../../components/ImportWizard';
import {
  INCOME_DUPLICATE_NOTICE,
  incomeImportApi,
  TEMPLATE_EXAMPLE_ROW,
  TEMPLATE_HEADERS,
} from '../../api/incomeImport';
import { useTranslation } from 'react-i18next';

/**
 * Bulk income import.
 *
 * Same wizard as every other importer, with one deliberate difference: an income record
 * has no identifier, so there is no duplicate rule. The limitation is disclosed on the
 * review step before commit.
 */
const IncomeImportPage: React.FC = () => { const { t } = useTranslation('imports'); return (
  <ImportWizard
    title={t('importIncome')}
    entityName="income record"
    listPath="/dashboard/finance/incomes"
    listLabel="View income"
    templateFileName="income-import-template"
    templateHeaders={TEMPLATE_HEADERS}
    templateExampleRow={TEMPLATE_EXAMPLE_ROW}
    api={incomeImportApi}
    notice={INCOME_DUPLICATE_NOTICE}
  />
); };

export default IncomeImportPage;
