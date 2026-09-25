import ImportWizard from '../../components/ImportWizard';
import { animalImportApi, TEMPLATE_EXAMPLE_ROW, TEMPLATE_HEADERS } from '../../api/animalImport';
import { useTranslation } from 'react-i18next';

/**
 * Bulk animal import.
 *
 * The wizard itself is shared with the employee and inventory importers; the only
 * animal-specific things are its name, its template, its endpoint and where "view them"
 * goes.
 */
const AnimalImportPage: React.FC = () => { const { t } = useTranslation('imports'); return (
  <ImportWizard
    title={t('importAnimals')}
    entityName="animal"
    listPath="/dashboard/animals"
    listLabel="View animals"
    templateFileName="animal-import-template"
    templateHeaders={TEMPLATE_HEADERS}
    templateExampleRow={TEMPLATE_EXAMPLE_ROW}
    api={animalImportApi}
  />
); };

export default AnimalImportPage;
