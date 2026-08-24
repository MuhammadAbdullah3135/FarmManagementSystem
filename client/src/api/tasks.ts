import api from './axios';
import { farmUrl } from './farmApi';
import type { FarmTask, PagedResult } from '../types';

export interface FarmTaskListFilter {
  search?: string;
  status?: string;
  priority?: string;
  assignedEmployeeId?: string;
  includeOverdueOnly?: boolean;
  page?: number;
  pageSize?: number;
}

export const tasksApi = {
  list: (filter: FarmTaskListFilter) =>
    api.get<PagedResult<FarmTask>>(farmUrl('/tasks'), { params: filter }),
  get: (id: string) => api.get<FarmTask>(farmUrl(`/tasks/${id}`)),
  create: (data: {
    title: string;
    description?: string;
    priority: string;
    dueDate: string;
    assignedEmployeeId?: string;
    animalId?: string;
    locationId?: string;
  }) => api.post<FarmTask>(farmUrl('/tasks'), data),
  update: (
    id: string,
    data: {
      title: string;
      description?: string;
      priority: string;
      dueDate: string;
      assignedEmployeeId?: string;
      animalId?: string;
      locationId?: string;
    }
  ) => api.put<FarmTask>(farmUrl(`/tasks/${id}`), data),
  remove: (id: string) => api.delete(farmUrl(`/tasks/${id}`)),
  start: (id: string) => api.post<FarmTask>(farmUrl(`/tasks/${id}/start`)),
  complete: (id: string, completionNotes?: string) =>
    api.post<FarmTask>(farmUrl(`/tasks/${id}/complete`), { completionNotes }),
  cancel: (id: string, reason?: string) =>
    api.post<FarmTask>(farmUrl(`/tasks/${id}/cancel`), { reason }),
  reopen: (id: string) => api.post<FarmTask>(farmUrl(`/tasks/${id}/reopen`)),
};
