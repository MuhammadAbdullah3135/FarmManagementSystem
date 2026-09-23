export interface AuthResponse {
  accessToken: string;
  refreshToken: string;
  userId: string;
  accountId: string;
  email: string;
  firstName: string;
  lastName: string;
  roles: string[];
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

export interface ConfirmResetPasswordRequest {
  token: string;
  newPassword: string;
}

// Shared
export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
}

/**
 * A page that can also answer "what changed since you last asked" (Phase 5.6).
 *
 * `deletedIds` is why this exists rather than a plain query filter: a row that was removed
 * cannot be reported by returning it, and its absence is indistinguishable from "unchanged", so
 * a device would keep showing it. `cursor` is the server's timestamp for this read — send it
 * back as `updatedSince` next time rather than the device's own clock, which may be wrong by
 * more than the interval between syncs. `requiresFullSync` means the cursor could not be
 * answered precisely and the collection should be re-read in full.
 */
export interface DeltaResult<T> extends PagedResult<T> {
  deletedIds: string[];
  cursor: string;
  requiresFullSync: boolean;
}

// General inventory
export interface Customer {
  id: string;
  farmId: string;
  name: string;
  contactInfo?: string;
  saleCount: number;
  totalSales: number;
}

export interface CustomerSale {
  id: string;
  customerId: string;
  customerName: string;
  inventoryItemId: string;
  inventoryItemName: string;
  unit: string;
  quantity: number;
  totalAmount: number;
  unitPrice: number;
  saleDate: string;
  stockMovementId?: string;
  incomeRecordId?: string;
  notes?: string;
}

export interface Supplier {
  id: string;
  farmId: string;
  name: string;
  contactInfo?: string;
  productsSupplied?: string;
  purchaseCount: number;
  totalPurchased: number;
}

export interface SupplierPurchase {
  id: string;
  supplierId: string;
  supplierName: string;
  inventoryItemId: string;
  inventoryItemName: string;
  unit: string;
  quantity: number;
  totalCost: number;
  unitCost: number;
  purchaseDate: string;
  stockMovementId?: string;
  expenseId?: string;
  notes?: string;
}

export interface InventoryReport {
  totalStockValue: number;
  totalItems: number;
  lowStockItemCount: number;
  totalMovementQuantity: number;
  currentStock: InventoryStockReportItem[];
  movementSummary: InventoryMovementSummary[];
  lowStockAlerts: InventoryLowStockAlert[];
}

export interface InventoryStockReportItem {
  inventoryItemId: string;
  itemName: string;
  category?: string;
  unit: string;
  quantity: number;
  reorderLevel: number;
  unitCost: number;
  stockValue: number;
  location?: string;
  isLowStock: boolean;
}

export interface InventoryMovementSummary {
  movementType: InventoryMovementType;
  movementTypeName: string;
  movementCount: number;
  quantity: number;
}

export interface InventoryLowStockAlert {
  inventoryItemId: string;
  itemName: string;
  unit: string;
  quantity: number;
  reorderLevel: number;
  shortfall: number;
  location?: string;
}

export interface InventoryItem {
  id: string;
  farmId: string;
  name: string;
  category?: string;
  unit: string;
  quantity: number;
  reorderLevel: number;
  unitCost: number;
  location?: string;
  stockValue: number;
  isLowStock: boolean;
  createdAt: string;
}

// Feed management
export type FeedCategory = 'Forage' | 'Concentrate' | 'Mineral' | 'Supplement' | 'Additive' | 'Other';
export type FeedUnit = 'Kilogram' | 'Gram' | 'Ton' | 'Liter' | 'Bale' | 'Bag' | 'Other';
export type StockMovementType = 'Purchase' | 'Consumption' | 'Adjustment';

export type InventoryMovementType = 'Purchase' | 'Consumption' | 'Transfer' | 'Adjustment';

export interface StockMovement {
  id: string;
  farmId: string;
  inventoryItemId: string;
  inventoryItemName: string;
  unit: string;
  movementType: InventoryMovementType;
  movementTypeName: string;
  quantity: number;
  signedQuantity: number;
  movementDate: string;
  reason?: string;
  performedBy?: string;
}

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

export interface FeedStockMovement {
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

// Finance - expenses
export interface ExpenseCategory {
  id: string;
  name: string;
  description?: string;
  expenseCount: number;
}

export interface PaymentMethod {
  id: string;
  name: string;
  description?: string;
  expenseCount: number;
  incomeRecordCount: number;
}

export interface Expense {
  id: string;
  expenseDate: string;
  amount: number;
  expenseCategoryId: string;
  expenseCategoryName: string;
  paymentMethodId: string;
  paymentMethodName: string;
  animalId?: string;
  animalTagNumber?: string;
  animalName?: string;
  locationId?: string;
  locationName?: string;
  description?: string;
}

// Finance - income
export interface IncomeCategory {
  id: string;
  name: string;
  description?: string;
  incomeRecordCount: number;
}

export interface IncomeRecord {
  id: string;
  incomeDate: string;
  amount: number;
  incomeCategoryId: string;
  incomeCategoryName: string;
  paymentMethodId: string;
  paymentMethodName: string;
  animalId?: string;
  animalTagNumber?: string;
  animalName?: string;
  locationId?: string;
  locationName?: string;
  description?: string;
}

// Finance - reports
export interface ProfitLossReport {
  totalIncome: number;
  totalExpenses: number;
  netProfit: number;
  from?: string;
  to?: string;
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

export interface MonthlySummaryItem {
  year: number;
  month: number;
  monthName: string;
  income: number;
  expenses: number;
  net: number;
}

// Health - Medical Records
export type MedicalRecordStatus = 'Open' | 'InProgress' | 'Resolved';

export interface MedicalRecord {
  id: string;
  farmId: string;
  animalId: string;
  animalTagNumber: string;
  animalName?: string;
  symptoms: string;
  diagnosis?: string;
  treatment?: string;
  medicineUsed?: string;
  dosage?: string;
  vetName?: string;
  cost: number;
  dateRecorded: string;
  followUpDate?: string;
  status: MedicalRecordStatus;
  statusName: string;
  notes?: string;
  followUpTaskId?: string;
  createdAt: string;
}

export interface MedicalRecordListItem {
  id: string;
  animalId: string;
  animalTagNumber: string;
  animalName?: string;
  diagnosis?: string;
  vetName?: string;
  cost: number;
  dateRecorded: string;
  followUpDate?: string;
  status: MedicalRecordStatus;
  statusName: string;
}

// Health - Medicine Inventory
export interface Medicine {
  id: string;
  name: string;
  description?: string;
  unit: string;
  lowStockThreshold: number;
  expiringSoonDays: number;
  totalQuantity: number;
  batchCount: number;
  createdAt: string;
}

export interface MedicineListItem {
  id: string;
  name: string;
  description?: string;
  unit: string;
  lowStockThreshold: number;
  totalQuantity: number;
  batchCount: number;
}

export interface MedicineStock {
  id: string;
  medicineId: string;
  batchNumber: string;
  quantity: number;
  unitCost: number;
  totalCost: number;
  expiryDate: string;
  supplier?: string;
  dateReceived: string;
}

export interface MedicineUsage {
  id: string;
  medicineId: string;
  medicineName: string;
  medicineStockId: string;
  batchNumber: string;
  medicalRecordId?: string;
  quantityUsed: number;
  dateUsed: string;
  notes?: string;
}

export type MedicineAlertType = 'LowStock' | 'ExpiringSoon' | 'Expired';

export interface MedicineAlert {
  medicineId: string;
  medicineName: string;
  unit: string;
  stockId: string;
  batchNumber: string;
  currentQuantity: number;
  lowStockThreshold: number;
  expiryDate: string;
  alertType: MedicineAlertType;
  alertTypeName: string;
}

// Health - Vaccines
export interface VaccineType {
  id: string;
  name: string;
  defaultDosage?: string;
  notes?: string;
  linkedMedicineId?: string;
  linkedMedicineName?: string;
  vaccinationCount: number;
  scheduleCount: number;
  createdAt: string;
}

export interface VaccineTypeListItem {
  id: string;
  name: string;
  defaultDosage?: string;
  linkedMedicineId?: string;
  linkedMedicineName?: string;
  vaccinationCount: number;
  scheduleCount: number;
}

export interface VaccinationRecord {
  id: string;
  farmId: string;
  animalId: string;
  animalTagNumber: string;
  animalName?: string;
  vaccineTypeId: string;
  vaccineTypeName: string;
  dateGiven: string;
  vetName?: string;
  batchNumber?: string;
  cost: number;
  notes?: string;
  createdAt: string;
}

export interface VaccinationRecordListItem {
  id: string;
  animalId: string;
  animalTagNumber: string;
  animalName?: string;
  vaccineTypeId: string;
  vaccineTypeName: string;
  dateGiven: string;
  vetName?: string;
  batchNumber?: string;
  cost: number;
}

// Health - Vaccination Schedules
export interface VaccinationSchedule {
  id: string;
  vaccineTypeId: string;
  vaccineTypeName: string;
  animalTypeId?: string;
  animalTypeName?: string;
  breedId?: string;
  breedName?: string;
  recurrenceDays: number;
  isActive: boolean;
  notes?: string;
  createdAt: string;
}

export type VaccinationStatusType = 'Upcoming' | 'Due' | 'Overdue';

export interface VaccinationStatus {
  animalId: string;
  animalTagNumber: string;
  animalName?: string;
  vaccineTypeId: string;
  vaccineTypeName: string;
  lastVaccinationDate?: string;
  nextDueDate: string;
  status: VaccinationStatusType;
  statusName: string;
  daysUntilDue: number;
}



// Health - Weight Check Schedules
export interface WeightCheckSchedule {
  id: string;
  animalTypeId?: string;
  animalTypeName?: string;
  breedId?: string;
  breedName?: string;
  ageCategoryId?: string;
  ageCategoryName?: string;
  recurrenceDays: number;
  isActive: boolean;
  notes?: string;
  createdAt: string;
}

export type WeightCheckStatusType = 'Upcoming' | 'Due' | 'Overdue';

export interface WeightCheckStatus {
  animalId: string;
  animalTagNumber: string;
  animalName?: string;
  lastWeightDate?: string;
  nextDueDate: string;
  status: WeightCheckStatusType;
  statusName: string;
  daysUntilDue: number;
}

// Health - Cost Rollups
export interface HealthCostSummary {
  totalMedicalCost: number;
  medicalRecordCount: number;
  totalVaccinationCost: number;
  vaccinationRecordCount: number;
  grandTotal: number;
}

export interface HealthCostByVet {
  vetName: string;
  totalCost: number;
  recordCount: number;
}

export interface HealthCostByAnimal {
  animalId: string;
  tagNumber: string;
  animalName?: string;
  totalCost: number;
  recordCount: number;
}

export interface HealthCostByMonth {
  year: number;
  month: number;
  monthName: string;
  medicalCost: number;
  vaccinationCost: number;
  total: number;
}

// Animals
export interface AnimalListItem {
  id: string;
  tagNumber: string;
  name?: string;
  animalTypeId: string;
  animalTypeName: string;
  breedId?: string;
  breedName?: string;
  sexOptionId: string;
  sexValue: string;
  animalStatusId: string;
  statusName: string;
  statusCategory: number;
  locationId?: string;
  locationName?: string;
  sireId?: string;
  sireTagNumber?: string;
  damId?: string;
  damTagNumber?: string;
  dateOfBirth?: string;
  latestWeightKg?: number;
  createdAt: string;
}

export interface AnimalDetail {
  id: string;
  farmId: string;
  tagNumber: string;
  name?: string;
  animalTypeId: string;
  animalTypeName: string;
  breedId?: string;
  breedName?: string;
  sexOptionId: string;
  sexValue: string;
  ageCategoryId?: string;
  ageCategoryName?: string;
  animalStatusId: string;
  statusName: string;
  statusCategory: number;
  locationId?: string;
  locationName?: string;
  sireId?: string;
  sireTagNumber?: string;
  damId?: string;
  damTagNumber?: string;
  dateOfBirth?: string;
  acquisitionDate?: string;
  notes?: string;
  identifications: AnimalIdentification[];
  weightRecordsCount: number;
  imagesCount: number;
  documentsCount: number;
  createdAt: string;
}

export interface AnimalIdentification {
  id: string;
  identificationTypeId: string;
  identificationTypeName: string;
  value: string;
  isPrimary: boolean;
  dateAttached?: string;
  dateRemoved?: string;
}

export interface Breed {
  id: string;
  name: string;
  animalTypeId: string;
  averageGestationDays: number;
}

export interface AnimalStatus {
  id: string;
  name: string;
  isActive: boolean;
  category: number;
  isSystemDefined: boolean;
}

// Breeding
export type BreedingMethod = 0 | 1;
export type BreedingResult = 0 | 1 | 2;

export interface BreedingRecord {
  id: string;
  farmId: string;
  sireId: string;
  sireTagNumber: string;
  sireName?: string;
  damId: string;
  damTagNumber: string;
  damName?: string;
  breedingDate: string;
  method: BreedingMethod;
  vetName?: string;
  result: BreedingResult;
  notes?: string;
  createdAt: string;
}

export type GestationStage = 0 | 1 | 2 | 3;

export interface GestationRecord {
  id: string;
  farmId: string;
  breedingRecordId: string;
  animalId: string;
  animalTagNumber: string;
  animalName?: string;
  sireId: string;
  sireTagNumber: string;
  breedingDate: string;
  confirmedDate?: string;
  expectedDeliveryDate: string;
  daysUntilDue: number;
  daysElapsed: number;
  currentStage: GestationStage;
  healthCheckNotes?: string;
  healthCheckCount: number;
  createdAt: string;
}

export interface GestationHealthCheck {
  id: string;
  gestationRecordId: string;
  checkDate: string;
  notes?: string;
  performedBy?: string;
  weightKg?: number;
  createdAt: string;
}

export type BirthOutcome = 0 | 1 | 2;

export interface BirthRecord {
  id: string;
  farmId: string;
  damId: string;
  damTagNumber: string;
  damName?: string;
  gestationRecordId?: string;
  breedingRecordId?: string;
  birthDate: string;
  offspringCount: number;
  aliveCount: number;
  vetName?: string;
  notes?: string;
  createdAt: string;
  offspring: BirthOffspring[];
}

export interface BirthOffspring {
  id: string;
  tagNumber: string;
  name?: string;
  sexOptionId: string;
  sexValue: string;
  outcome: BirthOutcome;
  birthWeightKg?: number;
  offspringAnimalId?: string;
  notes?: string;
}

// Lineage
export interface LineageNode {
  id: string;
  tagNumber: string;
  name?: string;
  sex: string;
  breed?: string;
  status: string;
  dateOfBirth?: string;
  isRoot: boolean;
  sire?: LineageNode;
  dam?: LineageNode;
  offspring: LineageNode[];
}

export interface LineageResponse {
  root: LineageNode;
  ancestorDepth: number;
  descendantDepth: number;
}

// Breeding Reports
export interface BreedingSummaryReport {
  totalBreedingRecords: number;
  pendingCount: number;
  confirmedCount: number;
  failedCount: number;
  confirmationRate: number;
  activePregnancies: number;
  totalBirths: number;
  totalOffspring: number;
  aliveOffspring: number;
  averageGestationDays: number;
}

export interface BreedingTrendEntry {
  month: string;
  count: number;
  confirmed: number;
  failed: number;
}

export interface MethodDistributionEntry {
  method: string;
  count: number;
  percentage: number;
}

export interface SirePerformanceEntry {
  sireId: string;
  sireTagNumber: string;
  sireName?: string;
  totalBreedings: number;
  confirmed: number;
  failed: number;
  pending: number;
  successRate: number;
  totalOffspring: number;
}

export interface CalendarEventEntry {
  id: string;
  eventType: string;
  animalTag: string;
  animalName?: string;
  eventDate: string;
  details?: string;
  priority: string;
}
