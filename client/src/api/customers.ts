import api from './axios';
import { farmUrl } from './farmApi';
import type { Customer, CustomerSale, PagedResult } from '../types';

export const customersApi = {
  list: (params: { search?: string; page?: number; pageSize?: number } = {}) => api.get<PagedResult<Customer>>(farmUrl('/inventory/customers'), { params }),
  get: (id: string) => api.get<Customer>(farmUrl(`/inventory/customers/${id}`)),
  create: (data: { name: string; contactInfo?: string }) => api.post<Customer>(farmUrl('/inventory/customers'), data),
  update: (id: string, data: { name: string; contactInfo?: string }) => api.put<Customer>(farmUrl(`/inventory/customers/${id}`), data),
  remove: (id: string) => api.delete(farmUrl(`/inventory/customers/${id}`)),
  sales: (params: { customerId?: string; inventoryItemId?: string; from?: string; to?: string; page?: number; pageSize?: number } = {}) => api.get<PagedResult<CustomerSale>>(farmUrl('/inventory/customer-sales'), { params }),
  createSale: (data: { customerId: string; inventoryItemId: string; quantity: number; totalAmount: number; saleDate?: string; incomeCategoryId?: string; paymentMethodId?: string; notes?: string }) => api.post<CustomerSale>(farmUrl('/inventory/customer-sales'), data),
};
