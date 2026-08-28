-- ============================================================================
-- Migration 009: HR and Employee tables
-- ============================================================================

-- ─── Departments ───────────────────────────────────────────────────────
CREATE TABLE departments (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    farm_id UUID NOT NULL REFERENCES farms(id) ON DELETE CASCADE,
    name TEXT NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    modified_at TIMESTAMPTZ,
    UNIQUE(farm_id, name)
);

CREATE INDEX idx_departments_farm_id ON departments(farm_id);

-- ─── Employee Roles ────────────────────────────────────────────────────
CREATE TABLE employee_roles (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    farm_id UUID NOT NULL REFERENCES farms(id) ON DELETE CASCADE,
    name TEXT NOT NULL,
    description TEXT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    modified_at TIMESTAMPTZ,
    UNIQUE(farm_id, name)
);

CREATE INDEX idx_employee_roles_farm_id ON employee_roles(farm_id);

-- ─── Employees ─────────────────────────────────────────────────────────
CREATE TABLE employees (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    farm_id UUID NOT NULL REFERENCES farms(id) ON DELETE CASCADE,
    first_name TEXT NOT NULL,
    last_name TEXT NOT NULL,
    phone TEXT,
    email TEXT,
    address TEXT,
    department_id UUID REFERENCES departments(id) ON DELETE SET NULL,
    employee_role_id UUID REFERENCES employee_roles(id) ON DELETE SET NULL,
    salary_type salary_type NOT NULL DEFAULT 'Monthly',
    salary_rate DECIMAL(12,2) NOT NULL DEFAULT 0,
    hire_date DATE,
    is_active BOOLEAN NOT NULL DEFAULT true,
    notes TEXT,
    is_deleted BOOLEAN NOT NULL DEFAULT false,
    deleted_at TIMESTAMPTZ,
    deleted_by UUID,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    modified_at TIMESTAMPTZ,
    created_by UUID,
    modified_by UUID
);

CREATE INDEX idx_employees_farm_id ON employees(farm_id);
CREATE INDEX idx_employees_department_id ON employees(department_id);
CREATE INDEX idx_employees_employee_role_id ON employees(employee_role_id);
CREATE INDEX idx_employees_not_deleted ON employees(farm_id) WHERE NOT is_deleted;

-- ─── Attendance Records ────────────────────────────────────────────────
CREATE TABLE attendance_records (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    farm_id UUID NOT NULL REFERENCES farms(id) ON DELETE CASCADE,
    employee_id UUID NOT NULL REFERENCES employees(id) ON DELETE CASCADE,
    date DATE NOT NULL,
    status attendance_status NOT NULL DEFAULT 'Present',
    check_in_at TIMESTAMPTZ,
    check_out_at TIMESTAMPTZ,
    notes TEXT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    modified_at TIMESTAMPTZ,
    created_by UUID,
    modified_by UUID,
    UNIQUE(employee_id, date)
);

CREATE INDEX idx_attendance_records_farm_id ON attendance_records(farm_id);
CREATE INDEX idx_attendance_records_employee_id ON attendance_records(employee_id);
CREATE INDEX idx_attendance_records_date ON attendance_records(date);

-- ─── Salary Payments ──────────────────────────────────────────────────
CREATE TABLE salary_payments (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    farm_id UUID NOT NULL REFERENCES farms(id) ON DELETE CASCADE,
    employee_id UUID NOT NULL REFERENCES employees(id) ON DELETE CASCADE,
    amount DECIMAL(12,2) NOT NULL,
    payment_date TIMESTAMPTZ NOT NULL DEFAULT now(),
    salary_type salary_type NOT NULL DEFAULT 'Monthly',
    notes TEXT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    modified_at TIMESTAMPTZ,
    created_by UUID,
    modified_by UUID
);

CREATE INDEX idx_salary_payments_farm_id ON salary_payments(farm_id);
CREATE INDEX idx_salary_payments_employee_id ON salary_payments(employee_id);
CREATE INDEX idx_salary_payments_payment_date ON salary_payments(payment_date);

-- ─── Performance Reviews ──────────────────────────────────────────────
CREATE TABLE performance_reviews (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    farm_id UUID NOT NULL REFERENCES farms(id) ON DELETE CASCADE,
    employee_id UUID NOT NULL REFERENCES employees(id) ON DELETE CASCADE,
    rating INT NOT NULL CHECK (rating BETWEEN 1 AND 5),
    review_date TIMESTAMPTZ NOT NULL DEFAULT now(),
    period_start TIMESTAMPTZ,
    period_end TIMESTAMPTZ,
    strengths TEXT,
    areas_for_improvement TEXT,
    comments TEXT,
    reviewed_by UUID,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    modified_at TIMESTAMPTZ,
    created_by UUID,
    modified_by UUID
);

CREATE INDEX idx_performance_reviews_farm_id ON performance_reviews(farm_id);
CREATE INDEX idx_performance_reviews_employee_id ON performance_reviews(employee_id);
