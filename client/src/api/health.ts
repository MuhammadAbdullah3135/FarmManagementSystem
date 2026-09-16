import api from './axios';
import { farmUrl } from './farmApi';
import type { MedicalRecord, MedicalRecordListItem, Medicine, MedicineListItem, MedicineStock, MedicineAlert, VaccineType, VaccineTypeListItem, VaccinationRecord, VaccinationRecordListItem, VaccinationSchedule, WeightCheckSchedule, WeightCheckStatus, HealthCostSummary, HealthCostByVet, HealthCostByAnimal, HealthCostByMonth, PagedResult } from '../types';
import type { VaccinationStatus } from '../types';

export interface MedicalRecordListFilter {
  search?: string;
  animalId?: string;
  status?: string;
  fromDate?: string;
  toDate?: string;
  page?: number;
  pageSize?: number;
}

export const medicalRecordsApi = {
  list: (filter: MedicalRecordListFilter = {}) =>
    api.get<PagedResult<MedicalRecordListItem>>(farmUrl('/medical-records'), { params: filter }),
  get: (id: string) =>
    api.get<MedicalRecord>(farmUrl(`/medical-records/${id}`)),
  listByAnimal: (animalId: string, page = 1, pageSize = 20) =>
    api.get<PagedResult<MedicalRecordListItem>>(farmUrl(`/medical-records/animal/${animalId}`), { params: { page, pageSize } }),
  create: (data: {
    animalId: string;
    symptoms: string;
    diagnosis?: string;
    treatment?: string;
    medicineUsed?: string;
    dosage?: string;
    vetName?: string;
    cost?: number;
    dateRecorded: string;
    followUpDate?: string;
    status?: string;
    notes?: string;
  }) => api.post<MedicalRecord>(farmUrl('/medical-records'), data),
  update: (id: string, data: {
    animalId: string;
    symptoms: string;
    diagnosis?: string;
    treatment?: string;
    medicineUsed?: string;
    dosage?: string;
    vetName?: string;
    cost?: number;
    dateRecorded: string;
    followUpDate?: string;
    status?: string;
    notes?: string;
  }) => api.put<MedicalRecord>(farmUrl(`/medical-records/${id}`), data),
  remove: (id: string) => api.delete(farmUrl(`/medical-records/${id}`)),
};

// ── Medicine Inventory ──────────────────────────────────

export const medicinesApi = {
  list: (params: { page?: number; pageSize?: number; search?: string } = {}) =>
    api.get<PagedResult<MedicineListItem>>(farmUrl('/medicines'), { params }),
  get: (id: string) =>
    api.get<Medicine>(farmUrl(`/medicines/${id}`)),
  create: (data: { name: string; description?: string; unit: string; lowStockThreshold?: number; expiringSoonDays?: number }) =>
    api.post<Medicine>(farmUrl('/medicines'), data),
  update: (id: string, data: { name: string; description?: string; unit: string; lowStockThreshold?: number; expiringSoonDays?: number }) =>
    api.put<Medicine>(farmUrl(`/medicines/${id}`), data),
  remove: (id: string) => api.delete(farmUrl(`/medicines/${id}`)),

  // Stock batches
  getStock: (medicineId: string) =>
    api.get<MedicineStock[]>(farmUrl(`/medicines/${medicineId}/stock`)),
  addStock: (medicineId: string, data: {
    batchNumber: string;
    quantity: number;
    unitCost: number;
    expiryDate: string;
    supplier?: string;
    dateReceived?: string;
  }) => api.post<MedicineStock>(farmUrl(`/medicines/${medicineId}/stock`), data),
  deleteStock: (medicineId: string, stockId: string) =>
    api.delete(farmUrl(`/medicines/${medicineId}/stock/${stockId}`)),

  // Usage
  recordUsage: (data: {
    medicineId: string;
    medicalRecordId?: string;
    quantityUsed: number;
    dateUsed?: string;
    notes?: string;
  }) => api.post(farmUrl('/medicines/usage'), data),

  // Alerts
  alerts: () =>
    api.get<MedicineAlert[]>(farmUrl('/medicines/alerts')),
};

// ── Vaccines ────────────────────────────────────────────

export interface VaccinationRecordListFilter {
  search?: string;
  animalId?: string;
  vaccineTypeId?: string;
  fromDate?: string;
  toDate?: string;
  page?: number;
  pageSize?: number;
}

export const vaccineTypesApi = {
  list: (params: { page?: number; pageSize?: number; search?: string } = {}) =>
    api.get<PagedResult<VaccineTypeListItem>>(farmUrl('/vaccines'), { params }),
  get: (id: string) =>
    api.get<VaccineType>(farmUrl(`/vaccines/${id}`)),
  create: (data: { name: string; defaultDosage?: string; notes?: string; linkedMedicineId?: string }) =>
    api.post<VaccineType>(farmUrl('/vaccines'), data),
  update: (id: string, data: { name: string; defaultDosage?: string; notes?: string; linkedMedicineId?: string }) =>
    api.put<VaccineType>(farmUrl(`/vaccines/${id}`), data),
  remove: (id: string) => api.delete(farmUrl(`/vaccines/${id}`)),
};

export const vaccinationRecordsApi = {
  list: (filter: VaccinationRecordListFilter = {}) =>
    api.get<PagedResult<VaccinationRecordListItem>>(farmUrl('/vaccinations'), { params: filter }),
  get: (id: string) =>
    api.get<VaccinationRecord>(farmUrl(`/vaccinations/${id}`)),
  listByAnimal: (animalId: string, page = 1, pageSize = 20) =>
    api.get<PagedResult<VaccinationRecordListItem>>(farmUrl(`/vaccinations/animal/${animalId}`), { params: { page, pageSize } }),
  create: (data: {
    animalId: string;
    vaccineTypeId: string;
    dateGiven: string;
    vetName?: string;
    batchNumber?: string;
    /** Units of the linked medicine consumed; defaults to 1 on the backend. */
    quantityUsed?: number;
    cost?: number;
    notes?: string;
  }) => api.post<VaccinationRecord>(farmUrl('/vaccinations'), data),
  update: (id: string, data: {
    animalId: string;
    vaccineTypeId: string;
    dateGiven: string;
    vetName?: string;
    batchNumber?: string;
    cost?: number;
    notes?: string;
  }) => api.put<VaccinationRecord>(farmUrl(`/vaccinations/${id}`), data),
  remove: (id: string) => api.delete(farmUrl(`/vaccinations/${id}`)),
};

// ── Vaccination Schedules & Status ──────────────────────

export const vaccinationSchedulesApi = {
  list: (params: { page?: number; pageSize?: number } = {}) =>
    api.get<PagedResult<VaccinationSchedule>>(farmUrl('/vaccinations/schedule'), { params }),
  create: (data: {
    vaccineTypeId: string;
    animalTypeId?: string;
    breedId?: string;
    recurrenceDays: number;
    isActive?: boolean;
    notes?: string;
  }) => api.post<VaccinationSchedule>(farmUrl('/vaccinations/schedule'), data),
  update: (id: string, data: {
    vaccineTypeId: string;
    animalTypeId?: string;
    breedId?: string;
    recurrenceDays: number;
    isActive?: boolean;
    notes?: string;
  }) => api.put<VaccinationSchedule>(farmUrl(`/vaccinations/schedule/${id}`), data),
  remove: (id: string) => api.delete(farmUrl(`/vaccinations/schedule/${id}`)),
};

export const vaccinationStatusApi = {
  all: () =>
    api.get<VaccinationStatus[]>(farmUrl('/vaccinations/status')),
  overdue: () =>
    api.get<VaccinationStatus[]>(farmUrl('/vaccinations/status/overdue')),
};

// ── Health Cost Rollups ────────────────────────────────

export interface HealthCostFilter {
  from?: string;
  to?: string;
}



// ── Weight Check Schedules & Status ────────────────────

export const weightCheckSchedulesApi = {
  list: (params: { page?: number; pageSize?: number } = {}) =>
    api.get<PagedResult<WeightCheckSchedule>>(farmUrl('/weight-schedules'), { params }),
  create: (data: {
    animalTypeId?: string;
    breedId?: string;
    ageCategoryId?: string;
    recurrenceDays: number;
    isActive?: boolean;
    notes?: string;
  }) => api.post<WeightCheckSchedule>(farmUrl('/weight-schedules'), data),
  update: (id: string, data: {
    animalTypeId?: string;
    breedId?: string;
    ageCategoryId?: string;
    recurrenceDays: number;
    isActive?: boolean;
    notes?: string;
  }) => api.put<WeightCheckSchedule>(farmUrl(`/weight-schedules/${id}`), data),
  remove: (id: string) => api.delete(farmUrl(`/weight-schedules/${id}`)),
};

export const weightCheckStatusApi = {
  all: () =>
    api.get<WeightCheckStatus[]>(farmUrl('/weight-schedules/status')),
  overdue: () =>
    api.get<WeightCheckStatus[]>(farmUrl('/weight-schedules/status/overdue')),
};

// ── Health Cost Rollups ────────────────────────────────

export const healthCostsApi = {
  summary: (filter: HealthCostFilter = {}) =>
    api.get<HealthCostSummary>(farmUrl('/health/costs/summary'), { params: filter }),
  byVet: (filter: HealthCostFilter = {}) =>
    api.get<HealthCostByVet[]>(farmUrl('/health/costs/by-vet'), { params: filter }),
  byAnimal: (filter: HealthCostFilter = {}) =>
    api.get<HealthCostByAnimal[]>(farmUrl('/health/costs/by-animal'), { params: filter }),
  byMonth: (year?: number) =>
    api.get<HealthCostByMonth[]>(farmUrl('/health/costs/by-month'), { params: { year: year || new Date().getFullYear() } }),
};
