import api from './axios';
import { farmUrl } from './farmApi';
import { configurationApi } from './configuration';
import type { AttendanceRecord, PerformanceReview, PagedResult } from '../types';

export const attendanceApi = {
  list: (params: {
    employeeId?: string;
    status?: string;
    from?: string;
    to?: string;
    page?: number;
    pageSize?: number;
  }) => api.get<PagedResult<AttendanceRecord>>(farmUrl('/attendance'), { params }),
  checkIn: (employeeId: string) =>
    api.post<AttendanceRecord>(farmUrl(`/attendance/${employeeId}/check-in`)),
  checkOut: (employeeId: string) =>
    api.post<AttendanceRecord>(farmUrl(`/attendance/${employeeId}/check-out`)),
  upsert: (data: {
    employeeId: string;
    date: string;
    status: string;
    checkInAt?: string;
    checkOutAt?: string;
    notes?: string;
  }) => api.put<AttendanceRecord>(farmUrl('/attendance'), data),
  remove: (id: string) => api.delete(farmUrl(`/attendance/${id}`)),
};

export const performanceReviewsApi = {
  list: (params: {
    employeeId?: string;
    minRating?: number;
    maxRating?: number;
    from?: string;
    to?: string;
    page?: number;
    pageSize?: number;
  }) => api.get<PagedResult<PerformanceReview>>(farmUrl('/performance-reviews'), { params }),
  create: (data: {
    employeeId: string;
    rating: number;
    reviewDate: string;
    periodStart?: string;
    periodEnd?: string;
    strengths?: string;
    areasForImprovement?: string;
    comments?: string;
  }) => api.post<PerformanceReview>(farmUrl('/performance-reviews'), data),
  update: (
    id: string,
    data: {
      rating: number;
      reviewDate: string;
      periodStart?: string;
      periodEnd?: string;
      strengths?: string;
      areasForImprovement?: string;
      comments?: string;
    }
  ) => api.put<PerformanceReview>(farmUrl(`/performance-reviews/${id}`), data),
  remove: (id: string) => api.delete(farmUrl(`/performance-reviews/${id}`)),
};

// Lookups used by HR/task/feed forms.
// GET wrappers are shared with the Configuration page and live in ./configuration;
// they are re-exported here so existing call sites keep working unchanged.
/**
 * The farm's animals as a form lookup: a bounded first page of 100, not the full list.
 *
 * Named rather than inline because 4.5.4 caches this exact call as the device's animal
 * lookup, and the offline weight-recording picker and the queue screens both resolve an
 * animal's label through it.
 */
export interface AnimalLookupRow {
  id: string;
  tagNumber: string;
  name?: string;
}

export const lookupsApi = {
  ...configurationApi,
  animals: () =>
    api.get<PagedResult<AnimalLookupRow>>(
      farmUrl('/animals?page=1&pageSize=100')
    ),
};
