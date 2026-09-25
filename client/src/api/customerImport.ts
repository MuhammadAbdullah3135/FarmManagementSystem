import { createImportApi } from './importApi';

/**
 * The customer importer, on the shared import contract.
 *
 * Only the endpoint's base path and the template's wording are customer-specific; the
 * pipeline behind it is the same one every other importer uses.
 */
export const customerImportApi = createImportApi('/inventory/customers/import');

/**
 * The headers written into the downloadable template.
 *
 * Mirrors FMS.Application.Inventory.Import.CustomerImportFields.All. Every header here is
 * matched by the server's auto-detection.
 */
export const TEMPLATE_HEADERS = [
  'Customer name',
  'Contact information',
] as const;

export const TEMPLATE_EXAMPLE_ROW = [
  'Nairobi Dairy Co-op',
  '+254 711 111111',
];
