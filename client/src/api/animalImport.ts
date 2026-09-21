import axios from './axios';
import { farmUrl } from './farmApi';

/** One column the importer understands, in the order the mapping table shows them. */
export interface AnimalImportFieldDescriptor {
  key: string;
  label: string;
  required: boolean;
  hint: string;
}

/** Where a canonical field's value comes from: a column, or a value for every row. */
export interface AnimalImportFieldMap {
  column?: number | null;
  constant?: string | null;
}

export interface AnimalImportMapping {
  fields: Record<string, AnimalImportFieldMap>;
  dateFormat?: string | null;
}

export interface AnimalImportRowError {
  field: string;
  message: string;
}

export interface AnimalImportRow {
  rowNumber: number;
  isValid: boolean;
  errors: AnimalImportRowError[];
  values: Record<string, string>;
}

export interface AnimalImportPreview {
  fields: AnimalImportFieldDescriptor[];
  headers: string[];
  mapping: AnimalImportMapping;
  suggestedMapping: AnimalImportMapping;
  lookups: Record<string, string[]>;
  totalRows: number;
  validRowCount: number;
  invalidRowCount: number;
  truncated: boolean;
  invalidRows: AnimalImportRow[];
  sampleValidRows: AnimalImportRow[];
}

export interface AnimalImportCommit {
  totalRows: number;
  importedCount: number;
  truncated: boolean;
  invalidRows: AnimalImportRow[];
}

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

const buildForm = (file: File, mapping?: AnimalImportMapping): FormData => {
  const data = new FormData();
  data.append('file', file);
  if (mapping) data.append('mapping', JSON.stringify(mapping));
  return data;
};

// The explicit multipart content type matters: the shared axios instance defaults to
// application/json, which would serialise a FormData body instead of sending it.
const MULTIPART = { headers: { 'Content-Type': 'multipart/form-data' } } as const;

export const animalImportApi = {
  /** Validates the file and writes nothing. Called with no mapping, it answers with the headers and its own guess. */
  preview: (file: File, mapping?: AnimalImportMapping) =>
    axios.post<AnimalImportPreview>(
      farmUrl('/animals/import/preview'),
      buildForm(file, mapping),
      MULTIPART,
    ),

  /** Imports the file. All-or-nothing: any invalid row means nothing is written. */
  commit: (file: File, mapping?: AnimalImportMapping) =>
    axios.post<AnimalImportCommit>(
      farmUrl('/animals/import/commit'),
      buildForm(file, mapping),
      MULTIPART,
    ),
};
