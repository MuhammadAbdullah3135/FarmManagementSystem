-- ============================================================================
-- Migration 001: Extensions and PostgreSQL enum types
-- ============================================================================

-- Enable required extensions
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";
CREATE EXTENSION IF NOT EXISTS "pgcrypto";

-- ─── Custom enum types ──────────────────────────────────────────────────

CREATE TYPE animal_status_category AS ENUM ('Active', 'Inactive', 'Terminal');
CREATE TYPE audit_action AS ENUM ('Create', 'Update', 'Delete');
CREATE TYPE attendance_status AS ENUM ('Present', 'Absent', 'Late', 'HalfDay', 'Leave', 'Holiday');
CREATE TYPE birth_outcome AS ENUM ('Alive', 'Stillborn', 'Weak');
CREATE TYPE breeding_method AS ENUM ('Natural', 'AI');
CREATE TYPE breeding_result AS ENUM ('Pending', 'Confirmed', 'Failed');
CREATE TYPE custom_field_type AS ENUM ('String', 'Number', 'Boolean', 'Date', 'Select');
CREATE TYPE farm_task_priority AS ENUM ('Low', 'Medium', 'High');
CREATE TYPE farm_task_status AS ENUM ('Pending', 'InProgress', 'Completed', 'Cancelled');
CREATE TYPE feed_category AS ENUM ('Forage', 'Concentrate', 'Mineral', 'Supplement', 'Additive', 'Other');
CREATE TYPE feed_unit AS ENUM ('Kilogram', 'Gram', 'Ton', 'Liter', 'Bale', 'Bag', 'Other');
CREATE TYPE feeding_task_status AS ENUM ('Pending', 'Completed', 'Skipped');
CREATE TYPE gestation_stage AS ENUM ('Early', 'Mid', 'Late', 'Overdue');
CREATE TYPE inventory_movement_type AS ENUM ('Purchase', 'Consumption', 'Transfer', 'Adjustment');
CREATE TYPE medical_record_status AS ENUM ('Open', 'InProgress', 'Resolved');
CREATE TYPE salary_type AS ENUM ('Monthly', 'Weekly', 'Daily', 'Hourly');
CREATE TYPE stock_movement_type AS ENUM ('Purchase', 'Consumption', 'Adjustment');
