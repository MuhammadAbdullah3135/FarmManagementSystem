import ImportWizard from '../../components/ImportWizard';
import { employeeImportApi, TEMPLATE_EXAMPLE_ROW, TEMPLATE_HEADERS } from '../../api/employeeImport';

/**
 * Bulk employee import.
 *
 * Same wizard as animals and inventory. Two employee-specific rules the mapping step
 * makes visible: the department and the role have to match records the farm already has,
 * and the email is the identifier, so a duplicate is refused rather than skipped.
 */
const EmployeeImportPage: React.FC = () => (
  <ImportWizard
    title="Import employees"
    entityName="employee"
    listPath="/dashboard/hr/employees"
    listLabel="View employees"
    templateFileName="employee-import-template"
    templateHeaders={TEMPLATE_HEADERS}
    templateExampleRow={TEMPLATE_EXAMPLE_ROW}
    api={employeeImportApi}
  />
);

export default EmployeeImportPage;
