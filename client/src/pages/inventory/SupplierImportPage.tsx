import ImportWizard from '../../components/ImportWizard';
import { supplierImportApi, TEMPLATE_EXAMPLE_ROW, TEMPLATE_HEADERS } from '../../api/supplierImport';
import { useTranslation } from 'react-i18next';

/**
 * Bulk supplier import.
 *
 * Same wizard as animals, employees and inventory items. The one supplier-specific rule
 * worth knowing: the name is the identifier, so a duplicate — already in the farm, or
 * repeated inside the file — is an error rather than a silent skip.
 */
const SupplierImportPage: React.FC = () => { const { t } = useTranslation('imports'); return (
  <ImportWizard
    title={t('importSuppliers')}
    entityName="supplier"
    listPath="/dashboard/inventory/suppliers"
    listLabel="View suppliers"
    templateFileName="supplier-import-template"
    templateHeaders={TEMPLATE_HEADERS}
    templateExampleRow={TEMPLATE_EXAMPLE_ROW}
    api={supplierImportApi}
  />
); };

export default SupplierImportPage;
