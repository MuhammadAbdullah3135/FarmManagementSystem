-- ============================================================================
-- Migration 010: Inventory, suppliers, customers, tasks, audit log
-- ============================================================================

-- ─── Inventory Items ──────────────────────────────────────────────────
CREATE TABLE inventory_items (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    farm_id UUID NOT NULL REFERENCES farms(id) ON DELETE CASCADE,
    name TEXT NOT NULL,
    category TEXT,
    unit TEXT NOT NULL DEFAULT '',
    quantity DECIMAL(12,2) NOT NULL DEFAULT 0,
    reorder_level DECIMAL(12,2) NOT NULL DEFAULT 0,
    unit_cost DECIMAL(12,2) NOT NULL DEFAULT 0,
    location TEXT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    modified_at TIMESTAMPTZ,
    created_by UUID,
    modified_by UUID
);

CREATE INDEX idx_inventory_items_farm_id ON inventory_items(farm_id);

-- ─── Stock Movements ──────────────────────────────────────────────────
CREATE TABLE stock_movements (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    farm_id UUID NOT NULL REFERENCES farms(id) ON DELETE CASCADE,
    inventory_item_id UUID NOT NULL REFERENCES inventory_items(id) ON DELETE CASCADE,
    movement_type inventory_movement_type NOT NULL,
    quantity DECIMAL(12,2) NOT NULL,
    movement_date TIMESTAMPTZ NOT NULL DEFAULT now(),
    reason TEXT,
    performed_by UUID,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    modified_at TIMESTAMPTZ,
    created_by UUID,
    modified_by UUID
);

CREATE INDEX idx_stock_movements_farm_id ON stock_movements(farm_id);
CREATE INDEX idx_stock_movements_inventory_item_id ON stock_movements(inventory_item_id);
CREATE INDEX idx_stock_movements_movement_date ON stock_movements(movement_date);

-- ─── Suppliers ────────────────────────────────────────────────────────
CREATE TABLE suppliers (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    farm_id UUID NOT NULL REFERENCES farms(id) ON DELETE CASCADE,
    name TEXT NOT NULL,
    contact_info TEXT,
    products_supplied TEXT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    modified_at TIMESTAMPTZ,
    created_by UUID,
    modified_by UUID
);

CREATE INDEX idx_suppliers_farm_id ON suppliers(farm_id);

-- ─── Supplier Purchases ──────────────────────────────────────────────
CREATE TABLE supplier_purchases (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    farm_id UUID NOT NULL REFERENCES farms(id) ON DELETE CASCADE,
    supplier_id UUID NOT NULL REFERENCES suppliers(id) ON DELETE RESTRICT,
    inventory_item_id UUID NOT NULL REFERENCES inventory_items(id) ON DELETE RESTRICT,
    quantity DECIMAL(12,2) NOT NULL,
    total_cost DECIMAL(14,2) NOT NULL,
    purchase_date TIMESTAMPTZ NOT NULL DEFAULT now(),
    stock_movement_id UUID REFERENCES stock_movements(id) ON DELETE SET NULL,
    expense_id UUID REFERENCES expenses(id) ON DELETE SET NULL,
    notes TEXT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    modified_at TIMESTAMPTZ,
    created_by UUID,
    modified_by UUID
);

CREATE INDEX idx_supplier_purchases_farm_id ON supplier_purchases(farm_id);
CREATE INDEX idx_supplier_purchases_supplier_id ON supplier_purchases(supplier_id);

-- ─── Customers ────────────────────────────────────────────────────────
CREATE TABLE customers (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    farm_id UUID NOT NULL REFERENCES farms(id) ON DELETE CASCADE,
    name TEXT NOT NULL,
    contact_info TEXT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    modified_at TIMESTAMPTZ,
    created_by UUID,
    modified_by UUID
);

CREATE INDEX idx_customers_farm_id ON customers(farm_id);

-- ─── Customer Sales ───────────────────────────────────────────────────
CREATE TABLE customer_sales (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    farm_id UUID NOT NULL REFERENCES farms(id) ON DELETE CASCADE,
    customer_id UUID NOT NULL REFERENCES customers(id) ON DELETE RESTRICT,
    inventory_item_id UUID NOT NULL REFERENCES inventory_items(id) ON DELETE RESTRICT,
    quantity DECIMAL(12,2) NOT NULL,
    total_amount DECIMAL(14,2) NOT NULL,
    sale_date TIMESTAMPTZ NOT NULL DEFAULT now(),
    stock_movement_id UUID REFERENCES stock_movements(id) ON DELETE SET NULL,
    income_record_id UUID REFERENCES income_records(id) ON DELETE SET NULL,
    notes TEXT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    modified_at TIMESTAMPTZ,
    created_by UUID,
    modified_by UUID
);

CREATE INDEX idx_customer_sales_farm_id ON customer_sales(farm_id);
CREATE INDEX idx_customer_sales_customer_id ON customer_sales(customer_id);

-- ─── Farm Tasks ───────────────────────────────────────────────────────
CREATE TABLE farm_tasks (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    farm_id UUID NOT NULL REFERENCES farms(id) ON DELETE CASCADE,
    title TEXT NOT NULL,
    description TEXT,
    priority farm_task_priority NOT NULL DEFAULT 'Medium',
    status farm_task_status NOT NULL DEFAULT 'Pending',
    due_date TIMESTAMPTZ NOT NULL,
    started_at TIMESTAMPTZ,
    completed_at TIMESTAMPTZ,
    completed_by UUID,
    completion_notes TEXT,
    cancel_reason TEXT,
    assigned_employee_id UUID REFERENCES employees(id) ON DELETE SET NULL,
    animal_id UUID REFERENCES animals(id) ON DELETE SET NULL,
    location_id UUID REFERENCES locations(id) ON DELETE SET NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    modified_at TIMESTAMPTZ,
    created_by UUID,
    modified_by UUID
);

CREATE INDEX idx_farm_tasks_farm_id ON farm_tasks(farm_id);
CREATE INDEX idx_farm_tasks_status ON farm_tasks(status);
CREATE INDEX idx_farm_tasks_due_date ON farm_tasks(due_date);
CREATE INDEX idx_farm_tasks_assigned_employee_id ON farm_tasks(assigned_employee_id);

-- ─── Audit Log ────────────────────────────────────────────────────────
-- Append-only. No UPDATE or DELETE policies.
CREATE TABLE audit_logs (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    farm_id UUID REFERENCES farms(id) ON DELETE SET NULL,
    user_id UUID REFERENCES users(id) ON DELETE SET NULL,
    user_email TEXT,
    entity_type TEXT NOT NULL,
    entity_id TEXT NOT NULL,
    action audit_action NOT NULL,
    old_values JSONB,
    new_values JSONB,
    timestamp TIMESTAMPTZ NOT NULL DEFAULT now(),
    ip_address TEXT
);

CREATE INDEX idx_audit_logs_farm_id ON audit_logs(farm_id);
CREATE INDEX idx_audit_logs_user_id ON audit_logs(user_id);
CREATE INDEX idx_audit_logs_entity_type_entity_id ON audit_logs(entity_type, entity_id);
CREATE INDEX idx_audit_logs_timestamp ON audit_logs(timestamp);
CREATE INDEX idx_audit_logs_action ON audit_logs(action);
CREATE INDEX idx_audit_logs_farm_timestamp ON audit_logs(farm_id, timestamp DESC);
