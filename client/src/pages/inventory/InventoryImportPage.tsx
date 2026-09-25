import ImportWizard from '../../components/ImportWizard';
import { inventoryImportApi, TEMPLATE_EXAMPLE_ROW, TEMPLATE_HEADERS } from '../../api/inventoryImport';
import { useTranslation } from 'react-i18next';

/**
 * Bulk inventory import.
 *
 * Same wizard as animals and employees. The one inventory-specific rule worth knowing
 * while using it: the item name is the identifier, so a duplicate — already in the farm,
 * or repeated inside the file — is an error rather than a silent skip.
 */
const InventoryImportPage: React.FC = () => { const { t } = useTranslation('imports'); return (
  <ImportWizard
    title={t('importInventoryItems')}
    entityName="inventory item"
    listPath="/dashboard/inventory/items"
    listLabel="View inventory"
    templateFileName="inventory-import-template"
    templateHeaders={TEMPLATE_HEADERS}
    templateExampleRow={TEMPLATE_EXAMPLE_ROW}
    api={inventoryImportApi}
  />
); };

export default InventoryImportPage;
