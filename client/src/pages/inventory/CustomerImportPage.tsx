import ImportWizard from '../../components/ImportWizard';
import { customerImportApi, TEMPLATE_EXAMPLE_ROW, TEMPLATE_HEADERS } from '../../api/customerImport';
import { useTranslation } from 'react-i18next';

/**
 * Bulk customer import.
 *
 * Same wizard as every other importer. The one customer-specific rule worth knowing: the
 * name is the identifier, so a duplicate — already in the farm, or repeated inside the
 * file — is an error rather than a silent skip.
 */
const CustomerImportPage: React.FC = () => { const { t } = useTranslation('imports'); return (
  <ImportWizard
    title={t('importCustomers')}
    entityName="customer"
    listPath="/dashboard/inventory/customers"
    listLabel="View customers"
    templateFileName="customer-import-template"
    templateHeaders={TEMPLATE_HEADERS}
    templateExampleRow={TEMPLATE_EXAMPLE_ROW}
    api={customerImportApi}
  />
); };

export default CustomerImportPage;
