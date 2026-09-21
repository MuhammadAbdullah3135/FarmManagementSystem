import { createImportApi } from './importApi';

/**
 * The employee importer, on the shared import contract.
 *
 * The department and the role resolve by name against the farm, and the email is the
 * employee's identifier — all of that is the server's rule set, shared with the single
 * employee create endpoint. The client only supplies the endpoint and the template.
 */
export const employeeImportApi = createImportApi('/employees/import');

/**
 * The headers written into the downloadable template.
 *
 * Mirrors FMS.Application.Employees.Import.EmployeeImportFields.All. Every header here
 * is matched by the server's auto-detection, so this list only decides the wording of
 * the empty template file — the mapping table itself is rendered from /preview.
 *
 * The department, role and salary type in the example row are the values a farm most
 * often already has; the import refuses a name that does not exist in the farm, so a row
 * copied from this template unchanged may still need those columns edited.
 */
export const TEMPLATE_HEADERS = [
  'First name',
  'Last name',
  'Email',
  'Phone',
  'Address',
  'Department',
  'Role',
  'Salary type',
  'Salary rate',
  'Hire date',
  'Notes',
] as const;

export const TEMPLATE_EXAMPLE_ROW = [
  'Amina',
  'Yusuf',
  'amina.yusuf@example.com',
  '+254 700 000000',
  '',
  'Dairy',
  'Milker',
  'Monthly',
  '45000',
  '2024-01-15',
  'Imported from the staff register',
];
