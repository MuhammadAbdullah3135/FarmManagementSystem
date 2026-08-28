import axios from './axios';
import { farmUrl } from './farmApi';

export interface DashboardSummary {
  totalAnimals: number;
  animalsByType: Record<string, number>;
  animalsByStatus: Record<string, number>;
  pregnantCount: number;
  sickCount: number;
  dueVaccinationCount: number;
  overdueTasks: number;
  upcomingBirths: number;
  totalFeedStockValue: number;
  totalInventoryStockValue: number;
}

export interface DashboardAlert {
  alertType: string;
  severity: string;
  title: string;
  message: string;
  dueDate?: string;
  link?: string;
}

export interface AnimalTrendPoint {
  month: string;
  count: number;
}

export interface CategoryBreakdownItem {
  categoryId: string;
  categoryName: string;
  total: number;
  percentage: number;
}

export interface CategoryBreakdownReport {
  items: CategoryBreakdownItem[];
  grandTotal: number;
}

export interface ConsumptionTrendPoint {
  periodStart: string;
  label: string;
  quantity: number;
  cost: number;
}

export interface MonthlySummaryItem {
  year: number;
  month: number;
  monthName: string;
  income: number;
  expenses: number;
  net: number;
}

export interface DashboardCharts {
  animalTrends: AnimalTrendPoint[];
  expenseBreakdown: CategoryBreakdownReport | null;
  feedConsumptionTrend: ConsumptionTrendPoint[];
  monthlyPL: MonthlySummaryItem[];
}

export const dashboardApi = {
  summary: () => axios.get<DashboardSummary>(farmUrl('/dashboard/summary')),
  alerts: () => axios.get<DashboardAlert[]>(farmUrl('/dashboard/alerts')),
  charts: (from?: string, to?: string) =>
    axios.get<DashboardCharts>(farmUrl('/dashboard/charts'), { params: { from, to } }),
};
