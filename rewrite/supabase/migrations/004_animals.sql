-- ============================================================================
-- Migration 004: Animal tables
-- ============================================================================

-- ─── Animals ───────────────────────────────────────────────────────────
CREATE TABLE animals (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    farm_id UUID NOT NULL REFERENCES farms(id) ON DELETE CASCADE,
    tag_number TEXT NOT NULL,
    name TEXT,
    animal_type_id UUID NOT NULL REFERENCES animal_types(id) ON DELETE RESTRICT,
    breed_id UUID REFERENCES breeds(id) ON DELETE SET NULL,
    sex_option_id UUID NOT NULL REFERENCES sex_options(id) ON DELETE RESTRICT,
    age_category_id UUID REFERENCES age_categories(id) ON DELETE SET NULL,
    animal_status_id UUID NOT NULL REFERENCES animal_statuses(id) ON DELETE RESTRICT,
    location_id UUID REFERENCES locations(id) ON DELETE SET NULL,
    sire_id UUID REFERENCES animals(id) ON DELETE SET NULL,
    dam_id UUID REFERENCES animals(id) ON DELETE SET NULL,
    date_of_birth DATE,
    acquisition_date DATE,
    notes TEXT,
    is_deleted BOOLEAN NOT NULL DEFAULT false,
    deleted_at TIMESTAMPTZ,
    deleted_by UUID,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    modified_at TIMESTAMPTZ,
    created_by UUID,
    modified_by UUID,
    UNIQUE(farm_id, tag_number)
);

CREATE INDEX idx_animals_farm_id ON animals(farm_id);
CREATE INDEX idx_animals_animal_type_id ON animals(animal_type_id);
CREATE INDEX idx_animals_breed_id ON animals(breed_id);
CREATE INDEX idx_animals_sex_option_id ON animals(sex_option_id);
CREATE INDEX idx_animals_animal_status_id ON animals(animal_status_id);
CREATE INDEX idx_animals_location_id ON animals(location_id);
CREATE INDEX idx_animals_sire_id ON animals(sire_id);
CREATE INDEX idx_animals_dam_id ON animals(dam_id);
CREATE INDEX idx_animals_date_of_birth ON animals(date_of_birth);
CREATE INDEX idx_animals_not_deleted ON animals(farm_id) WHERE NOT is_deleted;

-- ─── Animal Identifications ────────────────────────────────────────────
CREATE TABLE animal_identifications (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    animal_id UUID NOT NULL REFERENCES animals(id) ON DELETE CASCADE,
    farm_id UUID NOT NULL REFERENCES farms(id) ON DELETE CASCADE,
    identification_type_id UUID NOT NULL REFERENCES identification_types(id) ON DELETE RESTRICT,
    value TEXT NOT NULL,
    is_primary BOOLEAN NOT NULL DEFAULT false,
    date_attached TIMESTAMPTZ,
    date_removed TIMESTAMPTZ,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    modified_at TIMESTAMPTZ
);

CREATE INDEX idx_animal_identifications_animal_id ON animal_identifications(animal_id);
CREATE INDEX idx_animal_identifications_farm_id ON animal_identifications(farm_id);

-- ─── Animal Images ─────────────────────────────────────────────────────
CREATE TABLE animal_images (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    animal_id UUID NOT NULL REFERENCES animals(id) ON DELETE CASCADE,
    farm_id UUID NOT NULL REFERENCES farms(id) ON DELETE CASCADE,
    storage_path TEXT NOT NULL,
    file_name TEXT NOT NULL,
    original_file_name TEXT NOT NULL,
    content_type TEXT NOT NULL,
    file_size_bytes BIGINT NOT NULL DEFAULT 0,
    caption TEXT,
    is_primary BOOLEAN NOT NULL DEFAULT false,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    modified_at TIMESTAMPTZ,
    created_by UUID,
    modified_by UUID
);

CREATE INDEX idx_animal_images_animal_id ON animal_images(animal_id);
CREATE INDEX idx_animal_images_farm_id ON animal_images(farm_id);

-- ─── Animal Documents ──────────────────────────────────────────────────
CREATE TABLE animal_documents (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    animal_id UUID NOT NULL REFERENCES animals(id) ON DELETE CASCADE,
    farm_id UUID NOT NULL REFERENCES farms(id) ON DELETE CASCADE,
    category TEXT NOT NULL DEFAULT '',
    storage_path TEXT NOT NULL,
    file_name TEXT NOT NULL,
    original_file_name TEXT NOT NULL,
    content_type TEXT NOT NULL,
    file_size_bytes BIGINT NOT NULL DEFAULT 0,
    description TEXT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    modified_at TIMESTAMPTZ,
    created_by UUID,
    modified_by UUID
);

CREATE INDEX idx_animal_documents_animal_id ON animal_documents(animal_id);
CREATE INDEX idx_animal_documents_farm_id ON animal_documents(farm_id);

-- ─── Animal Timeline Events ────────────────────────────────────────────
CREATE TABLE animal_timeline_events (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    animal_id UUID NOT NULL REFERENCES animals(id) ON DELETE CASCADE,
    farm_id UUID NOT NULL REFERENCES farms(id) ON DELETE CASCADE,
    event_type TEXT NOT NULL,
    title TEXT NOT NULL,
    description TEXT,
    related_entity_id UUID,
    related_entity_type TEXT,
    occurred_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    modified_at TIMESTAMPTZ,
    created_by UUID,
    modified_by UUID
);

CREATE INDEX idx_animal_timeline_events_animal_id ON animal_timeline_events(animal_id);
CREATE INDEX idx_animal_timeline_events_farm_id ON animal_timeline_events(farm_id);
CREATE INDEX idx_animal_timeline_events_occurred_at ON animal_timeline_events(occurred_at);

-- ─── Animal Transfers ──────────────────────────────────────────────────
CREATE TABLE animal_transfers (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    animal_id UUID NOT NULL REFERENCES animals(id) ON DELETE CASCADE,
    farm_id UUID NOT NULL REFERENCES farms(id) ON DELETE CASCADE,
    from_location_id UUID REFERENCES locations(id) ON DELETE SET NULL,
    to_location_id UUID NOT NULL REFERENCES locations(id) ON DELETE RESTRICT,
    transferred_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    reason TEXT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    modified_at TIMESTAMPTZ,
    created_by UUID,
    modified_by UUID
);

CREATE INDEX idx_animal_transfers_animal_id ON animal_transfers(animal_id);
CREATE INDEX idx_animal_transfers_farm_id ON animal_transfers(farm_id);

-- ─── Weight Records ────────────────────────────────────────────────────
CREATE TABLE weight_records (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    animal_id UUID NOT NULL REFERENCES animals(id) ON DELETE CASCADE,
    farm_id UUID NOT NULL REFERENCES farms(id) ON DELETE CASCADE,
    weight_kg DECIMAL(10,2) NOT NULL,
    recorded_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    notes TEXT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    modified_at TIMESTAMPTZ,
    created_by UUID,
    modified_by UUID
);

CREATE INDEX idx_weight_records_animal_id ON weight_records(animal_id);
CREATE INDEX idx_weight_records_farm_id ON weight_records(farm_id);
CREATE INDEX idx_weight_records_recorded_at ON weight_records(recorded_at);
