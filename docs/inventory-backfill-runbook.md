# Inventory category backfill — runbook

Status: **dry run completed against a local snapshot of production**. The snapshot contains
no inventory items, so no duplicates were found. Nothing in this document authorises
`--apply`.

## What the command does

`inventory:categories:backfill` creates one `InventoryCategory` per distinct,
case-insensitively grouped, non-blank `InventoryItem.Category` string per farm and links
the items that do not have a `CategoryId` yet. It never changes the retained `Category`
string, never overwrites an existing `CategoryId`, and leaves blank/null categories null.

The command is handled in `src/FMS.API/Program.cs` **before** the host starts, so it does
not run `MigrateAsync()` or role seeding. In dry-run mode the service opens a transaction,
marks it `SET TRANSACTION READ ONLY` on PostgreSQL, and always rolls it back
(`src/FMS.Infrastructure/Inventory/InventoryCategoryBackfillService.cs`). Only an explicit
`--apply` writes.

## Preconditions checklist

The owner must confirm every line below **before** connecting. Reply with the checked list
and the target details.

- [ ] The target is **non-production** (staging, a restored copy, or a local restore — not
      the live farm database).
- [ ] It was **restored from a snapshot** of the database to be assessed, so the counts
      describe real data rather than an empty schema.
- [ ] The schema migration `20261020152830_AddInventoryCategories` **is applied** (the
      `InventoryCategories` table and `InventoryItems.CategoryId` exist).
- [ ] The backfill **has not been applied** yet, or you accept that a re-run reports only
      what is still unlinked. A previous partial run is safe to re-run.
- [ ] A **read-only database role is preferred**. The dry run only reads, but a read-only
      role makes that guarantee independent of the application code. If the role is
      read-only, a dry run that tried to write would fail loudly instead of silently
      succeeding.
- [ ] You have the host, port, database name, username, and password for that copy.
- [ ] You know the branch/commit being run (`git rev-parse --short HEAD`).

To check the migration and backfill state without using the app, run these read-only
queries against the copy:

```sql
-- Expect a row for AddInventoryCategories.
SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY "MigrationId";

-- How many items the backfill would consider.
SELECT count(*) AS unlinked_nonblank
FROM "InventoryItems"
WHERE "CategoryId" IS NULL
  AND "Category" IS NOT NULL
  AND btrim("Category") <> '';

-- How many categories exist already (usually 0 before the backfill).
SELECT count(*) AS categories FROM "InventoryCategories";
```

Optional duplicate check — case/whitespace variants that the backfill will merge into one
category:

```sql
SELECT "FarmId",
       lower(btrim("Category")) AS normalized,
       count(*) AS items,
       array_agg(DISTINCT btrim("Category")) AS spellings
FROM "InventoryItems"
WHERE "CategoryId" IS NULL
  AND "Category" IS NOT NULL
  AND btrim("Category") <> ''
GROUP BY "FarmId", lower(btrim("Category"))
HAVING count(DISTINCT btrim("Category")) > 1
ORDER BY "FarmId", normalized;
```

## Connection setup

Provide the connection string through the standard configuration key
`ConnectionStrings:DefaultConnection`, using the environment-variable form with a double
underscore:

```text
ConnectionStrings__DefaultConnection
```

Do not commit it to `appsettings*.json` and do not put it in source control.

PowerShell (Windows):

```powershell
$env:ConnectionStrings__DefaultConnection = "Host=<host>;Port=5432;Database=<database>;Username=<user>;Password=<password>;SSL Mode=Require"
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run --project src/FMS.API -- inventory:categories:backfill
```

bash (macOS/Linux):

```bash
export ConnectionStrings__DefaultConnection="Host=<host>;Port=5432;Database=<database>;Username=<user>;Password=<password>;SSL Mode=Require"
export ASPNETCORE_ENVIRONMENT=Development
dotnet run --project src/FMS.API -- inventory:categories:backfill
```

Notes:

- Run from the repository root.
- `ASPNETCORE_ENVIRONMENT=Development` is there because the startup guards for the JWT
  signing key, email, and push configuration run before the command branch. The existing
  Development settings satisfy them; the environment variable above still overrides the
  database connection.
- The command is exactly `inventory:categories:backfill`. There is **no `--apply`** here.
- `dotnet run` builds first; the first run is slower. A connection failure appears as a
  database exception and exits non-zero. That is a failed dry run, not a partial write.

## Expected output

A successful dry run prints:

```text
Inventory category backfill dry run (no changes written).
categories created: 14
items linked: 137
blank-category items left null: 9
  farm 11111111-1111-1111-1111-111111111111: categories=12, linked=120, blank=7
  farm 22222222-2222-2222-2222-222222222222: categories=2, linked=17, blank=2
Re-run with --apply to write these changes.
```

The numbers are placeholders. The totals are the sums of the per-farm rows.

## Dry-run results

**Date:** 2026-10-21

**Environment:** local Windows VM, PostgreSQL 18.6, `localhost:5433/fms_copy`, read-only
role `fms_readonly`. This is a **fresh, empty local database**, created by applying all 13
EF migrations with the admin role. It is **not production**, and no Supabase or Neon
database was read or written.

**Migrations applied:** all 13, from `20260908091002_InitialPostgres` through
`20261020152830_AddInventoryCategories`.

**Checks:**

| Check | Result |
|---|---|
| `20261020152830_AddInventoryCategories` in `__EFMigrationsHistory` | present |
| `InventoryCategories` rows | 0 |
| `InventoryItems` rows | 0 |
| unlinked non-blank `InventoryItems` | 0 |
| duplicate query rows | 0 |

**Duplicate query output:** no rows returned.

**Dry-run output:**

```text
Inventory category backfill dry run (no changes written).
categories created: 0
items linked: 0
blank-category items left null: 0
Re-run with --apply to write these changes.
```

Because the database is empty, these zero counts validate only that the command runs
against the migrated schema. See the real-data results below. No canonical-name proposals
can be made from this empty run, and nothing was renamed or merged. `--apply` was not run.

## Real-data dry run results

**Run date:** 2026-10-22

**Source label:** production snapshot 2026-10-21

**Environment:** local PostgreSQL database `fms_prod_copy` on `localhost:5433`, restored
from a read-only `public`-schema dump of Supabase production. The dump used `pg_dump`
17.11 in custom format, with `--no-owner --no-acl`, and was 246,257 bytes. Production was
read-only throughout; it was not modified, and the dry run itself ran only against the
local copy.

**Snapshot contents:** 3 farms, 3 users, 136 audit log rows, and 0 inventory items.

**Migrations:**

| State | Migrations |
|---|---|
| Present in the snapshot | 11, through `20260930092132_AddSalaryPaymentLedgerFields` |
| Applied locally to `fms_prod_copy` only | `20261017165739_AddUserIsDemo`, `20261020152830_AddInventoryCategories` |
| Present after migration | 13 |

**Counts:**

| Check | Result |
|---|---|
| `InventoryItems` rows | 0 |
| unlinked non-blank `InventoryItems` | 0 |
| `InventoryCategories` rows | 0 |
| duplicate query rows | 0 |

**Duplicate query output:** no rows returned. There are no farm IDs, spellings, or affected
item IDs to report, and no canonical names are proposed.

**Dry-run output:**

```text
Inventory category backfill dry run (no changes written).
categories created: 0
items linked: 0
blank-category items left null: 0
Re-run with --apply to write these changes.
```

The production snapshot has no inventory items, so this run validates the migration path and
the command against real production data but does not exercise non-empty inventory
categories. `--apply` was not run on any database.

## Interpreting the counts

- **categories created** — new `InventoryCategories` rows the apply would insert. Existing
  categories are matched per farm by case-insensitive, trimmed name, so this excludes
  categories that already exist. It also counts a category created earlier in the same run
  only once, which is why two spellings of the same name produce one category, not two.
- **items linked** — items with a non-blank `Category` and a null `CategoryId` that the
  apply would link. This is the number of rows whose `CategoryId` would change.
- **blank-category items left null** — items whose `Category` is null or whitespace. They
  stay unlinked by design and are reported so the total item count can be reconciled.

Reading the result:

- `categories created: 0` and `items linked: 0` with a non-zero blank count means the
  backfill is already applied (or the copy has no categorised items). If you expected work
  to do, check whether `CategoryId` is already populated.
- `categories created` can be much smaller than `items linked`: many items, few distinct
  names.
- **Duplicates**: the command does not print category names, so duplicates show up as
  `categories created` being **lower than the number of distinct raw spellings** in the
  database. Case and leading/trailing whitespace are folded together; the stored spelling
  is the deterministic first one (case-insensitive, then ordinal, on trimmed values). Use
  the optional duplicate query above to see exactly which spellings merge. A merge is
  expected and is what prevents two categories for "Supplies" and "supplies".
- If a farm has no unlinked non-blank items, it may not appear in the per-farm list at all.
- Re-running the same dry run is safe and should produce the same counts unless data
  changed in between.

## What to send back

Send:

1. The complete stdout, including every per-farm line (not a screenshot crop).
2. The target host and database name, and confirmation that it is a non-production copy.
3. The output of the migration query and the two counts (`unlinked_nonblank`,
   `categories`) from the checklist.
4. The optional duplicate query output if any rows come back.
5. `git rev-parse --short HEAD` for the branch that ran.
6. Anything unexpected: non-zero exit, connection error, permission error, or counts that
   do not reconcile with the query above.

## Warning

**Do not run `--apply` until the dry-run output has been reviewed and approved.** The apply
is the writing step:

```text
dotnet run --project src/FMS.API -- inventory:categories:backfill --apply
```

It inserts categories and updates `InventoryItems.CategoryId` for every unlinked non-blank
item. Review the dry-run counts and duplicate findings first, take a backup, and get
explicit approval. Do not run it against production as part of this review.
