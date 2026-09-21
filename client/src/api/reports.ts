import axios from './axios';
import { farmUrl } from './farmApi';

export interface AnimalCountByCategory {
  name: string;
  count: number;
}

export interface AnimalTrendPoint {
  month: string;
  avgWeight?: number;
  animalCount: number;
}

export interface AnimalReport {
  totalCount: number;
  byType: AnimalCountByCategory[];
  byStatus: AnimalCountByCategory[];
  growthTrend: AnimalTrendPoint[];
  mortalityCount: number;
  transferCount: number;
}

export interface MedicalMonthlyTrend {
  month: string;
  caseCount: number;
  cost: number;
}

export interface MedicalByVet {
  vetName: string;
  caseCount: number;
  totalCost: number;
}

export interface MedicalByStatus {
  status: string;
  count: number;
}

export interface MedicalReport {
  totalCases: number;
  totalCost: number;
  monthlyTrend: MedicalMonthlyTrend[];
  byVet: MedicalByVet[];
  byStatus: MedicalByStatus[];
}

export interface VaccinationMonthlyTrend {
  month: string;
  count: number;
  cost: number;
}

export interface VaccinationByVaccine {
  vaccineName: string;
  count: number;
  totalCost: number;
}

export interface VaccinationReport {
  totalVaccinations: number;
  totalCost: number;
  overdueCount: number;
  upcomingCount: number;
  monthlyTrend: VaccinationMonthlyTrend[];
  byVaccine: VaccinationByVaccine[];
}

export interface EmployeePayrollByMonth {
  month: string;
  amount: number;
  paymentCount: number;
}

export interface EmployeePayrollByDepartment {
  departmentName: string;
  employeeCount: number;
  totalPaid: number;
}

export interface EmployeeReport {
  totalEmployees: number;
  activeEmployees: number;
  totalPaid: number;
  paymentCount: number;
  expectedMonthlyPayroll: number;
  byMonth: EmployeePayrollByMonth[];
  byDepartment: EmployeePayrollByDepartment[];
}

// ── cost per animal ─────────────────────────────────────

/** How a figure was attributed. See the report's `rules` for the wording shown to users. */
export type CostAllocationMethod = 'direct' | 'location-animal-days' | 'farm-animal-days';

export interface CostComponent {
  key: string;
  label: string;
  source: string;
  method: CostAllocationMethod | string;
  amount: number;
  recordCount: number;
  /** The pool a share came from, with the two numbers the UI shows as the arithmetic. */
  poolAmount?: number;
  allocatedDays?: number;
  poolDays?: number;
  roundingAdjustment: number;
}

export interface AnimalCostRow {
  animalId: string;
  tagNumber: string;
  name?: string;
  locationId?: string;
  locationName?: string;
  presentFrom: string;
  presentTo?: string;
  animalDays: number;
  shareOfFarmDays: number;
  costs: CostComponent[];
  revenue: CostComponent[];
  totalCost: number;
  totalRevenue: number;
  margin: number;
  warnings: string[];
}

export interface HerdCostRow {
  locationId?: string;
  locationName: string;
  animalCount: number;
  animalDays: number;
  totalCost: number;
  totalRevenue: number;
  margin: number;
}

export interface CostTotals {
  totalCost: number;
  totalRevenue: number;
  margin: number;
  costPerAnimalDay: number;
}

export interface CostRule {
  key: string;
  title: string;
  description: string;
}

export interface CostReportWarning {
  code: string;
  message: string;
  affectedCount: number;
  amount?: number;
}

export interface CostReconciliation {
  farmExpensesTotal: number;
  healthLinkedExpenses: number;
  expensesAttributedToAnimals: number;
  expensesAllocatedFromLocations: number;
  expensesAllocatedFromFarmPool: number;
  expensesUnallocated: number;
  expensesReconcile: boolean;
  feedConsumedTotal: number;
  feedAttributedToAnimals: number;
  feedAllocatedFromLocations: number;
  feedUnallocated: number;
  feedReconciles: boolean;
  healthRecordsTotal: number;
  healthAttributedToAnimals: number;
  healthCostOutsideTheExpenseLedger: number;
  healthReconciles: boolean;
  labourTotal: number;
  costOutsideTheExpenseLedger: number;
}

export interface CostPerAnimalReport {
  from: string;
  to: string;
  totalAnimalDays: number;
  animals: AnimalCostRow[];
  herds: HerdCostRow[];
  farm: CostTotals;
  rules: CostRule[];
  warnings: CostReportWarning[];
  reconciliation: CostReconciliation;
}

export const reportsApi = {
  animalReport: (from?: string, to?: string) =>
    axios.get<AnimalReport>(farmUrl('/reports/animals'), { params: { from, to } }),
  medicalReport: (from?: string, to?: string) =>
    axios.get<MedicalReport>(farmUrl('/reports/medical'), { params: { from, to } }),
  vaccinationReport: (from?: string, to?: string) =>
    axios.get<VaccinationReport>(farmUrl('/reports/vaccination'), { params: { from, to } }),
  employeeReport: (from?: string, to?: string) =>
    axios.get<EmployeeReport>(farmUrl('/reports/employees'), { params: { from, to } }),

  /**
   * Cost and revenue per animal, with every shared pool allocated by animal-days and the
   * allocation's inputs travelling with each figure. The reconciliation is deliberately not
   * dropped by this client: the report's claim is that its numbers add up, so the UI shows
   * the check rather than hiding it.
   */
  costPerAnimalReport: (from?: string, to?: string) =>
    axios.get<CostPerAnimalReport>(farmUrl('/reports/cost-per-animal'), { params: { from, to } }),
};
