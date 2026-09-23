import api from './axios';
import { farmUrl } from './farmApi';
import type { DeltaResult, FarmTask } from '../types';

export interface FarmTaskListFilter {
  search?: string;
  status?: string;
  priority?: string;
  assignedEmployeeId?: string;
  includeOverdueOnly?: boolean;
  /**
   * The server's cursor from the last read of this collection. Present means "only what changed
   * since then, plus the ids of tasks deleted since" (Phase 5.6).
   */
  updatedSince?: string | null;
  page?: number;
  pageSize?: number;
}

export const tasksApi = {
  list: (filter: FarmTaskListFilter) =>
    api.get<DeltaResult<FarmTask>>(farmUrl('/tasks'), { params: filter }),
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
