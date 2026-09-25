import axios from './axios';
import { farmUrl } from './farmApi';

export interface DashboardSummary {
  totalAnimals: number;
  animalsByType: Record<string, number>;
  animalsByStatus: Record<string, number>;
  pregnantCount: number;
  sickCount: number;
  dueVaccinationCount: number;
  dueWeightCheckCount: number;
  overdueTasks: number;
  upcomingBirths: number;
  totalFeedStockValue: number;
  totalInventoryStockValue: number;
  /** Metrics the backend could not calculate; their values default to 0. */
  degradedMetrics: string[];
}

export interface DashboardAlert {
  /**
   * The alert as a message key plus its arguments, when the server generated it from a
   * template (see `AlertMessageKeys` on the server). Render these in preference to the
   * English `title`/`message`, which stay as the fallback.
   */
  titleKey?: string | null;
  titleArgs?: Record<string, unknown> | null;
  messageKey?: string | null;
  messageArgs?: Record<string, unknown> | null;
  alertType: string;
  severity: string;
  title: string;
  message: string;
  dueDate?: string;
  link?: string;
  /**
   * Stable identity of the underlying condition (entity ids only, no dates).
   * The notification dispatcher dedupes on it; the dashboard does not need it,
   * so treat it as optional here.
   */
  sourceKey?: string;
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
