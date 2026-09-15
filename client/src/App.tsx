import React from 'react';
import { BrowserRouter as Router, Routes, Route, Navigate } from 'react-router-dom';
import { ConfigProvider } from 'antd';
import enUS from 'antd/locale/en_US';
import ErrorBoundary from './components/ErrorBoundary';
import LoginPage from './pages/LoginPage';
import RegisterPage from './pages/RegisterPage';
import ResetPasswordPage from './pages/ResetPasswordPage';
import DashboardPage from './pages/DashboardPage';
import AppLayout from './components/AppLayout';
import ProtectedRoute from './components/ProtectedRoute';
import FeedTypesPage from './pages/feed/FeedTypesPage';
import FeedRecordsPage from './pages/feed/FeedRecordsPage';
import DietPlansPage from './pages/feed/DietPlansPage';
import FeedingSchedulesPage from './pages/feed/FeedingSchedulesPage';
import FeedingTasksPage from './pages/feed/FeedingTasksPage';
import FeedReportsPage from './pages/feed/FeedReportsPage';
import EmployeesPage from './pages/hr/EmployeesPage';
import DepartmentsRolesPage from './pages/hr/DepartmentsRolesPage';
import SalaryPaymentsPage from './pages/hr/SalaryPaymentsPage';
import AttendancePage from './pages/hr/AttendancePage';
import PerformanceReviewsPage from './pages/hr/PerformanceReviewsPage';
import ExpensesPage from './pages/finance/ExpensesPage';
import IncomesPage from './pages/finance/IncomesPage';
import FinanceReportsPage from './pages/finance/FinanceReportsPage';
import CategoriesPage from './pages/finance/CategoriesPage';
import TasksPage from './pages/TasksPage';
import MedicalRecordsPage from './pages/health/MedicalRecordsPage';
import MedicinesPage from './pages/health/MedicinesPage';
import MedicineAlertsPage from './pages/health/MedicineAlertsPage';
import VaccineTypesPage from './pages/health/VaccineTypesPage';
import VaccinationRecordsPage from './pages/health/VaccinationRecordsPage';
import VaccinationSchedulePage from './pages/health/VaccinationSchedulePage';
import WeightCheckSchedulePage from './pages/health/WeightCheckSchedulePage';
import VetCostsPage from './pages/health/VetCostsPage';
import AnimalsPage from './pages/animals/AnimalsPage';
import AnimalDetailPage from './pages/animals/AnimalDetailPage';
import BreedingRecordsPage from './pages/breeding/BreedingRecordsPage';
import GestationPage from './pages/breeding/GestationPage';
import BirthRecordingPage from './pages/breeding/BirthRecordingPage';
import LineagePage from './pages/breeding/LineagePage';
import BreedingReportsPage from './pages/breeding/BreedingReportsPage';
import InventoryItemsPage from './pages/inventory/InventoryItemsPage';
import InventoryMovementsPage from './pages/inventory/InventoryMovementsPage';
import SuppliersPage from './pages/inventory/SuppliersPage';
import CustomersPage from './pages/inventory/CustomersPage';
import InventoryReportsPage from './pages/inventory/InventoryReportsPage';
import AnimalReportsPage from './pages/reports/AnimalReportsPage';
import MedicalReportsPage from './pages/reports/MedicalReportsPage';
import VaccinationReportsPage from './pages/reports/VaccinationReportsPage';
import EmployeeReportsPage from './pages/reports/EmployeeReportsPage';
import AuditLogPage from './pages/admin/AuditLogPage';

const App: React.FC = () => {
  return (
    <ErrorBoundary>
      <ConfigProvider
      locale={enUS}
      theme={{
        token: {
          colorPrimary: '#1677ff',
        },
      }}
    >
      <Router basename={import.meta.env.BASE_URL}>
        <Routes>
          <Route path="/login" element={<LoginPage />} />
          <Route path="/register" element={<RegisterPage />} />
          <Route path="/reset-password" element={<ResetPasswordPage />} />
          <Route
            path="/dashboard"
            element={
              <ProtectedRoute>
                <AppLayout />
              </ProtectedRoute>
            }
          >
            <Route index element={<DashboardPage />} />

            {/* Feed Management */}
            <Route path="feed/types" element={<FeedTypesPage />} />
            <Route path="feed/records" element={<FeedRecordsPage />} />
            <Route path="feed/diet-plans" element={<DietPlansPage />} />
            <Route path="feed/schedules" element={<FeedingSchedulesPage />} />
            <Route path="feed/tasks" element={<FeedingTasksPage />} />
            <Route path="feed/reports" element={<FeedReportsPage />} />

            {/* Tasks */}
            <Route path="tasks" element={<TasksPage />} />

            {/* HR */}
            <Route path="hr/employees" element={<EmployeesPage />} />
            <Route path="hr/departments-roles" element={<DepartmentsRolesPage />} />
            <Route path="hr/salary-payments" element={<SalaryPaymentsPage />} />
            <Route path="hr/attendance" element={<AttendancePage />} />
            <Route path="hr/performance" element={<PerformanceReviewsPage />} />

            {/* Finance */}
            <Route path="finance/expenses" element={<ExpensesPage />} />
            <Route path="finance/incomes" element={<IncomesPage />} />
            <Route path="finance/reports" element={<FinanceReportsPage />} />
            <Route path="finance/categories" element={<CategoriesPage />} />

            {/* Health */}
            <Route path="health/medical" element={<MedicalRecordsPage />} />
            <Route path="health/medicines" element={<MedicinesPage />} />
            <Route path="health/medicines/alerts" element={<MedicineAlertsPage />} />
            <Route path="health/vaccines" element={<VaccineTypesPage />} />
            <Route path="health/vaccinations" element={<VaccinationRecordsPage />} />
            <Route path="health/vaccinations/schedule" element={<VaccinationSchedulePage />} />
            <Route path="health/weight-schedules" element={<WeightCheckSchedulePage />} />
            <Route path="health/costs" element={<VetCostsPage />} />

            {/* Animals */}
            <Route path="animals" element={<AnimalsPage />} />
            <Route path="animals/:id" element={<AnimalDetailPage />} />

            {/* Inventory */}
            <Route path="inventory/items" element={<InventoryItemsPage />} />
            <Route path="inventory/movements" element={<InventoryMovementsPage />} />
            <Route path="inventory/suppliers" element={<SuppliersPage />} />
            <Route path="inventory/customers" element={<CustomersPage />} />
            <Route path="inventory/reports" element={<InventoryReportsPage />} />

            {/* Reports */}
            <Route path="reports/animals" element={<AnimalReportsPage />} />
            <Route path="reports/financial" element={<FinanceReportsPage />} />
            <Route path="reports/feed" element={<FeedReportsPage />} />
            <Route path="reports/breeding" element={<BreedingReportsPage />} />
            <Route path="reports/medical" element={<MedicalReportsPage />} />
            <Route path="reports/vaccination" element={<VaccinationReportsPage />} />
            <Route path="reports/employees" element={<EmployeeReportsPage />} />

            {/* Breeding */}
            <Route path="breeding/records" element={<BreedingRecordsPage />} />
            <Route path="breeding/gestation" element={<GestationPage />} />
            <Route path="breeding/births" element={<BirthRecordingPage />} />
            <Route path="breeding/lineage" element={<LineagePage />} />
            <Route path="breeding/reports" element={<BreedingReportsPage />} />

            {/* Admin */}
            <Route path="admin/audit-log" element={<AuditLogPage />} />
          </Route>
          <Route path="*" element={<Navigate to="/login" replace />} />
        </Routes>
      </Router>
    </ConfigProvider>
    </ErrorBoundary>
  );
};

export default App;
