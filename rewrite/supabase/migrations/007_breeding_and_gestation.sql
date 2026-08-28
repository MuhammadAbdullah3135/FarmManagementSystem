-- ============================================================================
-- Migration 007: Breeding, gestation, and birth tables
-- ============================================================================

-- ─── Breeding Records ──────────────────────────────────────────────────
CREATE TABLE breeding_records (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    farm_id UUID NOT NULL REFERENCES farms(id) ON DELETE CASCADE,
    sire_id UUID NOT NULL REFERENCES animals(id) ON DELETE RESTRICT,
    dam_id UUID NOT NULL REFERENCES animals(id) ON DELETE RESTRICT,
    breeding_date TIMESTAMPTZ NOT NULL DEFAULT now(),
    method breeding_method NOT NULL DEFAULT 'Natural',
    vet_name TEXT,
    result breeding_result NOT NULL DEFAULT 'Pending',
    notes TEXT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    modified_at TIMESTAMPTZ,
    created_by UUID,
    modified_by UUID
);

CREATE INDEX idx_breeding_records_farm_id ON breeding_records(farm_id);
CREATE INDEX idx_breeding_records_sire_id ON breeding_records(sire_id);
CREATE INDEX idx_breeding_records_dam_id ON breeding_records(dam_id);
CREATE INDEX idx_breeding_records_breeding_date ON breeding_records(breeding_date);

-- ─── Gestation Records ────────────────────────────────────────────────
CREATE TABLE gestation_records (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    farm_id UUID NOT NULL REFERENCES farms(id) ON DELETE CASCADE,
    breeding_record_id UUID NOT NULL REFERENCES breeding_records(id) ON DELETE CASCADE,
    animal_id UUID NOT NULL REFERENCES animals(id) ON DELETE RESTRICT,
    confirmed_date TIMESTAMPTZ,
    expected_delivery_date TIMESTAMPTZ NOT NULL,
    current_stage gestation_stage NOT NULL DEFAULT 'Early',
    health_check_notes TEXT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    modified_at TIMESTAMPTZ,
    created_by UUID,
    modified_by UUID
);

CREATE INDEX idx_gestation_records_farm_id ON gestation_records(farm_id);
CREATE INDEX idx_gestation_records_breeding_record_id ON gestation_records(breeding_record_id);
CREATE INDEX idx_gestation_records_animal_id ON gestation_records(animal_id);
CREATE INDEX idx_gestation_records_expected_delivery ON gestation_records(expected_delivery_date);

-- ─── Gestation Health Checks ───────────────────────────────────────────
CREATE TABLE gestation_health_checks (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    farm_id UUID NOT NULL REFERENCES farms(id) ON DELETE CASCADE,
    gestation_record_id UUID NOT NULL REFERENCES gestation_records(id) ON DELETE CASCADE,
    check_date TIMESTAMPTZ NOT NULL DEFAULT now(),
    notes TEXT,
    performed_by TEXT,
    weight_kg DECIMAL(10,2),
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    modified_at TIMESTAMPTZ,
    created_by UUID,
    modified_by UUID
);

CREATE INDEX idx_gestation_health_checks_gestation_record_id ON gestation_health_checks(gestation_record_id);

-- ─── Birth Records ────────────────────────────────────────────────────
CREATE TABLE birth_records (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    farm_id UUID NOT NULL REFERENCES farms(id) ON DELETE CASCADE,
    dam_id UUID NOT NULL REFERENCES animals(id) ON DELETE RESTRICT,
    gestation_record_id UUID REFERENCES gestation_records(id) ON DELETE SET NULL,
    breeding_record_id UUID REFERENCES breeding_records(id) ON DELETE SET NULL,
    birth_date TIMESTAMPTZ NOT NULL DEFAULT now(),
    offspring_count INT NOT NULL DEFAULT 1,
    notes TEXT,
    vet_name TEXT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    modified_at TIMESTAMPTZ,
    created_by UUID,
    modified_by UUID
);

CREATE INDEX idx_birth_records_farm_id ON birth_records(farm_id);
CREATE INDEX idx_birth_records_dam_id ON birth_records(dam_id);
CREATE INDEX idx_birth_records_birth_date ON birth_records(birth_date);

-- ─── Birth Offspring ──────────────────────────────────────────────────
CREATE TABLE birth_offspring (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    farm_id UUID NOT NULL REFERENCES farms(id) ON DELETE CASCADE,
    birth_record_id UUID NOT NULL REFERENCES birth_records(id) ON DELETE CASCADE,
    offspring_animal_id UUID REFERENCES animals(id) ON DELETE SET NULL,
    tag_number TEXT NOT NULL,
    name TEXT,
    sex_option_id UUID NOT NULL REFERENCES sex_options(id) ON DELETE RESTRICT,
    outcome birth_outcome NOT NULL DEFAULT 'Alive',
    birth_weight_kg DECIMAL(10,2),
    notes TEXT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    modified_at TIMESTAMPTZ
);

CREATE INDEX idx_birth_offspring_farm_id ON birth_offspring(farm_id);
CREATE INDEX idx_birth_offspring_birth_record_id ON birth_offspring(birth_record_id);
