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
    axios.get<unknown>(farmUrl(`/animals/${id}/weights`), { params: { page, pageSize } }),
};
