import api from './axios';
import { getActiveFarmId } from './farmApi';

/** The only field names the server's suggestions endpoint accepts. */
export type SuggestionField =
  | 'medicalDiagnosis'
  | 'medicalDosage'
  | 'supplierProductsSupplied';

export const suggestionsApi = {
  list: (field: SuggestionField) =>
    api.get<string[]>(`/farms/${getActiveFarmId()}/suggestions/${field}`),
};
