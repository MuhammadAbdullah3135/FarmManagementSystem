import axios from './axios';
import { farmUrl } from './farmApi';
import type { PagedResult, BreedingRecord, GestationRecord, GestationHealthCheck, BirthRecord, LineageResponse, BreedingSummaryReport, BreedingTrendEntry, MethodDistributionEntry, SirePerformanceEntry, CalendarEventEntry } from '../types';

export interface BreedingRecordListFilter {
  sireId?: string;
  damId?: string;
  result?: number;
  fromDate?: string;
  toDate?: string;
  page?: number;
  pageSize?: number;
}

export interface CreateBreedingRecordPayload {
  sireId: string;
  damId: string;
  breedingDate: string;
  method: number;
  vetName?: string;
  notes?: string;
}

export interface UpdateBreedingRecordPayload extends CreateBreedingRecordPayload {
  result: number;
}

export interface GestationRecordListFilter {
  stage?: number;
  activeOnly?: boolean;
  page?: number;
  pageSize?: number;
}

export interface ConfirmPregnancyPayload {
  breedingRecordId: string;
  confirmedDate: string;
}

export interface LogHealthCheckPayload {
  checkDate: string;
  notes?: string;
  performedBy?: string;
  weightKg?: number;
}

export const breedingRecordsApi = {
  list: (params: BreedingRecordListFilter = {}) =>
    axios.get<PagedResult<BreedingRecord>>(farmUrl('/breeding-records'), { params }),

  get: (id: string) =>
    axios.get<BreedingRecord>(farmUrl(`/breeding-records/${id}`)),

  create: (data: CreateBreedingRecordPayload) =>
    axios.post<BreedingRecord>(farmUrl('/breeding-records'), data),

  update: (id: string, data: UpdateBreedingRecordPayload) =>
    axios.put<BreedingRecord>(farmUrl(`/breeding-records/${id}`), data),

  delete: (id: string) =>
    axios.delete(farmUrl(`/breeding-records/${id}`)),
};

export const gestationApi = {
  list: (params: GestationRecordListFilter = {}) =>
    axios.get<PagedResult<GestationRecord>>(farmUrl('/gestation'), { params }),

  get: (id: string) =>
    axios.get<GestationRecord>(farmUrl(`/gestation/${id}`)),

  confirm: (data: ConfirmPregnancyPayload) =>
    axios.post<GestationRecord>(farmUrl('/gestation/confirm'), data),

  revert: (id: string, reason: string) =>
    axios.post(farmUrl(`/gestation/${id}/revert`), { reason }),

  getHealthChecks: (id: string) =>
    axios.get<GestationHealthCheck[]>(farmUrl(`/gestation/${id}/health-checks`)),

  logHealthCheck: (id: string, data: LogHealthCheckPayload) =>
    axios.post<GestationHealthCheck>(farmUrl(`/gestation/${id}/health-checks`), data),
};

export interface BirthRecordListFilter {
  damId?: string;
  fromDate?: string;
  toDate?: string;
  page?: number;
  pageSize?: number;
}

export interface CreateBirthOffspringPayload {
  sexOptionId: string;
  outcome: number;
  birthWeightKg?: number;
  name?: string;
  notes?: string;
}

export interface CreateBirthRecordPayload {
  damId: string;
  gestationRecordId?: string;
  breedingRecordId?: string;
  birthDate: string;
  vetName?: string;
  notes?: string;
  offspring: CreateBirthOffspringPayload[];
}

export const birthsApi = {
  list: (params: BirthRecordListFilter = {}) =>
    axios.get<PagedResult<BirthRecord>>(farmUrl('/births'), { params }),

  get: (id: string) =>
    axios.get<BirthRecord>(farmUrl(`/births/${id}`)),

  create: (data: CreateBirthRecordPayload) =>
    axios.post<BirthRecord>(farmUrl('/births'), data),

  delete: (id: string) =>
    axios.delete(farmUrl(`/births/${id}`)),
};

export const lineageApi = {
  get: (animalId: string, ancestorDepth = 5, descendantDepth = 3) =>
    axios.get<LineageResponse>(farmUrl(`/lineage/${animalId}`), {
      params: { ancestorDepth, descendantDepth },
    }),
};

export interface BreedingReportFilter {
  fromDate?: string;
  toDate?: string;
}

export const breedingReportsApi = {
  summary: (filter: BreedingReportFilter = {}) =>
    axios.get<BreedingSummaryReport>(farmUrl('/breeding/reports/summary'), { params: filter }),

  trend: (filter: BreedingReportFilter = {}) =>
    axios.get<BreedingTrendEntry[]>(farmUrl('/breeding/reports/trend'), { params: filter }),

  methods: (filter: BreedingReportFilter = {}) =>
    axios.get<MethodDistributionEntry[]>(farmUrl('/breeding/reports/methods'), { params: filter }),

  sires: (filter: BreedingReportFilter = {}) =>
    axios.get<SirePerformanceEntry[]>(farmUrl('/breeding/reports/sires'), { params: filter }),

  calendar: (daysAhead = 30) =>
    axios.get<CalendarEventEntry[]>(farmUrl('/breeding/reports/calendar'), { params: { daysAhead } }),
};
