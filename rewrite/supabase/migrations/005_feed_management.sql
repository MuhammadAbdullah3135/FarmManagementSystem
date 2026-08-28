-- ============================================================================
-- Migration 005: Feed management tables
-- ============================================================================

-- ─── Feed Types ────────────────────────────────────────────────────────
CREATE TABLE feed_types (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    farm_id UUID NOT NULL REFERENCES farms(id) ON DELETE CASCADE,
    name TEXT NOT NULL,
    category feed_category NOT NULL DEFAULT 'Other',
    unit feed_unit NOT NULL DEFAULT 'Kilogram',
    cost_per_unit DECIMAL(12,2) NOT NULL DEFAULT 0,
    notes TEXT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    modified_at TIMESTAMPTZ,
    created_by UUID,
    modified_by UUID,
    UNIQUE(farm_id, name)
);

CREATE INDEX idx_feed_types_farm_id ON feed_types(farm_id);

-- ─── Feed Stock Movements ──────────────────────────────────────────────
CREATE TABLE feed_stock_movements (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    farm_id UUID NOT NULL REFERENCES farms(id) ON DELETE CASCADE,
    feed_type_id UUID NOT NULL REFERENCES feed_types(id) ON DELETE CASCADE,
    movement_type stock_movement_type NOT NULL,
    quantity DECIMAL(12,2) NOT NULL,
    unit_cost DECIMAL(12,2),
    total_cost DECIMAL(14,2),
    supplier TEXT,
    notes TEXT,
    movement_date TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    modified_at TIMESTAMPTZ,
    created_by UUID,
    modified_by UUID
);

CREATE INDEX idx_feed_stock_movements_farm_id ON feed_stock_movements(farm_id);
CREATE INDEX idx_feed_stock_movements_feed_type_id ON feed_stock_movements(feed_type_id);
CREATE INDEX idx_feed_stock_movements_movement_date ON feed_stock_movements(movement_date);

-- ─── Diet Plans ────────────────────────────────────────────────────────
CREATE TABLE diet_plans (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    farm_id UUID NOT NULL REFERENCES farms(id) ON DELETE CASCADE,
    name TEXT NOT NULL,
    animal_type_id UUID REFERENCES animal_types(id) ON DELETE SET NULL,
    breed_id UUID REFERENCES breeds(id) ON DELETE SET NULL,
    age_category_id UUID REFERENCES age_categories(id) ON DELETE SET NULL,
    min_weight_kg DECIMAL(10,2),
    max_weight_kg DECIMAL(10,2),
    is_active BOOLEAN NOT NULL DEFAULT true,
    notes TEXT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    modified_at TIMESTAMPTZ,
    created_by UUID,
    modified_by UUID
);

CREATE INDEX idx_diet_plans_farm_id ON diet_plans(farm_id);

-- ─── Diet Plan Items ───────────────────────────────────────────────────
CREATE TABLE diet_plan_items (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    diet_plan_id UUID NOT NULL REFERENCES diet_plans(id) ON DELETE CASCADE,
    feed_type_id UUID NOT NULL REFERENCES feed_types(id) ON DELETE RESTRICT,
    quantity_per_feeding DECIMAL(10,2) NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    modified_at TIMESTAMPTZ,
    created_by UUID,
    modified_by UUID
);

CREATE INDEX idx_diet_plan_items_diet_plan_id ON diet_plan_items(diet_plan_id);

-- ─── Feeding Schedules ────────────────────────────────────────────────
CREATE TABLE feeding_schedules (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    farm_id UUID NOT NULL REFERENCES farms(id) ON DELETE CASCADE,
    diet_plan_id UUID NOT NULL REFERENCES diet_plans(id) ON DELETE CASCADE,
    time_of_day TIME NOT NULL,
    label TEXT,
    is_active BOOLEAN NOT NULL DEFAULT true,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    modified_at TIMESTAMPTZ,
    created_by UUID,
    modified_by UUID
);

CREATE INDEX idx_feeding_schedules_farm_id ON feeding_schedules(farm_id);
CREATE INDEX idx_feeding_schedules_diet_plan_id ON feeding_schedules(diet_plan_id);

-- ─── Feeding Tasks ─────────────────────────────────────────────────────
CREATE TABLE feeding_tasks (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    farm_id UUID NOT NULL REFERENCES farms(id) ON DELETE CASCADE,
    feeding_schedule_id UUID NOT NULL REFERENCES feeding_schedules(id) ON DELETE CASCADE,
    diet_plan_id UUID NOT NULL REFERENCES diet_plans(id) ON DELETE CASCADE,
    task_date DATE NOT NULL,
    time_of_day TIME NOT NULL,
    status feeding_task_status NOT NULL DEFAULT 'Pending',
    target_animal_count INT NOT NULL DEFAULT 0,
    completed_at TIMESTAMPTZ,
    completed_by UUID,
    notes TEXT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    modified_at TIMESTAMPTZ,
    created_by UUID,
    modified_by UUID
);

CREATE INDEX idx_feeding_tasks_farm_id ON feeding_tasks(farm_id);
CREATE INDEX idx_feeding_tasks_task_date ON feeding_tasks(task_date);
CREATE INDEX idx_feeding_tasks_status ON feeding_tasks(status);

-- ─── Feed Records ──────────────────────────────────────────────────────
CREATE TABLE feed_records (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    farm_id UUID NOT NULL REFERENCES farms(id) ON DELETE CASCADE,
    feed_type_id UUID NOT NULL REFERENCES feed_types(id) ON DELETE RESTRICT,
    animal_id UUID REFERENCES animals(id) ON DELETE SET NULL,
    location_id UUID REFERENCES locations(id) ON DELETE SET NULL,
    quantity DECIMAL(12,2) NOT NULL,
    unit_cost DECIMAL(12,2) NOT NULL DEFAULT 0,
    fed_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    notes TEXT,
    stock_movement_id UUID REFERENCES feed_stock_movements(id) ON DELETE SET NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    modified_at TIMESTAMPTZ,
    created_by UUID,
    modified_by UUID
);

CREATE INDEX idx_feed_records_farm_id ON feed_records(farm_id);
CREATE INDEX idx_feed_records_feed_type_id ON feed_records(feed_type_id);
CREATE INDEX idx_feed_records_animal_id ON feed_records(animal_id);
CREATE INDEX idx_feed_records_fed_at ON feed_records(fed_at);
