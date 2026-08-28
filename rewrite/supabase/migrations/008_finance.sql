-- ============================================================================
-- Migration 008: Finance tables
-- ============================================================================

-- ─── Expense Categories ────────────────────────────────────────────────
CREATE TABLE expense_categories (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    farm_id UUID NOT NULL REFERENCES farms(id) ON DELETE CASCADE,
    name TEXT NOT NULL,
    description TEXT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    modified_at TIMESTAMPTZ,
    created_by UUID,
    modified_by UUID,
    UNIQUE(farm_id, name)
);

CREATE INDEX idx_expense_categories_farm_id ON expense_categories(farm_id);

-- ─── Payment Methods ───────────────────────────────────────────────────
CREATE TABLE payment_methods (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    farm_id UUID NOT NULL REFERENCES farms(id) ON DELETE CASCADE,
    name TEXT NOT NULL,
    description TEXT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    modified_at TIMESTAMPTZ,
    created_by UUID,
    modified_by UUID,
    UNIQUE(farm_id, name)
);

CREATE INDEX idx_payment_methods_farm_id ON payment_methods(farm_id);

-- ─── Expenses ──────────────────────────────────────────────────────────
CREATE TABLE expenses (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    farm_id UUID NOT NULL REFERENCES farms(id) ON DELETE CASCADE,
    expense_date TIMESTAMPTZ NOT NULL DEFAULT now(),
    amount DECIMAL(14,2) NOT NULL,
    expense_category_id UUID NOT NULL REFERENCES expense_categories(id) ON DELETE RESTRICT,
    payment_method_id UUID NOT NULL REFERENCES payment_methods(id) ON DELETE RESTRICT,
    animal_id UUID REFERENCES animals(id) ON DELETE SET NULL,
    location_id UUID REFERENCES locations(id) ON DELETE SET NULL,
    description TEXT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    modified_at TIMESTAMPTZ,
    created_by UUID,
    modified_by UUID
);

CREATE INDEX idx_expenses_farm_id ON expenses(farm_id);
CREATE INDEX idx_expenses_expense_category_id ON expenses(expense_category_id);
CREATE INDEX idx_expenses_expense_date ON expenses(expense_date);

-- ─── Income Categories ─────────────────────────────────────────────────
CREATE TABLE income_categories (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    farm_id UUID NOT NULL REFERENCES farms(id) ON DELETE CASCADE,
    name TEXT NOT NULL,
    description TEXT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    modified_at TIMESTAMPTZ,
    created_by UUID,
    modified_by UUID,
    UNIQUE(farm_id, name)
);

CREATE INDEX idx_income_categories_farm_id ON income_categories(farm_id);

-- ─── Income Records ────────────────────────────────────────────────────
CREATE TABLE income_records (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    farm_id UUID NOT NULL REFERENCES farms(id) ON DELETE CASCADE,
    income_date TIMESTAMPTZ NOT NULL DEFAULT now(),
    amount DECIMAL(14,2) NOT NULL,
    income_category_id UUID NOT NULL REFERENCES income_categories(id) ON DELETE RESTRICT,
    payment_method_id UUID NOT NULL REFERENCES payment_methods(id) ON DELETE RESTRICT,
    animal_id UUID REFERENCES animals(id) ON DELETE SET NULL,
    location_id UUID REFERENCES locations(id) ON DELETE SET NULL,
    description TEXT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    modified_at TIMESTAMPTZ,
    created_by UUID,
    modified_by UUID
);

CREATE INDEX idx_income_records_farm_id ON income_records(farm_id);
CREATE INDEX idx_income_records_income_category_id ON income_records(income_category_id);
CREATE INDEX idx_income_records_income_date ON income_records(income_date);
