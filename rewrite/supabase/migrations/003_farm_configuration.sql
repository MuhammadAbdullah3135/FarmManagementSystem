-- ============================================================================
-- Migration 003: Farm configuration tables
-- ============================================================================

-- ─── Animal Types ──────────────────────────────────────────────────────
CREATE TABLE animal_types (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    farm_id UUID NOT NULL REFERENCES farms(id) ON DELETE CASCADE,
    name TEXT NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    modified_at TIMESTAMPTZ,
    UNIQUE(farm_id, name)
);

CREATE INDEX idx_animal_types_farm_id ON animal_types(farm_id);

-- ─── Breeds ────────────────────────────────────────────────────────────
CREATE TABLE breeds (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    animal_type_id UUID NOT NULL REFERENCES animal_types(id) ON DELETE CASCADE,
    name TEXT NOT NULL,
    average_gestation_days INT NOT NULL DEFAULT 283,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    modified_at TIMESTAMPTZ,
    UNIQUE(animal_type_id, name)
);

CREATE INDEX idx_breeds_animal_type_id ON breeds(animal_type_id);

-- ─── Sex Options ───────────────────────────────────────────────────────
CREATE TABLE sex_options (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    farm_id UUID NOT NULL REFERENCES farms(id) ON DELETE CASCADE,
    value TEXT NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    modified_at TIMESTAMPTZ,
    UNIQUE(farm_id, value)
);

CREATE INDEX idx_sex_options_farm_id ON sex_options(farm_id);

-- ─── Age Categories ────────────────────────────────────────────────────
CREATE TABLE age_categories (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    farm_id UUID NOT NULL REFERENCES farms(id) ON DELETE CASCADE,
    name TEXT NOT NULL,
    min_days INT NOT NULL DEFAULT 0,
    max_days INT NOT NULL DEFAULT 99999,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    modified_at TIMESTAMPTZ,
    UNIQUE(farm_id, name)
);

CREATE INDEX idx_age_categories_farm_id ON age_categories(farm_id);

-- ─── Animal Statuses ───────────────────────────────────────────────────
CREATE TABLE animal_statuses (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    farm_id UUID NOT NULL REFERENCES farms(id) ON DELETE CASCADE,
    name TEXT NOT NULL,
    is_active BOOLEAN NOT NULL DEFAULT true,
    category animal_status_category NOT NULL DEFAULT 'Active',
    is_system_defined BOOLEAN NOT NULL DEFAULT false,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    modified_at TIMESTAMPTZ,
    UNIQUE(farm_id, name)
);

CREATE INDEX idx_animal_statuses_farm_id ON animal_statuses(farm_id);

-- ─── Identification Types ──────────────────────────────────────────────
CREATE TABLE identification_types (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    farm_id UUID NOT NULL REFERENCES farms(id) ON DELETE CASCADE,
    name TEXT NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    modified_at TIMESTAMPTZ,
    UNIQUE(farm_id, name)
);

CREATE INDEX idx_identification_types_farm_id ON identification_types(farm_id);

-- ─── Location Types ────────────────────────────────────────────────────
CREATE TABLE location_types (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    farm_id UUID NOT NULL REFERENCES farms(id) ON DELETE CASCADE,
    name TEXT NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    modified_at TIMESTAMPTZ,
    UNIQUE(farm_id, name)
);

CREATE INDEX idx_location_types_farm_id ON location_types(farm_id);

-- ─── Locations ─────────────────────────────────────────────────────────
CREATE TABLE locations (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    farm_id UUID NOT NULL REFERENCES farms(id) ON DELETE CASCADE,
    name TEXT NOT NULL,
    location_type_id UUID NOT NULL REFERENCES location_types(id) ON DELETE RESTRICT,
    parent_location_id UUID REFERENCES locations(id) ON DELETE SET NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    modified_at TIMESTAMPTZ,
    UNIQUE(farm_id, name)
);

CREATE INDEX idx_locations_farm_id ON locations(farm_id);
CREATE INDEX idx_locations_location_type_id ON locations(location_type_id);
CREATE INDEX idx_locations_parent_location_id ON locations(parent_location_id);

-- ─── Custom Field Definitions ──────────────────────────────────────────
CREATE TABLE custom_field_definitions (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    farm_id UUID NOT NULL REFERENCES farms(id) ON DELETE CASCADE,
    field_name TEXT NOT NULL,
    field_type custom_field_type NOT NULL DEFAULT 'String',
    is_required BOOLEAN NOT NULL DEFAULT false,
    options JSONB,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    modified_at TIMESTAMPTZ,
    UNIQUE(farm_id, field_name)
);

CREATE INDEX idx_custom_field_definitions_farm_id ON custom_field_definitions(farm_id);
