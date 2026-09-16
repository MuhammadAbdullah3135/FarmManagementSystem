import api from './axios';
import { farmUrl } from './farmApi';

// ─── Types ────────────────────────────────────────────────────────────────

export interface AnimalType {
  id: string;
  name: string;
  breeds: Breed[];
}

export interface Breed {
  id: string;
  name: string;
  animalTypeId: string;
  averageGestationDays: number;
}

export interface SexOption {
  id: string;
  value: string;
}

export interface AgeCategory {
  id: string;
  name: string;
  minDays: number;
  maxDays: number;
}

export type AnimalStatusCategory = 'Active' | 'Inactive' | 'Terminal';

export interface AnimalStatus {
  id: string;
  name: string;
  isActive: boolean;
  category: AnimalStatusCategory | number;
  isSystemDefined: boolean;
}

export interface LocationType {
  id: string;
  name: string;
}

export interface Location {
  id: string;
  name: string;
  locationTypeId: string;
  locationTypeName?: string;
  parentLocationId?: string | null;
  parentLocationName?: string | null;
  childLocations: Location[];
}

// ─── Endpoints (ConfigurationController) ──────────────────────────────────

export const configurationApi = {
  // Animal types
  animalTypes: () => api.get<AnimalType[]>(farmUrl('/configuration/animal-types')),
  createAnimalType: (data: { name: string }) =>
    api.post(farmUrl('/configuration/animal-types'), data),
  deleteAnimalType: (id: string) => api.delete(farmUrl(`/configuration/animal-types/${id}`)),

  // Breeds
  breeds: (animalTypeId?: string) => {
    const params: Record<string, string> = {};
    if (animalTypeId) params.animalTypeId = animalTypeId;
    return api.get<Breed[]>(farmUrl('/configuration/breeds'), { params });
  },
  createBreed: (data: { name: string; animalTypeId: string; averageGestationDays: number }) =>
    api.post(farmUrl('/configuration/breeds'), data),
  deleteBreed: (id: string) => api.delete(farmUrl(`/configuration/breeds/${id}`)),

  // Sex options
  sexOptions: () => api.get<SexOption[]>(farmUrl('/configuration/sex-options')),
  createSexOption: (data: { value: string }) =>
    api.post(farmUrl('/configuration/sex-options'), data),
  deleteSexOption: (id: string) => api.delete(farmUrl(`/configuration/sex-options/${id}`)),

  // Age categories
  ageCategories: () => api.get<AgeCategory[]>(farmUrl('/configuration/age-categories')),
  createAgeCategory: (data: { name: string; minDays: number; maxDays: number }) =>
    api.post(farmUrl('/configuration/age-categories'), data),
  deleteAgeCategory: (id: string) => api.delete(farmUrl(`/configuration/age-categories/${id}`)),

  // Animal statuses
  statuses: () =>
    api.get<AnimalStatus[]>(farmUrl('/configuration/statuses')),
  createStatus: (data: { name: string; isActive: boolean; category: number }) =>
    api.post(farmUrl('/configuration/statuses'), data),
  deleteStatus: (id: string) => api.delete(farmUrl(`/configuration/statuses/${id}`)),

  // Location types
  locationTypes: () => api.get<LocationType[]>(farmUrl('/configuration/location-types')),
  createLocationType: (data: { name: string }) =>
    api.post(farmUrl('/configuration/location-types'), data),
  deleteLocationType: (id: string) => api.delete(farmUrl(`/configuration/location-types/${id}`)),

  // Locations (tree)
  locations: () => api.get<Location[]>(farmUrl('/configuration/locations')),
  createLocation: (data: { name: string; locationTypeId: string; parentLocationId?: string }) =>
    api.post(farmUrl('/configuration/locations'), data),
  deleteLocation: (id: string) => api.delete(farmUrl(`/configuration/locations/${id}`)),
};

/** Flatten the nested location tree into a flat list (for select options). */
export const flattenLocations = (nodes: Location[], depth = 0): { id: string; name: string; depth: number }[] =>
  nodes.flatMap((n) => [
    { id: n.id, name: n.name, depth },
    ...flattenLocations(n.childLocations ?? [], depth + 1),
  ]);
