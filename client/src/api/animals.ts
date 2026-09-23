import axios from './axios';
import { farmUrl } from './farmApi';
import type { PagedResult } from '../types';

export interface AnimalListFilter {
  search?: string;
  animalTypeId?: string;
  breedId?: string;
  statusId?: string;
  locationId?: string;
  includeTerminal?: boolean;
  page?: number;
  pageSize?: number;
  sortBy?: string;
  sortDescending?: boolean;
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

export const animalsApi = {
  list: (params: AnimalListFilter = {}) =>
    axios.get<PagedResult<unknown>>(farmUrl('/animals'), { params }),

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
