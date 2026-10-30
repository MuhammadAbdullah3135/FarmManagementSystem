# Follow-ups

Open items from the security fix and demo-data work. None of these block the current
deployments; they are tracked here so they are not lost.

## Merged

- PR #5 (`fix/auth-empty-credentials`) merged into `main` at `9ac8d8d`.
- PR #6 (`feature/demo-seed`) merged into `main` at `f58745f`.
- GitHub Pages redeployed successfully from `f58745f`.

## 1. `ReportService` counts sold animals as mortality

`ReportService.GetAnimalReportAsync` currently counts every animal whose status category is
`Terminal`:

```csharp
var mortalityCount = await _db.Animals
    .CountAsync(a => a.FarmId == farmId
        && a.AnimalStatus.Category == AnimalStatusCategory.Terminal
        && !a.IsDeleted);
```

`Sold` and `Deceased` are both terminal statuses, so a farm with 3 sold and 2 deceased animals
reports **5 deaths**. The expanded demo dataset makes this visible: it has 3 sold and 2
deceased animals, so the mortality tile reads 5.

**Proposed fix:** count only the deceased status. The smallest safe change is to filter on the
status name (`a.AnimalStatus.Name == "Deceased"`), which matches the status seeded by
`FarmDefaultsSeeder`. If farms can rename statuses or add custom terminal statuses, add an
explicit `CountsAsMortality` (or equivalent) flag to `AnimalStatus` and filter on that instead;
that requires a migration and is the more durable option. Add a report test with both sold and
deceased animals so the distinction is locked down.

## 2. Milk and egg production do not exist in the schema

The demo seeder currently has no milk or egg production records because the domain has no
`MilkProduction` or `EggProduction` entity. The approved demo dataset intentionally omits them
rather than inventing tables.

**Proposed task:** add production entities (farm, animal/flock, production date, quantity,
unit), EF Core configurations and migrations, API endpoints, reports, client screens, and
demo-seed records. Keep this separate from the demo expansion because it is a schema and
feature change, not seed data.

## 3. Real-account production login is still unverified

The production `fms-api` deployment was verified for the security fix (empty and whitespace
logins return `400`, health checks are green) and the demo endpoints still return `410`, but a
normal login with a real production account could not be exercised because no production
credentials were available.

**Recommendation:** with a known real user, verify `POST /api/auth/login` succeeds, `/auth/me`
returns that user, an expired access token refreshes successfully, and logout/revocation works.
Do this after the JWT rotation so the verification uses a fresh token.

## 4. JWT rotation invalidated existing sessions

The production `Jwt__SecretKey` was rotated as part of the security incident response. Every
access token and refresh token signed with the old secret is now invalid, so all users must
sign in again. This is expected, not a defect. Support notes should tell users to sign in
again if they are unexpectedly logged out; no data was lost.

A follow-up for the next rotation: announce the rotation window in advance and verify a
real-account login immediately afterwards (see item 3).

## 5. Demo dashboard shows 0 sick animals — PR open

`DashboardService` defines its sick metric as animals whose status category is `Inactive`.
The demo seeder creates the `Sick` status through `EnsureAnimalStatusAsync`, which leaves the
category at its default `Active`, so the dashboard tile reads **0** even though the approved
dataset contains 3 sick animals. The animals list and status breakdown show all 3 correctly.

**Status:** PR open — https://github.com/MuhammadAbdullah3135/FarmManagementSystem/pull/7.
The seeder now creates the demo `Sick` status in the `Inactive` category and a regression test
asserts the dashboard's sick-category count is 3. The PR is intentionally unmerged pending
review.
