-- ============================================================================
-- Migration 006: Health management tables
-- ============================================================================

-- ─── Medicines ─────────────────────────────────────────────────────────
CREATE TABLE medicines (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    farm_id UUID NOT NULL REFERENCES farms(id) ON DELETE CASCADE,
    name TEXT NOT NULL,
    description TEXT,
    unit TEXT NOT NULL DEFAULT '',
    low_stock_threshold INT NOT NULL DEFAULT 10,
    expiring_soon_days INT NOT NULL DEFAULT 30,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    modified_at TIMESTAMPTZ,
    created_by UUID,
    modified_by UUID
);

CREATE INDEX idx_medicines_farm_id ON medicines(farm_id);

-- ─── Medicine Stock Batches ────────────────────────────────────────────
CREATE TABLE medicine_stocks (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    farm_id UUID NOT NULL REFERENCES farms(id) ON DELETE CASCADE,
    medicine_id UUID NOT NULL REFERENCES medicines(id) ON DELETE CASCADE,
    batch_number TEXT NOT NULL,
    quantity INT NOT NULL DEFAULT 0,
    unit_cost DECIMAL(12,2) NOT NULL DEFAULT 0,
    expiry_date TIMESTAMPTZ NOT NULL,
    supplier TEXT,
    date_received TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    modified_at TIMESTAMPTZ,
    created_by UUID,
    modified_by UUID
);

CREATE INDEX idx_medicine_stocks_farm_id ON medicine_stocks(farm_id);
CREATE INDEX idx_medicine_stocks_medicine_id ON medicine_stocks(medicine_id);
CREATE INDEX idx_medicine_stocks_expiry_date ON medicine_stocks(expiry_date);

-- ─── Medicine Usages ───────────────────────────────────────────────────
CREATE TABLE medicine_usages (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    farm_id UUID NOT NULL REFERENCES farms(id) ON DELETE CASCADE,
    medicine_id UUID NOT NULL REFERENCES medicines(id) ON DELETE CASCADE,
    medicine_stock_id UUID NOT NULL REFERENCES medicine_stocks(id) ON DELETE RESTRICT,
    medical_record_id UUID,
    quantity_used INT NOT NULL DEFAULT 1,
    date_used TIMESTAMPTZ NOT NULL DEFAULT now(),
    notes TEXT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    modified_at TIMESTAMPTZ,
    created_by UUID,
    modified_by UUID
);

CREATE INDEX idx_medicine_usages_farm_id ON medicine_usages(farm_id);
CREATE INDEX idx_medicine_usages_medicine_id ON medicine_usages(medicine_id);

-- ─── Medical Records ───────────────────────────────────────────────────
CREATE TABLE medical_records (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    farm_id UUID NOT NULL REFERENCES farms(id) ON DELETE CASCADE,
    animal_id UUID NOT NULL REFERENCES animals(id) ON DELETE CASCADE,
    symptoms TEXT NOT NULL DEFAULT '',
    diagnosis TEXT,
    treatment TEXT,
    medicine_used TEXT,
    dosage TEXT,
    vet_name TEXT,
    cost DECIMAL(12,2) NOT NULL DEFAULT 0,
    date_recorded TIMESTAMPTZ NOT NULL DEFAULT now(),
    follow_up_date TIMESTAMPTZ,
    status medical_record_status NOT NULL DEFAULT 'Open',
    notes TEXT,
    follow_up_task_id UUID,
    expense_id UUID,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    modified_at TIMESTAMPTZ,
    created_by UUID,
    modified_by UUID
);

CREATE INDEX idx_medical_records_farm_id ON medical_records(farm_id);
CREATE INDEX idx_medical_records_animal_id ON medical_records(animal_id);
CREATE INDEX idx_medical_records_date_recorded ON medical_records(date_recorded);
CREATE INDEX idx_medical_records_status ON medical_records(status);

-- Add FK for medicine_usages.medical_record_id after medical_records is created
ALTER TABLE medicine_usages
    ADD CONSTRAINT fk_medicine_usages_medical_record
    FOREIGN KEY (medical_record_id) REFERENCES medical_records(id) ON DELETE SET NULL;

-- ─── Vaccine Types ─────────────────────────────────────────────────────
CREATE TABLE vaccine_types (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    farm_id UUID NOT NULL REFERENCES farms(id) ON DELETE CASCADE,
    name TEXT NOT NULL,
    default_dosage TEXT,
    notes TEXT,
    linked_medicine_id UUID REFERENCES medicines(id) ON DELETE SET NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    modified_at TIMESTAMPTZ,
    created_by UUID,
    modified_by UUID
);

CREATE INDEX idx_vaccine_types_farm_id ON vaccine_types(farm_id);

-- ─── Vaccination Records ───────────────────────────────────────────────
CREATE TABLE vaccination_records (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    farm_id UUID NOT NULL REFERENCES farms(id) ON DELETE CASCADE,
    animal_id UUID NOT NULL REFERENCES animals(id) ON DELETE CASCADE,
    vaccine_type_id UUID NOT NULL REFERENCES vaccine_types(id) ON DELETE RESTRICT,
    date_given TIMESTAMPTZ NOT NULL DEFAULT now(),
    vet_name TEXT,
    batch_number TEXT,
    cost DECIMAL(12,2) NOT NULL DEFAULT 0,
    notes TEXT,
    expense_id UUID,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    modified_at TIMESTAMPTZ,
    created_by UUID,
    modified_by UUID
);

CREATE INDEX idx_vaccination_records_farm_id ON vaccination_records(farm_id);
CREATE INDEX idx_vaccination_records_animal_id ON vaccination_records(animal_id);
CREATE INDEX idx_vaccination_records_vaccine_type_id ON vaccination_records(vaccine_type_id);
CREATE INDEX idx_vaccination_records_date_given ON vaccination_records(date_given);

-- ─── Vaccination Schedules ─────────────────────────────────────────────
CREATE TABLE vaccination_schedules (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    farm_id UUID NOT NULL REFERENCES farms(id) ON DELETE CASCADE,
    vaccine_type_id UUID NOT NULL REFERENCES vaccine_types(id) ON DELETE CASCADE,
    animal_type_id UUID REFERENCES animal_types(id) ON DELETE SET NULL,
    breed_id UUID REFERENCES breeds(id) ON DELETE SET NULL,
    recurrence_days INT NOT NULL DEFAULT 365,
    is_active BOOLEAN NOT NULL DEFAULT true,
    notes TEXT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    modified_at TIMESTAMPTZ,
    created_by UUID,
    modified_by UUID
);

CREATE INDEX idx_vaccination_schedules_farm_id ON vaccination_schedules(farm_id);
CREATE INDEX idx_vaccination_schedules_vaccine_type_id ON vaccination_schedules(vaccine_type_id);
