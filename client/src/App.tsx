import React from 'react';
import { BrowserRouter as Router, Routes, Route, Navigate } from 'react-router-dom';
import { ConfigProvider } from 'antd';
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
import TasksPage from './pages/TasksPage';

const App: React.FC = () => {
  return (
    <ConfigProvider
      theme={{
        token: {
          colorPrimary: '#1677ff',
        },
      }}
    >
      <Router>
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
          </Route>
          <Route path="*" element={<Navigate to="/login" replace />} />
        </Routes>
      </Router>
    </ConfigProvider>
  );
};

export default App;
