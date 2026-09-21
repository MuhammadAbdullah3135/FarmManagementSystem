import axios from './axios';
import { farmUrl } from './farmApi';

/**
 * The bulk-import contract, shared by every entity the app can import.
 *
 * These shapes are the server's `ImportPreviewDto` / `ImportCommitDto` — one contract,
 * not one per entity, because the pipeline they describe is entity-agnostic: the file's
 * headers and the mapping table come back from the server rather than being hard-coded
 * here. That is what lets a single wizard serve animals, employees and inventory.
 */

/** One column the importer understands, in the order the mapping table shows them. */
export interface ImportFieldDescriptor {
  key: string;
  label: string;
  required: boolean;
  hint: string;
}

/** Where a canonical field's value comes from: a column, or a value for every row. */
export interface ImportFieldMap {
  column?: number | null;
  constant?: string | null;
}

export interface ImportMapping {
  fields: Record<string, ImportFieldMap>;
  dateFormat?: string | null;
}

export interface ImportRowError {
  field: string;
  message: string;
}

export interface ImportRow {
  rowNumber: number;
  isValid: boolean;
  errors: ImportRowError[];
  values: Record<string, string>;
}

export interface ImportPreview {
  fields: ImportFieldDescriptor[];
  headers: string[];
  mapping: ImportMapping;
  suggestedMapping: ImportMapping;
  lookups: Record<string, string[]>;
  totalRows: number;
  validRowCount: number;
  invalidRowCount: number;
  truncated: boolean;
  invalidRows: ImportRow[];
  sampleValidRows: ImportRow[];
}

export interface ImportCommit {
  totalRows: number;
  importedCount: number;
  truncated: boolean;
  invalidRows: ImportRow[];
}

/** The two calls every importer offers, whatever the entity. */
export interface ImportApi {
  /** Validates the file and writes nothing. Called with no mapping, it answers with the headers and its own guess. */
  preview: (file: File, mapping?: ImportMapping) => Promise<{ data: ImportPreview }>;
  /** Imports the file. All-or-nothing: any invalid row means nothing is written. */
  commit: (file: File, mapping?: ImportMapping) => Promise<{ data: ImportCommit }>;
}

const buildForm = (file: File, mapping?: ImportMapping): FormData => {
  const data = new FormData();
  data.append('file', file);
  if (mapping) data.append('mapping', JSON.stringify(mapping));
  return data;
};

// The explicit multipart content type matters: the shared axios instance defaults to
// application/json, which would serialise a FormData body instead of sending it.
const MULTIPART = { headers: { 'Content-Type': 'multipart/form-data' } } as const;

/**
 * The import calls for one entity, addressed by its base path.
 *
 * Every importer is the same two multipart POSTs against
 * `/farm/{farmId}{basePath}/preview` and `/commit`, because the entity differences all
 * live server-side in the importer's field vocabulary and row rules — not in the wire
 * shape.
 */
export const createImportApi = (basePath: string): ImportApi => ({
  preview: (file, mapping) =>
    axios.post<ImportPreview>(farmUrl(`${basePath}/preview`), buildForm(file, mapping), MULTIPART),

  commit: (file, mapping) =>
    axios.post<ImportCommit>(farmUrl(`${basePath}/commit`), buildForm(file, mapping), MULTIPART),
});
