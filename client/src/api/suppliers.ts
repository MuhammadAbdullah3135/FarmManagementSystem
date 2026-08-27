import api from './axios';
import { farmUrl } from './farmApi';
import type { PagedResult, Supplier, SupplierPurchase } from '../types';

export const suppliersApi = {
  list: (params: { search?: string; page?: number; pageSize?: number } = {}) => api.get<PagedResult<Supplier>>(farmUrl('/inventory/suppliers'), { params }),
  get: (id: string) => api.get<Supplier>(farmUrl(`/inventory/suppliers/${id}`)),
  create: (data: { name: string; contactInfo?: string; productsSupplied?: string }) => api.post<Supplier>(farmUrl('/inventory/suppliers'), data),
  update: (id: string, data: { name: string; contactInfo?: string; productsSupplied?: string }) => api.put<Supplier>(farmUrl(`/inventory/suppliers/${id}`), data),
  remove: (id: string) => api.delete(farmUrl(`/inventory/suppliers/${id}`)),
  purchases: (params: { supplierId?: string; inventoryItemId?: string; from?: string; to?: string; page?: number; pageSize?: number } = {}) => api.get<PagedResult<SupplierPurchase>>(farmUrl('/inventory/supplier-purchases'), { params }),
  createPurchase: (data: { supplierId: string; inventoryItemId: string; quantity: number; totalCost: number; purchaseDate?: string; expenseCategoryId?: string; paymentMethodId?: string; notes?: string }) => api.post<SupplierPurchase>(farmUrl('/inventory/supplier-purchases'), data),
};
