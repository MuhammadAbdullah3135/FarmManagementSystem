# Offline support — approved architecture

This is the reviewed design for offline work in the Android wrapper. It is a plan, not a
description of shipped behaviour: read the status line under each increment to see what
actually exists. Nothing here changes the server's authorization model, validation rules or
schema except where it says so explicitly.

## Status

| Increment | What it adds | Status |
|---|---|---|
| 4.5.1 | Offline app shell + local store | **Built** (see below) |
| 4.5.2 | Cached work lists (animal list, weight-check status, tasks, employees) | Not started |
| 4.5.3 | Idempotency foundation — the only schema change in this work | Not started |
| 4.5.4 | Weight recording offline, end to end | Not started |
| 4.5.5 | Attendance check-in and task completion on the same machinery | Not started |
| 4.5.6 | Delta reads and the cache-lifetime policy | Not started |

**4.5.1 shipped:** a versioned IndexedDB store keyed by `(accountId, farmId, collection, id)`
with a per-version migration step, a connectivity store, an offline banner that states what
the device has stored rather than promising a sync that does not exist yet, a service worker
that precaches the built shell (emitted at build time, with the build failing if the shell is
missing), clearing on sign-out and on losing farm access, and the mobile wrapper's overlay
reduced to a genuine shell-load failure. Nothing reads cached farm data yet, and no writes are
queued — pages still fetch.

## Constraints confirmed in the code

These are the facts the design is built on. Each was read out of the source, not assumed.

- **The mobile app is a pure WebView wrapper.** `MainPage.cs` hardcodes the Pages URL, and
  `FMS.Mobile.csproj` references no storage package. Adding a native database would mean a
  capability layer, a JS bridge, and a new APK for every change to the queue — which is why
  the queue lives in the web app instead.
- **`DomStorageEnabled = true`** in `FmsWebViewHandler.cs`, so IndexedDB is available in the
  WebView. The same file sets `CacheMode = CacheModes.NoCache` with a comment claiming that
  means "use the cache but revalidate" — see the device checks below; that comment is wrong
  for Android (`LOAD_NO_CACHE` bypasses the cache) and the interaction with a service worker
  is exactly what the device check exists to settle.
- **The client has no offline story otherwise:** no service worker, no manifest, no
  `indexedDB`, no react-query. Persistence today is `localStorage` only (tokens,
  `activeFarmId`, the zustand auth/farm stores).
- **The write paths are not replay-safe.** `POST …/animals/{id}/weights` builds
  `Id = Guid.NewGuid()` server-side, `WeightRecord` has a *non-unique* `(AnimalId, RecordedAt)`
  index, and nothing accepts an idempotency key — so a retried request creates a second
  weight. Attendance is the opposite: a *unique* `(EmployeeId, Date)` index plus an existing
  upsert keyed on the same pair. Task completion answers `409` when the task is not
  Pending/InProgress.
- **Two paths stamp time server-side.** `CheckInAsync` uses `UtcNow`/`UtcNow.Date` and
  `CompleteTaskAsync` sets `CompletedAt = now`, so an offline check-in made at 06:55 and
  synced at 14:00 would be recorded as 14:00. Carrying the device timestamp is therefore a
  required change for those two workflows, not a nicety. `AddWeightAsync` already accepts a
  client `RecordedAt` (rejecting anything more than 5 minutes in the future), so weight
  recording is the one path that keeps time honestly today.
- **No incremental sync exists:** no `updatedSince`, no ETag, no `If-None-Match`. `CreatedAt`
  and `DeletedAt` exist, but `ModifiedAt` is nullable and hand-stamped in 128 places across 26
  files, and `AuditLogInterceptor` does not stamp it at all — so deltas are possible only
  behind a central guarantee that does not exist yet.
- **Validation has three homes:** FluentValidation validators (auto model-state), inline
  service checks (weight, attendance and tasks have no validators at all), and the extracted
  rule helpers (`AnimalRules`/`EmployeeRules`/`InventoryItemRules`) that 3.4/4.3 built so bulk
  import and single create cannot disagree.

## Decisions

**Local storage: IndexedDB in the WebView, behind a service worker.** The alternative — a
native MAUI SQLite database — would put the cache on the far side of a JS bridge and tie every
change to an APK release, for a device population that has already loaded the app over the
network at least once. IndexedDB ships with the environment the app is already running in.
Costs accepted: storage is only as good as the WebView's implementation (hence the store
degrades to "no cache" rather than throwing), and the cache is cleared by the OS under storage
pressure.

**Offline targets: weight recording, attendance check-in, task completion.** These are what a
field worker does standing next to an animal, and between them they cover the three shapes that
matter — append-only, upsert-keyed and state-machine. Full offline parity (animal creation,
finance, inventory movements) is explicitly not a target: those are desk workflows whose data
requirements would drag half the schema onto the device for no gain.

**Design horizon: a week offline.** A device can plausibly be disconnected for a week; the
existing 30-day refresh token already covers that without a new credential mechanism. Cached
work lists are therefore sized for one farm's active animals, tasks and employees — thousands
of small records, not the full history — and the UI reports staleness rather than hiding it.

**One new server surface.** `POST …/sync/mutations` accepts queued items and applies each by
calling the *existing* service methods. This is the central decision: offline validation cannot
keep up with server rules by being reimplemented, so the server re-validates every queued item
by running the same code path a live request would run. Import (3.4/4.3) already proved that
pattern — one validator, two callers — and this reuses it rather than inventing a second one.

**Idempotency comes before the first queued write.** A client-generated operation id, stored
server-side and unique, so a retry after an ambiguous failure applies once. This is the only
schema change in the whole set of increments, which is why it is its own increment and why
weight recording (4.5.4) does not start until it is in place.

**Conflict resolution, per workflow — stated rather than assumed:**

| Workflow | Strategy | Why |
|---|---|---|
| Weight record | Append-only, never merged | Two measurements taken a day apart are two facts. A queued weight is added; it never overwrites one that arrived later. |
| Attendance check-in | Earliest timestamp wins | The unique `(EmployeeId, Date)` key means one record per employee-day, so the earliest check-in is the truthful one; a later sync must not move a shift's start forward. |
| Task completion | Server state machine wins | Completion is a transition, not a value. If the task was cancelled or reopened while the device was offline, the queued completion is rejected with the server's own 409 message rather than silently forced. |

**A rejected item is quarantined, not dropped and not blocking.** The outbox keeps it with the
server's message so the user can see and fix it; the rest of the queue continues. Silently
discarding a rejected record is data loss, and stalling the whole queue on one bad row is how a
device stays permanently stuck.

**Everything stays farm- and account-scoped.** Every cached key and every queued item carries
both ids, and the sync endpoint applies items under the same farm-context middleware as a live
request — so membership is re-checked at sync time, not trusted from when the item was queued.

## Open questions (deliberately unresolved)

These were not decided because the answer changes what the user experiences, and the right
moment to decide is the increment that needs it:

1. Should the animal lookup be cache-only offline, or should a weight for an animal that is not
   cached be refused?
2. Two weight records with the same timestamp: reject the second, or keep both?
3. Is there a queue cap? If so, what does the app do at the cap — refuse new entries, or refuse
   to go offline?
4. Should the device refuse to accept writes after a staleness threshold, rather than queue them
   against a cache that may be weeks old?

## What cannot be verified in this environment

Handed off as device runbooks rather than claimed — see
[VERIFICATION.md](VERIFICATION.md), item 6:

- whether `CacheModes.NoCache` defeats the service worker on a real Android WebView, and
  whether the setting needs to change;
- relaunching in airplane mode after a cold kill;
- whether a deploy reaches the device within one online launch, given GitHub Pages' 600-second
  `Cache-Control` on `index.html`.

Client tests run in jsdom, which has no IndexedDB — the suite installs `fake-indexeddb`, and
the precache manifest is asserted at build time rather than by a browser run.
