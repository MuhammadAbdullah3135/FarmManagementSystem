import axios from './axios';
import { farmUrl } from './farmApi';
import type { PagedResult } from '../types';

export interface AnimalListFilter {
  search?: string;
  animalTypeId?: string;
  breedId?: string;
  statusId?: string;
  locationId?: string;
  sexOptionId?: string;
  includeTerminal?: boolean;
  page?: number;
  pageSize?: number;
  sortBy?: string;
  sortDescending?: boolean;
}

/**
 * What the two bulk endpoints answer: how many animals were asked for, how many changed, and a
 * reason per animal the server refused. A partial answer is normal — a sold animal cannot be
 * transferred again — so the UI reports "x of y" instead of treating the call as all-or-nothing.
 */
export interface BulkOperationFailure {
  animalId: string;
  reason: string;
}

export interface BulkOperationResult {
  requestedCount: number;
  successCount: number;
  failures: BulkOperationFailure[];
}

export interface BulkStatusChangePayload {
  animalIds: string[];
  newStatusId: string;
  reason?: string;
}

export interface BulkTransferPayload {
  animalIds: string[];
  toLocationId: string;
  reason?: string;
}

export interface CreateAnimalPayload {
  tagNumber: string;
  name?: string;
  animalTypeId: string;
  breedId?: string;
  sexOptionId: string;
  ageCategoryId?: string;
  animalStatusId: string;
  locationId?: string;
  sireId?: string;
  damId?: string;
  dateOfBirth?: string;
  acquisitionDate?: string;
  notes?: string;
  identifications?: {
    identificationTypeId: string;
    value: string;
    isPrimary: boolean;
  }[];
}

/**
 * One row of an animal's weight history, as the API returns it.
 *
 * There is deliberately no `addWeight` here: recording a weight goes through the offline
 * write queue (4.5.4) even when the device is online, so there is exactly one write path and
 * an online record and an offline one cannot behave differently. The queue sends it to
 * `POST /farm/{farmId}/sync/mutations`, which applies it by calling the same service method
 * `POST …/weights` would.
 */
export interface WeightRecord {
  id: string;
  animalId: string;
  weightKg: number;
  recordedAt: string;
  changeFromPreviousKg?: number | null;
  notes?: string | null;
  createdAt: string;
}

/**
 * One physical label: the animal it names, the text a person reads off it, and the payload
 * that goes into the code itself.
 *
 * The payload is built by the server (`AnimalQrCode`), never assembled here. That is what
 * makes "the code printed on the tag" and "the code that resolves back to this animal" one
 * definition instead of two that agree until one of them changes — and it is why the label
 * sheet is a server read rather than a loop over the rows already on screen.
 */
export interface AnimalQrLabel {
  animalId: string;
  tagNumber: string;
  name?: string | null;
  animalTypeName?: string | null;
  url: string;
}

export const animalsApi = {
  list: (params: AnimalListFilter = {}) =>
    axios.get<PagedResult<unknown>>(farmUrl('/animals'), { params }),

  /**
   * A page of labels for a sheet, through the same filter, ordering and paging the animal
   * list answers — so the sheet prints the rows the user was looking at.
   */
  qrLabels: (params: AnimalListFilter = {}) =>
    axios.get<PagedResult<AnimalQrLabel>>(farmUrl('/animals/qr-labels'), { params }),

  /** One animal's label, for the code shown on its own page. */
  qrLabel: (id: string) => axios.get<AnimalQrLabel>(farmUrl(`/animals/${id}/qr`)),

  get: (id: string) =>
    axios.get<unknown>(farmUrl(`/animals/${id}`)),

  create: (data: CreateAnimalPayload) =>
    axios.post<unknown>(farmUrl('/animals'), data),

  update: (id: string, data: CreateAnimalPayload) =>
    axios.put<unknown>(farmUrl(`/animals/${id}`), data),

  delete: (id: string) =>
    axios.delete(farmUrl(`/animals/${id}`)),

  changeStatus: (id: string, data: { newStatusId: string; reason?: string }) =>
    axios.put<unknown>(farmUrl(`/animals/${id}/status`), data),

  /** One status change for every ticked animal, applied server-side per animal. */
  bulkChangeStatus: (data: BulkStatusChangePayload) =>
    axios.post<BulkOperationResult>(farmUrl('/animals/bulk/status'), data),

  /** One move for every ticked animal, with the same per-animal refusal reporting. */
  bulkTransfer: (data: BulkTransferPayload) =>
    axios.post<BulkOperationResult>(farmUrl('/animals/bulk/transfer'), data),

  getTimeline: (id: string, page = 1, pageSize = 20, eventType?: string) => {
    const params: Record<string, string | number> = { page, pageSize };
    if (eventType) params.eventType = eventType;
    return axios.get<unknown>(farmUrl(`/animals/${id}/timeline`), { params });
  },

  getWeights: (id: string, page = 1, pageSize = 20) =>
    axios.get<PagedResult<WeightRecord>>(farmUrl(`/animals/${id}/weights`), {
      params: { page, pageSize },
    }),
};
