import api from './axios';
import { farmUrl } from './farmApi';
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

// Lookups used by HR/task/feed forms
export const lookupsApi = {
  animals: () =>
    api.get<PagedResult<{ id: string; tagNumber: string; name?: string }>>(
      farmUrl('/animals?page=1&pageSize=100')
    ),
  locations: () =>
    api.get<{ id: string; name: string }[]>(farmUrl('/configuration/locations')),
  animalTypes: () =>
    api.get<{ id: string; name: string }[]>(farmUrl('/configuration/animal-types')),
  ageCategories: () =>
    api.get<{ id: string; name: string }[]>(farmUrl('/configuration/age-categories')),
};
