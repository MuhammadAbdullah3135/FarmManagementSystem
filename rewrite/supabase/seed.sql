-- ============================================================================
-- Seed Data: Development environment
-- ============================================================================
-- Run after all migrations: psql $DATABASE_URL -f seed.sql
-- Or via Supabase Dashboard > SQL Editor

-- ─── Global Roles ──────────────────────────────────────────────────────
INSERT INTO roles (id, name) VALUES
  ('a0000000-0000-0000-0000-000000000001', 'SystemOwner'),
  ('a0000000-0000-0000-0000-000000000002', 'FarmManager'),
  ('a0000000-0000-0000-0000-000000000003', 'Employee'),
  ('a0000000-0000-0000-0000-000000000004', 'Vet'),
  ('a0000000-0000-0000-0000-000000000005', 'Accountant')
ON CONFLICT (name) DO NOTHING;

-- ─── Demo Account ──────────────────────────────────────────────────────
-- NOTE: In production, the account_id must match the Supabase auth.users.id.
-- For local dev seeding, we create a UUID that you'd replace after signup.
DO $$
DECLARE
  demo_user_id UUID := 'b0000000-0000-0000-0000-000000000001';
  demo_account_id UUID := 'c0000000-0000-0000-0000-000000000001';
  demo_farm_id UUID := 'd0000000-0000-0000-0000-000000000001';
BEGIN
  -- Account
  INSERT INTO accounts (id, name) VALUES
    (demo_account_id, 'Green Valley Farm Group')
  ON CONFLICT (id) DO NOTHING;

  -- User (note: in real app this comes from Supabase auth signup)
  -- This user ID should be replaced with the actual auth.users.id after signup
  INSERT INTO users (id, account_id, email, first_name, last_name) VALUES
    (demo_user_id, demo_account_id, 'admin@greenvalley.farm', 'Ahmad', 'Khan')
  ON CONFLICT (id) DO NOTHING;

  -- User-Role assignment
  INSERT INTO user_roles (user_id, role_id)
  SELECT demo_user_id, id FROM roles WHERE name = 'SystemOwner'
  ON CONFLICT DO NOTHING;

  -- Farm
  INSERT INTO farms (id, account_id, name, description) VALUES
    (demo_farm_id, demo_account_id, 'Green Valley Farm', 'Main cattle and crop farm in Punjab')
  ON CONFLICT (id) DO NOTHING;

  -- User-Farm membership
  INSERT INTO user_farms (user_id, farm_id, role)
  VALUES (demo_user_id, demo_farm_id, 'Owner')
  ON CONFLICT DO NOTHING;

  -- ─── Farm Configuration ──────────────────────────────────────────

  -- Animal Types
  INSERT INTO animal_types (id, farm_id, name) VALUES
    ('e1000000-0000-0000-0000-000000000001', demo_farm_id, 'Cattle'),
    ('e1000000-0000-0000-0000-000000000002', demo_farm_id, 'Goat'),
    ('e1000000-0000-0000-0000-000000000003', demo_farm_id, 'Sheep'),
    ('e1000000-0000-0000-0000-000000000004', demo_farm_id, 'Buffalo')
  ON CONFLICT (farm_id, name) DO NOTHING;

  -- Breeds
  INSERT INTO breeds (id, animal_type_id, name, average_gestation_days) VALUES
    ('f1000000-0000-0000-0000-000000000001', 'e1000000-0000-0000-0000-000000000001', 'Holstein', 283),
    ('f1000000-0000-0000-0000-000000000002', 'e1000000-0000-0000-0000-000000000001', 'Jersey', 279),
    ('f1000000-0000-0000-0000-000000000003', 'e1000000-0000-0000-0000-000000000001', 'Angus', 283),
    ('f1000000-0000-0000-0000-000000000004', 'e1000000-0000-0000-0000-000000000002', 'Boer', 150),
    ('f1000000-0000-0000-0000-000000000005', 'e1000000-0000-0000-0000-000000000004', 'Nili-Ravi', 310),
    ('f1000000-0000-0000-0000-000000000006', 'e1000000-0000-0000-0000-000000000004', 'Murrah', 305)
  ON CONFLICT (animal_type_id, name) DO NOTHING;

  -- Sex Options
  INSERT INTO sex_options (id, farm_id, value) VALUES
    ('g1000000-0000-0000-0000-000000000001', demo_farm_id, 'Male'),
    ('g1000000-0000-0000-0000-000000000002', demo_farm_id, 'Female')
  ON CONFLICT (farm_id, value) DO NOTHING;

  -- Age Categories
  INSERT INTO age_categories (id, farm_id, name, min_days, max_days) VALUES
    ('h1000000-0000-0000-0000-000000000001', demo_farm_id, 'Calf', 0, 180),
    ('h1000000-0000-0000-0000-000000000002', demo_farm_id, 'Heifer', 181, 730),
    ('h1000000-0000-0000-0000-000000000003', demo_farm_id, 'Adult', 731, 3650),
    ('h1000000-0000-0000-0000-000000000004', demo_farm_id, 'Senior', 3651, 99999)
  ON CONFLICT (farm_id, name) DO NOTHING;

  -- Animal Statuses
  INSERT INTO animal_statuses (id, farm_id, name, category, is_system_defined) VALUES
    ('i1000000-0000-0000-0000-000000000001', demo_farm_id, 'Active', 'Active', true),
    ('i1000000-0000-0000-0000-000000000002', demo_farm_id, 'Sick', 'Active', true),
    ('i1000000-0000-0000-0000-000000000003', demo_farm_id, 'Pregnant', 'Active', true),
    ('i1000000-0000-0000-0000-000000000004', demo_farm_id, 'Sold', 'Terminal', true),
    ('i1000000-0000-0000-0000-000000000005', demo_farm_id, 'Deceased', 'Terminal', true),
    ('i1000000-0000-0000-0000-000000000006', demo_farm_id, 'Quarantined', 'Inactive', true)
  ON CONFLICT (farm_id, name) DO NOTHING;

  -- Identification Types
  INSERT INTO identification_types (id, farm_id, name) VALUES
    ('j1000000-0000-0000-0000-000000000001', demo_farm_id, 'Ear Tag'),
    ('j1000000-0000-0000-0000-000000000002', demo_farm_id, 'Collar'),
    ('j1000000-0000-0000-0000-000000000003', demo_farm_id, 'Branding'),
    ('j1000000-0000-0000-0000-000000000004', demo_farm_id, 'RFID Chip')
  ON CONFLICT (farm_id, name) DO NOTHING;

  -- Location Types
  INSERT INTO location_types (id, farm_id, name) VALUES
    ('k1000000-0000-0000-0000-000000000001', demo_farm_id, 'Barn'),
    ('k1000000-0000-0000-0000-000000000002', demo_farm_id, 'Pasture'),
    ('k1000000-0000-0000-0000-000000000003', demo_farm_id, 'Pen'),
    ('k1000000-0000-0000-0000-000000000004', demo_farm_id, 'Feedlot'),
    ('k1000000-0000-0000-0000-000000000005', demo_farm_id, 'Field')
  ON CONFLICT (farm_id, name) DO NOTHING;

  -- Locations
  INSERT INTO locations (id, farm_id, name, location_type_id) VALUES
    ('l1000000-0000-0000-0000-000000000001', demo_farm_id, 'Main Barn', 'k1000000-0000-0000-0000-000000000001'),
    ('l1000000-0000-0000-0000-000000000002', demo_farm_id, 'North Pasture', 'k1000000-0000-0000-0000-000000000002'),
    ('l1000000-0000-0000-0000-000000000003', demo_farm_id, 'South Pasture', 'k1000000-0000-0000-0000-000000000002'),
    ('l1000000-0000-0000-0000-000000000004', demo_farm_id, 'Sick Bay', 'k1000000-0000-0000-0000-000000000003')
  ON CONFLICT (farm_id, name) DO NOTHING;

  -- ─── Expense & Payment Setup ─────────────────────────────────────

  -- Expense Categories
  INSERT INTO expense_categories (id, farm_id, name, description) VALUES
    ('m1000000-0000-0000-0000-000000000001', demo_farm_id, 'Feed', 'Animal feed purchases'),
    ('m1000000-0000-0000-0000-000000000002', demo_farm_id, 'Veterinary', 'Vet fees and medicine'),
    ('m1000000-0000-0000-0000-000000000003', demo_farm_id, 'Equipment', 'Farm equipment'),
    ('m1000000-0000-0000-0000-000000000004', demo_farm_id, 'Utilities', 'Electricity, water, etc.'),
    ('m1000000-0000-0000-0000-000000000005', demo_farm_id, 'Labor', 'Employee wages')
  ON CONFLICT (farm_id, name) DO NOTHING;

  -- Payment Methods
  INSERT INTO payment_methods (id, farm_id, name) VALUES
    ('n1000000-0000-0000-0000-000000000001', demo_farm_id, 'Cash'),
    ('n1000000-0000-0000-0000-000000000002', demo_farm_id, 'Bank Transfer'),
    ('n1000000-0000-0000-0000-000000000003', demo_farm_id, 'Mobile Money'),
    ('n1000000-0000-0000-0000-000000000004', demo_farm_id, 'Check')
  ON CONFLICT (farm_id, name) DO NOTHING;

  -- Income Categories
  INSERT INTO income_categories (id, farm_id, name, description) VALUES
    ('o1000000-0000-0000-0000-000000000001', demo_farm_id, 'Animal Sales', 'Revenue from selling animals'),
    ('o1000000-0000-0000-0000-000000000002', demo_farm_id, 'Milk Sales', 'Dairy revenue'),
    ('o1000000-0000-0000-0000-000000000003', demo_farm_id, 'Egg Sales', 'Poultry revenue'),
    ('o1000000-0000-0000-0000-000000000004', demo_farm_id, 'Other', 'Miscellaneous income')
  ON CONFLICT (farm_id, name) DO NOTHING;

  -- ─── Feed Types ──────────────────────────────────────────────────
  INSERT INTO feed_types (id, farm_id, name, category, unit, cost_per_unit) VALUES
    ('p1000000-0000-0000-0000-000000000001', demo_farm_id, 'Alfalfa Hay', 'Forage', 'Bale', 12.50),
    ('p1000000-0000-0000-0000-000000000002', demo_farm_id, 'Corn Silage', 'Forage', 'Kilogram', 0.35),
    ('p1000000-0000-0000-0000-000000000003', demo_farm_id, 'Dairy Concentrate', 'Concentrate', 'Kilogram', 0.85),
    ('p1000000-0000-0000-0000-000000000004', demo_farm_id, 'Mineral Premix', 'Mineral', 'Kilogram', 3.20),
    ('p1000000-0000-0000-0000-000000000005', demo_farm_id, 'Wheat Straw', 'Forage', 'Bale', 8.00)
  ON CONFLICT (farm_id, name) DO NOTHING;

  -- ─── Departments ─────────────────────────────────────────────────
  INSERT INTO departments (id, farm_id, name) VALUES
    ('q1000000-0000-0000-0000-000000000001', demo_farm_id, 'Operations'),
    ('q1000000-0000-0000-0000-000000000002', demo_farm_id, 'Veterinary'),
    ('q1000000-0000-0000-0000-000000000003', demo_farm_id, 'Administration')
  ON CONFLICT (farm_id, name) DO NOTHING;

  -- Employee Roles
  INSERT INTO employee_roles (id, farm_id, name, description) VALUES
    ('r1000000-0000-0000-0000-000000000001', demo_farm_id, 'Farm Worker', 'General farm labor'),
    ('r1000000-0000-0000-0000-000000000002', demo_farm_id, 'Veterinarian', 'Animal healthcare'),
    ('r1000000-0000-0000-0000-000000000003', demo_farm_id, 'Supervisor', 'Team lead'),
    ('r1000000-0000-0000-0000-000000000004', demo_farm_id, 'Accountant', 'Financial management')
  ON CONFLICT (farm_id, name) DO NOTHING;

  -- ─── Sample Animals ──────────────────────────────────────────────
  INSERT INTO animals (id, farm_id, tag_number, name, animal_type_id, breed_id, sex_option_id, age_category_id, animal_status_id, location_id, date_of_birth) VALUES
    ('s1000000-0000-0000-0000-000000000001', demo_farm_id, 'A001', 'Bella', 'e1000000-0000-0000-0000-000000000001', 'f1000000-0000-0000-0000-000000000001', 'g1000000-0000-0000-0000-000000000002', 'h1000000-0000-0000-0000-000000000003', 'i1000000-0000-0000-0000-000000000001', 'l1000000-0000-0000-0000-000000000002', '2022-03-15'),
    ('s1000000-0000-0000-0000-000000000002', demo_farm_id, 'A002', 'Daisy', 'e1000000-0000-0000-0000-000000000001', 'f1000000-0000-0000-0000-000000000002', 'g1000000-0000-0000-0000-000000000002', 'h1000000-0000-0000-0000-000000000003', 'i1000000-0000-0000-0000-000000000003', 'l1000000-0000-0000-0000-000000000002', '2021-07-20'),
    ('s1000000-0000-0000-0000-000000000003', demo_farm_id, 'A003', 'Thunder', 'e1000000-0000-0000-0000-000000000001', 'f1000000-0000-0000-0000-000000000003', 'g1000000-0000-0000-0000-000000000001', 'h1000000-0000-0000-0000-000000000003', 'i1000000-0000-0000-0000-000000000001', 'l1000000-0000-0000-0000-000000000002', '2023-01-10'),
    ('s1000000-0000-0000-0000-000000000004', demo_farm_id, 'G001', 'Nuby', 'e1000000-0000-0000-0000-000000000002', 'f1000000-0000-0000-0000-000000000004', 'g1000000-0000-0000-0000-000000000002', 'h1000000-0000-0000-0000-000000000003', 'i1000000-0000-0000-0000-000000000001', 'l1000000-0000-0000-0000-000000000003', '2023-06-05'),
    ('s1000000-0000-0000-0000-000000000005', demo_farm_id, 'B001', 'Noor', 'e1000000-0000-0000-0000-000000000004', 'f1000000-0000-0000-0000-000000000005', 'g1000000-0000-0000-0000-000000000002', 'h1000000-0000-0000-0000-000000000002', 'i1000000-0000-0000-0000-000000000001', 'l1000000-0000-0000-0000-000000000003', '2023-09-12')
  ON CONFLICT (farm_id, tag_number) DO NOTHING;

  -- ─── Weight Records ──────────────────────────────────────────────
  INSERT INTO weight_records (animal_id, farm_id, weight_kg, recorded_at) VALUES
    ('s1000000-0000-0000-0000-000000000001', demo_farm_id, 420.5, now() - interval '30 days'),
    ('s1000000-0000-0000-0000-000000000001', demo_farm_id, 427.0, now() - interval '15 days'),
    ('s1000000-0000-0000-0000-000000000002', demo_farm_id, 380.0, now() - interval '30 days'),
    ('s1000000-0000-0000-0000-000000000003', demo_farm_id, 510.0, now() - interval '20 days'),
    ('s1000000-0000-0000-0000-000000000004', demo_farm_id, 45.0, now() - interval '10 days'),
    ('s1000000-0000-0000-0000-000000000005', demo_farm_id, 280.0, now() - interval '5 days')
  ON CONFLICT DO NOTHING;

  -- ─── Sample Expenses ─────────────────────────────────────────────
  INSERT INTO expenses (farm_id, expense_date, amount, expense_category_id, payment_method_id, description) VALUES
    (demo_farm_id, now() - interval '60 days', 1500.00, 'm1000000-0000-0000-0000-000000000001', 'n1000000-0000-0000-0000-000000000001', 'Monthly feed purchase'),
    (demo_farm_id, now() - interval '45 days', 350.00, 'm1000000-0000-0000-0000-000000000002', 'n1000000-0000-0000-0000-000000000002', 'Vet check-up for A002'),
    (demo_farm_id, now() - interval '30 days', 1500.00, 'm1000000-0000-0000-0000-000000000001', 'n1000000-0000-0000-0000-000000000001', 'Monthly feed purchase'),
    (demo_farm_id, now() - interval '15 days', 800.00, 'm1000000-0000-0000-0000-000000000003', 'n1000000-0000-0000-0000-000000000002', 'Fence repair equipment')
  ON CONFLICT DO NOTHING;

  -- ─── Sample Income ───────────────────────────────────────────────
  INSERT INTO income_records (farm_id, income_date, amount, income_category_id, payment_method_id, description) VALUES
    (demo_farm_id, now() - interval '50 days', 5000.00, 'o1000000-0000-0000-0000-000000000002', 'n1000000-0000-0000-0000-000000000002', 'Weekly milk sales'),
    (demo_farm_id, now() - interval '35 days', 5000.00, 'o1000000-0000-0000-0000-000000000002', 'n1000000-0000-0000-0000-000000000002', 'Weekly milk sales'),
    (demo_farm_id, now() - interval '20 days', 5000.00, 'o1000000-0000-0000-0000-000000000002', 'n1000000-0000-0000-0000-000000000002', 'Weekly milk sales'),
    (demo_farm_id, now() - interval '10 days', 12000.00, 'o1000000-0000-0000-0000-000000000001', 'n1000000-0000-0000-0000-000000000002', 'Sold one adult bull')
  ON CONFLICT DO NOTHING;

  -- ─── Sample Employees ────────────────────────────────────────────
  INSERT INTO employees (id, farm_id, first_name, last_name, phone, department_id, employee_role_id, hire_date) VALUES
    ('t1000000-0000-0000-0000-000000000001', demo_farm_id, 'Rashid', 'Ahmed', '+92-300-1234567', 'q1000000-0000-0000-0000-000000000001', 'r1000000-0000-0000-0000-000000000003', '2022-01-15'),
    ('t1000000-0000-0000-0000-000000000002', demo_farm_id, 'Zain', 'Ali', '+92-300-7654321', 'q1000000-0000-0000-0000-000000000001', 'r1000000-0000-0000-0000-000000000001', '2023-03-01'),
    ('t1000000-0000-0000-0000-000000000003', demo_farm_id, 'Dr. Fatima', 'Noor', '+92-300-1112233', 'q1000000-0000-0000-0000-000000000002', 'r1000000-0000-0000-0000-000000000002', '2022-06-01')
  ON CONFLICT (id) DO NOTHING;

END $$;
