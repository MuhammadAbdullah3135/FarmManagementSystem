import { createImportApi } from './importApi';
import type {
  ImportCommit,
  ImportFieldDescriptor,
  ImportFieldMap,
  ImportMapping,
  ImportPreview,
  ImportRow,
  ImportRowError,
} from './importApi';

/**
 * The animal importer, on the shared import contract.
 *
 * The wire types and the two calls are the generic ones — the animal-specific part is
 * only the endpoint's base path and the template file's headers. The `Animal*` aliases
 * below are kept so callers (and their tests) that name the animal import explicitly
 * keep compiling against the same shapes they always did.
 */
export type AnimalImportFieldDescriptor = ImportFieldDescriptor;
export type AnimalImportFieldMap = ImportFieldMap;
export type AnimalImportMapping = ImportMapping;
export type AnimalImportRowError = ImportRowError;
export type AnimalImportRow = ImportRow;
export type AnimalImportPreview = ImportPreview;
export type AnimalImportCommit = ImportCommit;

/**
 * The headers written into the downloadable template.
 *
 * Mirrors FMS.Application.Animal.Import.AnimalImportFields.All. The server's list is
 * authoritative for mapping — the wizard renders whatever /preview returns — and every
 * header here is matched by the server's auto-detection, so this copy only decides the
 * wording of the empty template file.
 */
export const TEMPLATE_HEADERS = [
  'Tag number',
  'Name',
  'Animal type',
  'Breed',
  'Sex',
  'Age category',
  'Status',
  'Location',
  'Date of birth',
  'Acquisition date',
  'Sire tag',
  'Dam tag',
  'Notes',
] as const;

export const TEMPLATE_EXAMPLE_ROW = [
  'TAG-0001',
  'Bella',
  'Cattle',
  'Holstein',
  'Female',
  'Adult',
  'Active',
  'Shed A',
  '2023-04-15',
  '2023-05-01',
  '',
  '',
  'Imported from the herd register',
];

export const animalImportApi = createImportApi('/animals/import');
