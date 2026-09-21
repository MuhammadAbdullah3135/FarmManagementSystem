import { createImportApi } from './importApi';

/**
 * The inventory importer, on the shared import contract.
 *
 * Only the endpoint's base path and the template's wording are inventory-specific; the
 * pipeline behind it — column mapping, per-row validation, duplicate handling — is the
 * same one the animal and employee importers use.
 */
export const inventoryImportApi = createImportApi('/inventory-items/import');

/**
 * The headers written into the downloadable template.
 *
 * Mirrors FMS.Application.Inventory.Import.InventoryImportFields.All. Every header here
 * is matched by the server's auto-detection, so this list only decides the wording of
 * the empty template file — the mapping table itself is rendered from /preview.
 */
export const TEMPLATE_HEADERS = [
  'Item name',
  'Category',
  'Unit',
  'Quantity',
  'Reorder level',
  'Unit cost',
  'Location',
] as const;

export const TEMPLATE_EXAMPLE_ROW = [
  'Dairy meal',
  'Feed',
  'kg',
  '250',
  '50',
  '12.50',
  'Store room',
];
