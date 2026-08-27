import api from './axios';
import { farmUrl } from './farmApi';
import type { Expense, ExpenseCategory, IncomeCategory, IncomeRecord, PaymentMethod, PagedResult, ProfitLossReport, CategoryBreakdownReport, MonthlySummaryItem } from '../types';

export const expenseCategoriesApi = {
  list: () => api.get<ExpenseCategory[]>(farmUrl('/finance/expense-categories')),
  create: (data: { name: string; description?: string }) =>
    api.post<ExpenseCategory>(farmUrl('/finance/expense-categories'), data),
  update: (id: string, data: { name: string; description?: string }) =>
    api.put<ExpenseCategory>(farmUrl(`/finance/expense-categories/${id}`), data),
  remove: (id: string) => api.delete(farmUrl(`/finance/expense-categories/${id}`)),
};

export const paymentMethodsApi = {
  list: () => api.get<PaymentMethod[]>(farmUrl('/finance/payment-methods')),
  create: (data: { name: string; description?: string }) =>
    api.post<PaymentMethod>(farmUrl('/finance/payment-methods'), data),
  update: (id: string, data: { name: string; description?: string }) =>
    api.put<PaymentMethod>(farmUrl(`/finance/payment-methods/${id}`), data),
  remove: (id: string) => api.delete(farmUrl(`/finance/payment-methods/${id}`)),
};

export interface ExpenseListFilter {
  from?: string;
  to?: string;
  expenseCategoryId?: string;
  paymentMethodId?: string;
  animalId?: string;
  locationId?: string;
  search?: string;
  page?: number;
  pageSize?: number;
}

export interface ExpensePayload {
  expenseDate?: string;
  amount: number;
  expenseCategoryId: string;
  paymentMethodId: string;
  animalId?: string;
  locationId?: string;
  description?: string;
}

export const expensesApi = {
  list: (filter: ExpenseListFilter) =>
    api.get<PagedResult<Expense>>(farmUrl('/finance/expenses'), { params: filter }),
  get: (id: string) => api.get<Expense>(farmUrl(`/finance/expenses/${id}`)),
  create: (data: ExpensePayload) => api.post<Expense>(farmUrl('/finance/expenses'), data),
  update: (id: string, data: ExpensePayload) =>
    api.put<Expense>(farmUrl(`/finance/expenses/${id}`), data),
  remove: (id: string) => api.delete(farmUrl(`/finance/expenses/${id}`)),
};

export const incomeCategoriesApi = {
  list: () => api.get<IncomeCategory[]>(farmUrl('/finance/income-categories')),
  create: (data: { name: string; description?: string }) =>
    api.post<IncomeCategory>(farmUrl('/finance/income-categories'), data),
  update: (id: string, data: { name: string; description?: string }) =>
    api.put<IncomeCategory>(farmUrl(`/finance/income-categories/${id}`), data),
  remove: (id: string) => api.delete(farmUrl(`/finance/income-categories/${id}`)),
};

export interface IncomeRecordListFilter {
  from?: string;
  to?: string;
  incomeCategoryId?: string;
  paymentMethodId?: string;
  animalId?: string;
  locationId?: string;
  search?: string;
  page?: number;
  pageSize?: number;
}

export interface IncomeRecordPayload {
  incomeDate?: string;
  amount: number;
  incomeCategoryId: string;
  paymentMethodId: string;
  animalId?: string;
  locationId?: string;
  description?: string;
}

export const incomeRecordsApi = {
  list: (filter: IncomeRecordListFilter) =>
    api.get<PagedResult<IncomeRecord>>(farmUrl('/finance/income-records'), { params: filter }),
  get: (id: string) => api.get<IncomeRecord>(farmUrl(`/finance/income-records/${id}`)),
  create: (data: IncomeRecordPayload) => api.post<IncomeRecord>(farmUrl('/finance/income-records'), data),
  update: (id: string, data: IncomeRecordPayload) =>
    api.put<IncomeRecord>(farmUrl(`/finance/income-records/${id}`), data),
  remove: (id: string) => api.delete(farmUrl(`/finance/income-records/${id}`)),
};

export interface FinanceReportFilter {
  from?: string;
  to?: string;
}

export const financeReportsApi = {
  profitLoss: (filter: FinanceReportFilter = {}) =>
    api.get<ProfitLossReport>(farmUrl('/finance/reports/profit-loss'), { params: filter }),
  expenseBreakdown: (filter: FinanceReportFilter = {}) =>
    api.get<CategoryBreakdownReport>(farmUrl('/finance/reports/expense-breakdown'), { params: filter }),
  incomeBreakdown: (filter: FinanceReportFilter = {}) =>
    api.get<CategoryBreakdownReport>(farmUrl('/finance/reports/income-breakdown'), { params: filter }),
  monthlySummary: (year: number) =>
    api.get<MonthlySummaryItem[]>(farmUrl('/finance/reports/monthly-summary'), { params: { year } }),
};
