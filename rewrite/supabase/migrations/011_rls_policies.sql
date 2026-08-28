-- ============================================================================
-- Migration 011: Row Level Security policies
-- ============================================================================

-- ─── Helper function ───────────────────────────────────────────────────
-- Returns the list of farm IDs the current authenticated user has access to.
CREATE OR REPLACE FUNCTION public.user_farm_ids()
RETURNS SETOF uuid
LANGUAGE sql
STABLE
SECURITY DEFINER
AS $$
  SELECT uf.farm_id
  FROM user_farms uf
  WHERE uf.user_id = auth.uid()
$$;

-- Returns true if the current user belongs to the given farm.
CREATE OR REPLACE FUNCTION public.user_belongs_to_farm(target_farm_id uuid)
RETURNS boolean
LANGUAGE sql
STABLE
SECURITY DEFINER
AS $$
  SELECT EXISTS (
    SELECT 1 FROM user_farms
    WHERE user_id = auth.uid() AND farm_id = target_farm_id
  )
$$;

-- ─── Enable RLS on all tables ──────────────────────────────────────────

ALTER TABLE users ENABLE ROW LEVEL SECURITY;
ALTER TABLE user_farms ENABLE ROW LEVEL SECURITY;
ALTER TABLE farm_configurations ENABLE ROW LEVEL SECURITY;
ALTER TABLE animal_types ENABLE ROW LEVEL SECURITY;
ALTER TABLE breeds ENABLE ROW LEVEL SECURITY;
ALTER TABLE sex_options ENABLE ROW LEVEL SECURITY;
ALTER TABLE age_categories ENABLE ROW LEVEL SECURITY;
ALTER TABLE animal_statuses ENABLE ROW LEVEL SECURITY;
ALTER TABLE identification_types ENABLE ROW LEVEL SECURITY;
ALTER TABLE location_types ENABLE ROW LEVEL SECURITY;
ALTER TABLE locations ENABLE ROW LEVEL SECURITY;
ALTER TABLE custom_field_definitions ENABLE ROW LEVEL SECURITY;
ALTER TABLE animals ENABLE ROW LEVEL SECURITY;
ALTER TABLE animal_identifications ENABLE ROW LEVEL SECURITY;
ALTER TABLE animal_images ENABLE ROW LEVEL SECURITY;
ALTER TABLE animal_documents ENABLE ROW LEVEL SECURITY;
ALTER TABLE animal_timeline_events ENABLE ROW LEVEL SECURITY;
ALTER TABLE animal_transfers ENABLE ROW LEVEL SECURITY;
ALTER TABLE weight_records ENABLE ROW LEVEL SECURITY;
ALTER TABLE feed_types ENABLE ROW LEVEL SECURITY;
ALTER TABLE feed_stock_movements ENABLE ROW LEVEL SECURITY;
ALTER TABLE diet_plans ENABLE ROW LEVEL SECURITY;
ALTER TABLE diet_plan_items ENABLE ROW LEVEL SECURITY;
ALTER TABLE feeding_schedules ENABLE ROW LEVEL SECURITY;
ALTER TABLE feeding_tasks ENABLE ROW LEVEL SECURITY;
ALTER TABLE feed_records ENABLE ROW LEVEL SECURITY;
ALTER TABLE medicines ENABLE ROW LEVEL SECURITY;
ALTER TABLE medicine_stocks ENABLE ROW LEVEL SECURITY;
ALTER TABLE medicine_usages ENABLE ROW LEVEL SECURITY;
ALTER TABLE medical_records ENABLE ROW LEVEL SECURITY;
ALTER TABLE vaccine_types ENABLE ROW LEVEL SECURITY;
ALTER TABLE vaccination_records ENABLE ROW LEVEL SECURITY;
ALTER TABLE vaccination_schedules ENABLE ROW LEVEL SECURITY;
ALTER TABLE breeding_records ENABLE ROW LEVEL SECURITY;
ALTER TABLE gestation_records ENABLE ROW LEVEL SECURITY;
ALTER TABLE gestation_health_checks ENABLE ROW LEVEL SECURITY;
ALTER TABLE birth_records ENABLE ROW LEVEL SECURITY;
ALTER TABLE birth_offspring ENABLE ROW LEVEL SECURITY;
ALTER TABLE expense_categories ENABLE ROW LEVEL SECURITY;
ALTER TABLE payment_methods ENABLE ROW LEVEL SECURITY;
ALTER TABLE expenses ENABLE ROW LEVEL SECURITY;
ALTER TABLE income_categories ENABLE ROW LEVEL SECURITY;
ALTER TABLE income_records ENABLE ROW LEVEL SECURITY;
ALTER TABLE departments ENABLE ROW LEVEL SECURITY;
ALTER TABLE employee_roles ENABLE ROW LEVEL SECURITY;
ALTER TABLE employees ENABLE ROW LEVEL SECURITY;
ALTER TABLE attendance_records ENABLE ROW LEVEL SECURITY;
ALTER TABLE salary_payments ENABLE ROW LEVEL SECURITY;
ALTER TABLE performance_reviews ENABLE ROW LEVEL SECURITY;
ALTER TABLE inventory_items ENABLE ROW LEVEL SECURITY;
ALTER TABLE stock_movements ENABLE ROW LEVEL SECURITY;
ALTER TABLE suppliers ENABLE ROW LEVEL SECURITY;
ALTER TABLE supplier_purchases ENABLE ROW LEVEL SECURITY;
ALTER TABLE customers ENABLE ROW LEVEL SECURITY;
ALTER TABLE customer_sales ENABLE ROW LEVEL SECURITY;
ALTER TABLE farm_tasks ENABLE ROW LEVEL SECURITY;
ALTER TABLE audit_logs ENABLE ROW LEVEL SECURITY;

-- ─── Generic farm-scoped policies ──────────────────────────────────────
-- Pattern: every table with a farm_id column gets SELECT/INSERT/UPDATE
-- policies that check user_belongs_to_farm(farm_id).
-- Tables without farm_id get user-specific policies.

-- Helper: macro to create farm-scoped policies for a table
-- We do it per-table to keep migrations explicit and debuggable.

-- ─── Users ─────────────────────────────────────────────────────────────
CREATE POLICY "Users can view own profile" ON users
  FOR SELECT USING (id = auth.uid());

CREATE POLICY "Users can update own profile" ON users
  FOR UPDATE USING (id = auth.uid());

-- ─── User Farms ────────────────────────────────────────────────────────
CREATE POLICY "Users can view own farm memberships" ON user_farms
  FOR SELECT USING (user_id = auth.uid());

CREATE POLICY "Farm owners can manage memberships" ON user_farms
  FOR ALL USING (
    EXISTS (
      SELECT 1 FROM user_farms uf
      WHERE uf.user_id = auth.uid() AND uf.farm_id = user_farms.farm_id AND uf.role = 'Owner'
    )
  );

-- ─── Farm-scoped tables: SELECT ────────────────────────────────────────
-- All tables with farm_id get a SELECT policy
DO $$
DECLARE
  tbl text;
  farm_tables text[] := ARRAY[
    'farm_configurations', 'animal_types', 'breeds', 'sex_options',
    'age_categories', 'animal_statuses', 'identification_types',
    'location_types', 'locations', 'custom_field_definitions',
    'animals', 'animal_identifications', 'animal_images', 'animal_documents',
    'animal_timeline_events', 'animal_transfers', 'weight_records',
    'feed_types', 'feed_stock_movements', 'diet_plans', 'diet_plan_items',
    'feeding_schedules', 'feeding_tasks', 'feed_records',
    'medicines', 'medicine_stocks', 'medicine_usages', 'medical_records',
    'vaccine_types', 'vaccination_records', 'vaccination_schedules',
    'breeding_records', 'gestation_records', 'gestation_health_checks',
    'birth_records', 'birth_offspring',
    'expense_categories', 'payment_methods', 'expenses',
    'income_categories', 'income_records',
    'departments', 'employee_roles', 'employees',
    'attendance_records', 'salary_payments', 'performance_reviews',
    'inventory_items', 'stock_movements',
    'suppliers', 'supplier_purchases', 'customers', 'customer_sales',
    'farm_tasks', 'audit_logs'
  ];
BEGIN
  FOREACH tbl IN ARRAY farm_tables LOOP
    EXECUTE format(
      'CREATE POLICY "%s_farm_select" ON %I FOR SELECT USING (user_belongs_to_farm(farm_id))',
      tbl, tbl
    );
  END LOOP;
END $$;

-- ─── Farm-scoped tables: INSERT ────────────────────────────────────────
DO $$
DECLARE
  tbl text;
  farm_tables text[] := ARRAY[
    'farm_configurations', 'animal_types', 'breeds', 'sex_options',
    'age_categories', 'animal_statuses', 'identification_types',
    'location_types', 'locations', 'custom_field_definitions',
    'animals', 'animal_identifications', 'animal_images', 'animal_documents',
    'animal_timeline_events', 'animal_transfers', 'weight_records',
    'feed_types', 'feed_stock_movements', 'diet_plans', 'diet_plan_items',
    'feeding_schedules', 'feeding_tasks', 'feed_records',
    'medicines', 'medicine_stocks', 'medicine_usages', 'medical_records',
    'vaccine_types', 'vaccination_records', 'vaccination_schedules',
    'breeding_records', 'gestation_records', 'gestation_health_checks',
    'birth_records', 'birth_offspring',
    'expense_categories', 'payment_methods', 'expenses',
    'income_categories', 'income_records',
    'departments', 'employee_roles', 'employees',
    'attendance_records', 'salary_payments', 'performance_reviews',
    'inventory_items', 'stock_movements',
    'suppliers', 'supplier_purchases', 'customers', 'customer_sales',
    'farm_tasks'
  ];
BEGIN
  FOREACH tbl IN ARRAY farm_tables LOOP
    EXECUTE format(
      'CREATE POLICY "%s_farm_insert" ON %I FOR INSERT WITH CHECK (user_belongs_to_farm(farm_id))',
      tbl, tbl
    );
  END LOOP;
END $$;

-- ─── Farm-scoped tables: UPDATE ────────────────────────────────────────
DO $$
DECLARE
  tbl text;
  farm_tables text[] := ARRAY[
    'farm_configurations', 'animal_types', 'breeds', 'sex_options',
    'age_categories', 'animal_statuses', 'identification_types',
    'location_types', 'locations', 'custom_field_definitions',
    'animals', 'animal_identifications', 'animal_images', 'animal_documents',
    'animal_timeline_events', 'animal_transfers', 'weight_records',
    'feed_types', 'feed_stock_movements', 'diet_plans', 'diet_plan_items',
    'feeding_schedules', 'feeding_tasks', 'feed_records',
    'medicines', 'medicine_stocks', 'medicine_usages', 'medical_records',
    'vaccine_types', 'vaccination_records', 'vaccination_schedules',
    'breeding_records', 'gestation_records', 'gestation_health_checks',
    'birth_records', 'birth_offspring',
    'expense_categories', 'payment_methods', 'expenses',
    'income_categories', 'income_records',
    'departments', 'employee_roles', 'employees',
    'attendance_records', 'salary_payments', 'performance_reviews',
    'inventory_items', 'stock_movements',
    'suppliers', 'supplier_purchases', 'customers', 'customer_sales',
    'farm_tasks'
  ];
BEGIN
  FOREACH tbl IN ARRAY farm_tables LOOP
    EXECUTE format(
      'CREATE POLICY "%s_farm_update" ON %I FOR UPDATE USING (user_belongs_to_farm(farm_id))',
      tbl, tbl
    );
  END LOOP;
END $$;

-- ─── Farm-scoped tables: DELETE ────────────────────────────────────────
-- Only FarmManager and SystemOwner can delete. Using a broader policy
-- that checks user_farm role for delete operations.
DO $$
DECLARE
  tbl text;
  farm_tables text[] := ARRAY[
    'farm_configurations', 'animal_types', 'breeds', 'sex_options',
    'age_categories', 'animal_statuses', 'identification_types',
    'location_types', 'locations', 'custom_field_definitions',
    'animals', 'animal_identifications', 'animal_images', 'animal_documents',
    'animal_timeline_events', 'animal_transfers', 'weight_records',
    'feed_types', 'feed_stock_movements', 'diet_plans', 'diet_plan_items',
    'feeding_schedules', 'feeding_tasks', 'feed_records',
    'medicines', 'medicine_stocks', 'medicine_usages', 'medical_records',
    'vaccine_types', 'vaccination_records', 'vaccination_schedules',
    'breeding_records', 'gestation_records', 'gestation_health_checks',
    'birth_records', 'birth_offspring',
    'expense_categories', 'payment_methods', 'expenses',
    'income_categories', 'income_records',
    'departments', 'employee_roles', 'employees',
    'attendance_records', 'salary_payments', 'performance_reviews',
    'inventory_items', 'stock_movements',
    'suppliers', 'supplier_purchases', 'customers', 'customer_sales',
    'farm_tasks'
  ];
BEGIN
  FOREACH tbl IN ARRAY farm_tables LOOP
    EXECUTE format(
      'CREATE POLICY "%s_farm_delete" ON %I FOR DELETE USING (
        EXISTS (
          SELECT 1 FROM user_farms uf
          WHERE uf.user_id = auth.uid() AND uf.farm_id = %I.farm_id AND uf.role IN (''Owner'', ''Manager'')
        )
      )',
      tbl, tbl, tbl
    );
  END LOOP;
END $$;

-- ─── Audit Log: append-only ────────────────────────────────────────────
-- Audit logs cannot be updated or deleted by anyone (not even admins).
-- Remove UPDATE and DELETE policies (no policies = no access).
-- Already covered by farm_scoped policies above for SELECT/INSERT.
-- Add a specific policy to prevent updates:
CREATE POLICY "audit_logs_no_update" ON audit_logs
  FOR UPDATE USING (false);

CREATE POLICY "audit_logs_no_delete" ON audit_logs
  FOR DELETE USING (false);
