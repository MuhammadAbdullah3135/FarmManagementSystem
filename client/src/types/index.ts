export interface AuthResponse {
  accessToken: string;
  refreshToken: string;
  userId: string;
  accountId: string;
  email: string;
  firstName: string;
  lastName: string;
}

export interface User {
  userId: string;
  accountId: string;
  email: string;
  firstName: string;
  lastName: string;
  roles: string[];
}

export interface Farm {
  id: string;
  name: string;
  description?: string;
  isActive: boolean;
  createdAt: string;
  userFarmRole?: string;
}

export interface LoginRequest {
  email: string;
  password: string;
}

export interface RegisterRequest {
  email: string;
  password: string;
  firstName: string;
  lastName: string;
  accountName: string;
}

export interface ResetPasswordRequest {
  email: string;
}

// Shared
export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
}

// Feed management
export type FeedCategory = 'Forage' | 'Concentrate' | 'Mineral' | 'Supplement' | 'Additive' | 'Other';
export type FeedUnit = 'Kilogram' | 'Gram' | 'Ton' | 'Liter' | 'Bale' | 'Bag' | 'Other';
export type StockMovementType = 'Purchase' | 'Consumption' | 'Adjustment';

export interface FeedType {
  id: string;
  name: string;
  category: FeedCategory;
  categoryName: string;
  unit: FeedUnit;
  unitName: string;
  costPerUnit: number;
  notes?: string;
}

export interface FeedStock {
  feedTypeId: string;
  feedTypeName: string;
  unitName: string;
  quantityPurchased: number;
  quantityConsumed: number;
  netAdjustments: number;
  currentStock: number;
  totalCost: number;
}

export interface StockMovement {
  id: string;
  feedTypeId: string;
  feedTypeName: string;
  movementType: StockMovementType;
  movementTypeName: string;
  quantity: number;
  signedQuantity: number;
  unitCost?: number;
  totalCost?: number;
  supplier?: string;
  notes?: string;
  movementDate: string;
}

export interface FeedRecord {
  id: string;
  feedTypeId: string;
  feedTypeName: string;
  unitName: string;
  animalId?: string;
  animalTagNumber?: string;
  animalName?: string;
  locationId?: string;
  locationName?: string;
  quantity: number;
  unitCost: number;
  totalCost: number;
  fedAt: string;
  notes?: string;
}

export interface DietPlanItem {
  id: string;
  feedTypeId: string;
  feedTypeName: string;
  unitName: string;
  quantityPerFeeding: number;
}

export interface DietPlan {
  id: string;
  name: string;
  animalTypeId?: string;
  animalTypeName?: string;
  breedId?: string;
  breedName?: string;
  ageCategoryId?: string;
  ageCategoryName?: string;
  minWeightKg?: number;
  maxWeightKg?: number;
  isActive: boolean;
  notes?: string;
  items: DietPlanItem[];
  scheduleCount: number;
}

export interface FeedingSchedule {
  id: string;
  dietPlanId: string;
  dietPlanName: string;
  timeOfDay: string;
  label?: string;
  isActive: boolean;
}

export type FeedingTaskStatus = 'Pending' | 'Completed' | 'Skipped';

export interface FeedingTask {
  id: string;
  feedingScheduleId: string;
  dietPlanId: string;
  dietPlanName: string;
  taskDate: string;
  timeOfDay: string;
  status: FeedingTaskStatus;
  statusName: string;
  targetAnimalCount: number;
  items: DietPlanItem[];
  completedAt?: string;
  notes?: string;
}

export interface ConsumptionTrendPoint {
  periodStart: string;
  label: string;
  quantity: number;
  cost: number;
}

export interface FeedTypeBreakdown {
  feedTypeId: string;
  feedTypeName: string;
  unitName: string;
  quantity: number;
  cost: number;
  sharePercent: number;
}

export interface AnimalConsumption {
  animalId: string;
  tagNumber: string;
  name?: string;
  quantity: number;
  cost: number;
}

export interface LocationConsumption {
  locationId: string;
  locationName: string;
  quantity: number;
  cost: number;
}

export interface FeedCostSummary {
  from: string;
  to: string;
  totalConsumedQuantity: number;
  totalConsumedCost: number;
  totalPurchasedQuantity: number;
  totalPurchasedCost: number;
  currentInventoryValue: number;
}

// HR - employees
export type SalaryType = 'Monthly' | 'Weekly' | 'Daily' | 'Hourly';

export interface Department {
  id: string;
  name: string;
  description?: string;
  employeeCount: number;
}

export interface EmployeeRole {
  id: string;
  name: string;
  description?: string;
  employeeCount: number;
}

export interface Employee {
  id: string;
  firstName: string;
  lastName: string;
  phone?: string;
  email?: string;
  departmentId?: string;
  departmentName?: string;
  employeeRoleId?: string;
  employeeRoleName?: string;
  salaryType: SalaryType;
  salaryTypeName: string;
  salaryRate: number;
  hireDate: string;
  isActive: boolean;
  notes?: string;
}

export interface SalaryPayment {
  id: string;
  employeeId: string;
  employeeName: string;
  amount: number;
  paymentDate: string;
  salaryType: SalaryType;
  salaryTypeName: string;
  notes?: string;
}

export interface PayrollReport {
  from: string;
  to: string;
  totalPaid: number;
  paymentCount: number;
  expectedMonthlyPayroll: number;
  byEmployee: PayrollByEmployee[];
}

export interface PayrollByEmployee {
  employeeId: string;
  employeeName: string;
  totalPaid: number;
  paymentCount: number;
  lastPaymentDate?: string;
}

// Tasks
export type FarmTaskPriority = 'Low' | 'Medium' | 'High';
export type FarmTaskStatus = 'Pending' | 'InProgress' | 'Completed' | 'Cancelled';

export interface FarmTask {
  id: string;
  title: string;
  description?: string;
  priority: FarmTaskPriority;
  priorityName: string;
  status: FarmTaskStatus;
  statusName: string;
  dueDate: string;
  isOverdue: boolean;
  startedAt?: string;
  completedAt?: string;
  completionNotes?: string;
  cancelReason?: string;
  assignedEmployeeId?: string;
  assignedEmployeeName?: string;
  animalId?: string;
  animalName?: string;
  locationId?: string;
  locationName?: string;
  createdAt: string;
}

// Attendance & performance
export type AttendanceStatus = 'Present' | 'Absent' | 'Late' | 'HalfDay' | 'Leave' | 'Holiday';

export interface AttendanceRecord {
  id: string;
  employeeId: string;
  employeeName: string;
  date: string;
  status: AttendanceStatus;
  statusName: string;
  checkInAt?: string;
  checkOutAt?: string;
  hoursWorked?: number;
  notes?: string;
}

export interface PerformanceReview {
  id: string;
  employeeId: string;
  employeeName: string;
  rating: number;
  reviewDate: string;
  periodStart?: string;
  periodEnd?: string;
  strengths?: string;
  areasForImprovement?: string;
  comments?: string;
  reviewedBy?: string;
  createdAt: string;
}
