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

export const reportsApi = {
  animalReport: (from?: string, to?: string) =>
    axios.get<AnimalReport>(farmUrl('/reports/animals'), { params: { from, to } }),
  medicalReport: (from?: string, to?: string) =>
    axios.get<MedicalReport>(farmUrl('/reports/medical'), { params: { from, to } }),
  vaccinationReport: (from?: string, to?: string) =>
    axios.get<VaccinationReport>(farmUrl('/reports/vaccination'), { params: { from, to } }),
  employeeReport: (from?: string, to?: string) =>
    axios.get<EmployeeReport>(farmUrl('/reports/employees'), { params: { from, to } }),
};
