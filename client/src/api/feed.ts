import api from './axios';
import { farmUrl } from './farmApi';
import type {
  FeedType,
  FeedStock,
  FeedStockMovement,
  FeedRecord,
  DietPlan,
  FeedingSchedule,
  FeedingTask,
  ConsumptionTrendPoint,
  FeedTypeBreakdown,
  AnimalConsumption,
  LocationConsumption,
  FeedCostSummary,
  PagedResult,
} from '../types';

export const feedTypesApi = {
  list: () => api.get<FeedType[]>(farmUrl('/feed/types')),
  create: (data: { name: string; category: string; unit: string; costPerUnit: number; notes?: string }) =>
    api.post<FeedType>(farmUrl('/feed/types'), data),
  update: (id: string, data: { name: string; category: string; unit: string; costPerUnit: number; notes?: string }) =>
    api.put<FeedType>(farmUrl(`/feed/types/${id}`), data),
  remove: (id: string) => api.delete(farmUrl(`/feed/types/${id}`)),
};

export const feedInventoryApi = {
  stock: () => api.get<FeedStock[]>(farmUrl('/feed/inventory/stock')),
  movements: (feedTypeId?: string, page = 1, pageSize = 20) =>
    api.get<PagedResult<FeedStockMovement>>(farmUrl('/feed/inventory/movements'), {
      params: { feedTypeId, page, pageSize },
    }),
  recordMovement: (data: {
    feedTypeId: string;
    movementType: string;
    quantity: number;
    unitCost?: number;
    supplier?: string;
    notes?: string;
    movementDate?: string;
  }) => api.post(farmUrl('/feed/inventory/movements'), data),
};

export const feedRecordsApi = {
  list: (params: {
    feedTypeId?: string;
    animalId?: string;
    locationId?: string;
    from?: string;
    to?: string;
    page?: number;
    pageSize?: number;
  }) => api.get<PagedResult<FeedRecord>>(farmUrl('/feed/records'), { params }),
  create: (data: {
    feedTypeId: string;
    animalId?: string;
    locationId?: string;
    quantity: number;
    fedAt?: string;
    notes?: string;
  }) => api.post(farmUrl('/feed/records'), data),
  update: (
    id: string,
    data: { quantity: number; fedAt?: string; notes?: string }
  ) => api.put(farmUrl(`/feed/records/${id}`), data),
  remove: (id: string) => api.delete(farmUrl(`/feed/records/${id}`)),
};

export const dietPlansApi = {
  list: () => api.get<DietPlan[]>(farmUrl('/feed/diet-plans')),
  get: (id: string) => api.get<DietPlan>(farmUrl(`/feed/diet-plans/${id}`)),
  create: (data: {
    name: string;
    animalTypeId?: string;
    breedId?: string;
    ageCategoryId?: string;
    minWeightKg?: number;
    maxWeightKg?: number;
    notes?: string;
    items?: { feedTypeId: string; quantityPerFeeding: number }[];
  }) => api.post(farmUrl('/feed/diet-plans'), data),
  update: (
    id: string,
    data: {
      name: string;
      animalTypeId?: string;
      breedId?: string;
      ageCategoryId?: string;
      minWeightKg?: number;
      maxWeightKg?: number;
      isActive: boolean;
      notes?: string;
    }
  ) => api.put(farmUrl(`/feed/diet-plans/${id}`), data),
  remove: (id: string) => api.delete(farmUrl(`/feed/diet-plans/${id}`)),
  addItem: (id: string, item: { feedTypeId: string; quantityPerFeeding: number }) =>
    api.post(farmUrl(`/feed/diet-plans/${id}/items`), item),
  removeItem: (id: string, itemId: string) =>
    api.delete(farmUrl(`/feed/diet-plans/${id}/items/${itemId}`)),
};

export const feedingSchedulesApi = {
  list: () => api.get<FeedingSchedule[]>(farmUrl('/feed/schedules')),
  create: (data: { dietPlanId: string; timeOfDay: string; label?: string }) =>
    api.post(farmUrl('/feed/schedules'), data),
  update: (id: string, data: { timeOfDay?: string; label?: string; isActive?: boolean }) =>
    api.put(farmUrl(`/feed/schedules/${id}`), data),
  remove: (id: string) => api.delete(farmUrl(`/feed/schedules/${id}`)),
};

export const feedingTasksApi = {
  generate: (date: string) => api.post(farmUrl('/feed/tasks/generate'), { date }),
  list: (params: { date?: string; dietPlanId?: string; status?: string; page?: number; pageSize?: number }) =>
    api.get<PagedResult<FeedingTask>>(farmUrl('/feed/tasks'), { params }),
  complete: (id: string, notes?: string) =>
    api.post(farmUrl(`/feed/tasks/${id}/complete`), { notes }),
  skip: (id: string, notes?: string) => api.post(farmUrl(`/feed/tasks/${id}/skip`), { notes }),
};

export const feedReportsApi = {
  consumptionTrend: (period: 'day' | 'week' | 'month', from?: string, to?: string) =>
    api.get<ConsumptionTrendPoint[]>(farmUrl('/feed/reports/consumption/trend'), {
      params: { period, from, to },
    }),
  byFeedType: (from?: string, to?: string) =>
    api.get<FeedTypeBreakdown[]>(farmUrl('/feed/reports/consumption/by-feed-type'), {
      params: { from, to },
    }),
  byAnimal: (from?: string, to?: string) =>
    api.get<AnimalConsumption[]>(farmUrl('/feed/reports/consumption/by-animal'), {
      params: { from, to },
    }),
  byLocation: (from?: string, to?: string) =>
    api.get<LocationConsumption[]>(farmUrl('/feed/reports/consumption/by-location'), {
      params: { from, to },
    }),
  costSummary: (from?: string, to?: string) =>
    api.get<FeedCostSummary>(farmUrl('/feed/reports/cost-summary'), { params: { from, to } }),
};
