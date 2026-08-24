import api from './axios';
import { farmUrl } from './farmApi';
import type {
  Department,
  EmployeeRole,
  Employee,
  SalaryPayment,
  PayrollReport,
  PagedResult,
} from '../types';

export const departmentsApi = {
  list: () => api.get<Department[]>(farmUrl('/departments')),
  create: (data: { name: string; description?: string }) =>
    api.post(farmUrl('/departments'), data),
  remove: (id: string) => api.delete(farmUrl(`/departments/${id}`)),
};

export const employeeRolesApi = {
  list: () => api.get<EmployeeRole[]>(farmUrl('/employee-roles')),
  create: (data: { name: string; description?: string }) =>
    api.post(farmUrl('/employee-roles'), data),
  remove: (id: string) => api.delete(farmUrl(`/employee-roles/${id}`)),
};

export interface EmployeeListFilter {
  search?: string;
  departmentId?: string;
  employeeRoleId?: string;
  isActive?: boolean;
  page?: number;
  pageSize?: number;
}

export const employeesApi = {
  list: (filter: EmployeeListFilter) =>
    api.get<PagedResult<Employee>>(farmUrl('/employees'), { params: filter }),
  get: (id: string) => api.get<Employee>(farmUrl(`/employees/${id}`)),
  create: (data: {
    firstName: string;
    lastName: string;
    phone?: string;
    email?: string;
    departmentId?: string;
    employeeRoleId?: string;
    salaryType: string;
    salaryRate: number;
    hireDate: string;
    notes?: string;
  }) => api.post<Employee>(farmUrl('/employees'), data),
  update: (
    id: string,
    data: {
      firstName: string;
      lastName: string;
      phone?: string;
      email?: string;
      departmentId?: string;
      employeeRoleId?: string;
      salaryType: string;
      salaryRate: number;
      isActive: boolean;
      notes?: string;
    }
  ) => api.put<Employee>(farmUrl(`/employees/${id}`), data),
  remove: (id: string) => api.delete(farmUrl(`/employees/${id}`)),
};

export const salaryPaymentsApi = {
  list: (employeeId: string, page = 1, pageSize = 20) =>
    api.get<PagedResult<SalaryPayment>>(farmUrl(`/employees/${employeeId}/salary-payments`), {
      params: { page, pageSize },
    }),
  record: (
    employeeId: string,
    data: { amount: number; paymentDate?: string; notes?: string }
  ) => api.post<SalaryPayment>(farmUrl(`/employees/${employeeId}/salary-payments`), data),
  remove: (employeeId: string, paymentId: string) =>
    api.delete(farmUrl(`/employees/${employeeId}/salary-payments/${paymentId}`)),
};

export const payrollApi = {
  report: (from?: string, to?: string) =>
    api.get<PayrollReport>(farmUrl('/employees/payroll-report'), { params: { from, to } }),
};
