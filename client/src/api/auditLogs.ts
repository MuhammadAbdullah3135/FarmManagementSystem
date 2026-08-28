import api from './axios';

export interface AuditLogEntry {
  id: string;
  farmId: string | null;
  userEmail: string | null;
  entityType: string;
  entityId: string;
  action: 'Create' | 'Update' | 'Delete';
  actionDisplay: string;
  oldValues: string | null;
  newValues: string | null;
  timestamp: string;
  ipAddress: string | null;
}

export interface AuditLogFilter {
  entityType?: string;
  userId?: string;
  action?: string;
  entityId?: string;
  fromDate?: string;
  toDate?: string;
  search?: string;
  page?: number;
  pageSize?: number;
}

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

export const auditLogsApi = {
  getLogs: (farmId: string, params: AuditLogFilter = {}) =>
    api.get<PagedResult<AuditLogEntry>>(`/farm/${farmId}/audit-logs`, { params }),
};
