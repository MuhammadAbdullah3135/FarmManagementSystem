import api from './axios';
import { farmUrl } from './farmApi';
import type { InventoryItem, InventoryReport, PagedResult } from '../types';

export interface InventoryItemInput {
  name: string;
  category?: string;
  unit: string;
  quantity?: number;
  reorderLevel: number;
  unitCost: number;
  location?: string;
}

export const inventoryApi = {
  list: (params: { page?: number; pageSize?: number; search?: string } = {}) =>
    api.get<PagedResult<InventoryItem>>(farmUrl('/inventory-items'), { params }),
  get: (id: string) => api.get<InventoryItem>(farmUrl(`/inventory-items/${id}`)),
  create: (data: InventoryItemInput) => api.post<InventoryItem>(farmUrl('/inventory-items'), data),
  update: (id: string, data: Omit<InventoryItemInput, 'quantity'>) =>
    api.put<InventoryItem>(farmUrl(`/inventory-items/${id}`), data),
  remove: (id: string) => api.delete(farmUrl(`/inventory-items/${id}`)),
  movements: (params: { inventoryItemId?: string; movementType?: string; from?: string; to?: string; page?: number; pageSize?: number } = {}) =>
    api.get<PagedResult<import('../types').StockMovement>>(farmUrl('/inventory-items/movements'), { params }),
  recordMovement: (inventoryItemId: string, data: { movementType: string; quantity: number; movementDate?: string; reason?: string }) =>
    api.post<import('../types').StockMovement>(farmUrl('/inventory-items/movements'), { inventoryItemId, ...data }),
  report: () => api.get<InventoryReport>(farmUrl('/inventory-items/reports')),
};
