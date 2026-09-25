import { createImportApi } from './importApi';

/**
 * The supplier importer, on the shared import contract.
 *
 * Only the endpoint's base path and the template's wording are supplier-specific; the
 * pipeline behind it — column mapping, per-row validation, duplicate handling — is the
 * same one every other importer uses.
 */
export const supplierImportApi = createImportApi('/inventory/suppliers/import');

/**
 * The headers written into the downloadable template.
 *
 * Mirrors FMS.Application.Inventory.Import.SupplierImportFields.All. Every header here
 * is matched by the server's auto-detection, so this list only decides the wording of the
 * empty template file — the mapping table itself is rendered from /preview.
 */
export const TEMPLATE_HEADERS = [
  'Supplier name',
  'Contact information',
  'Products supplied',
] as const;

export const TEMPLATE_EXAMPLE_ROW = [
  'Kilimo Feeds',
  '+254 700 000000',
  'Feed, mineral supplements',
];
