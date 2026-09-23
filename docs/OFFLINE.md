# Offline support — approved architecture

This is the reviewed design for offline work in the Android wrapper. It is a plan, not a
description of shipped behaviour: read the status line under each increment to see what
actually exists. Nothing here changes the server's authorization model, validation rules or
schema except where it says so explicitly.

## Status

| Increment | What it adds | Status |
|---|---|---|
| 4.5.1 | Offline app shell + local store | **Built** (see below) |
| 4.5.2 | Cached read-only work lists (weight-check status, tasks first page, employees) | **Built** (see below) |
| 4.5.3 | Idempotency foundation — the only schema change in this work | **Built** (see below) |
| 4.5.4 | Weight recording offline, end to end | **Built** (see below) |
| 4.5.5 | Attendance check-in and task completion on the same machinery | **Built** (see below) |
| 4.5.6 | Delta reads and the cache-lifetime policy | **Built** (see below) |

**4.5.1 shipped:** a versioned IndexedDB store keyed by `(accountId, farmId, collection, id)`
with a per-version migration step, a connectivity store, an offline banner that states what
the device has stored rather than promising a sync that does not exist yet, a service worker
that precaches the built shell (emitted at build time, with the build failing if the shell is
missing), clearing on sign-out and on losing farm access, and the mobile wrapper's overlay
reduced to a genuine shell-load failure. Nothing reads cached farm data yet, and no writes are
queued — pages still fetch.

**4.5.1 close-out decisions (2026-09-23):**

- **One shared database, not one per account.** The proposal's "one IndexedDB database per
  account" wording was deliberately refined to a single `fms-offline` database whose every key
  carries the compound `(accountId, farmId, …)` path — the same isolation, enforced structurally
  (a read cannot cross scopes even in principle) and asserted by tests, with one migration path
  instead of one per account.
- **The service worker's same-origin handling is an allowlist, not a catch-all.** Only the
  precache manifest and Vite's hashed asset prefix are ever served or stored; every other
  same-origin request bypasses the worker entirely. This is the enforced form of the no-API
  rule: the production API is cross-origin so its responses could never be cached anyway, but
  the previous catch-all runtime cache demonstrably stored a same-origin `/api` 200 in a browser
  smoke test — which is exactly the leak shape the constraint exists to prevent (the dev proxy
  runs a same-origin API). Found by test, fixed by default-deny; both are now asserted in the
  suite.

**4.5.2 shipped:** cached read-only work lists behind one `useCachedQuery` hook. The cache is
served first (and is the only source when offline), then revalidated online — stale-while-
revalidate — and written back through 4.5.1's `replaceCollection`, which stamps each
collection's `lastSyncedAt`. Every cached view carries a `Synced <age>` label, so a view never
pretends to be live. Three collections only, enumerated in a registry the hook validates
against: weight-check status (`all`), the first page of the task list (`firstPage`), and
employees (`listPage1` and the `options` shape used by the task assignee selector). Searches,
later pages and filtered views are deliberately never cached — they fetch through, so the
device does not accumulate a copy of the list per keystroke.

Rows carry the identity of the view they were read for — account, farm, query and its
parameters — and only rows whose identity matches the *current* view are rendered. That guard
is applied during render, not in an effect, so switching farms cannot show the previous farm's
rows for even one frame while the new farm's cache read is in flight.

**Deferred from 4.5.2:** the animal list. The 4.5 sizing note named it alongside these three;
the increment was scoped to weight-check status, tasks and employees (the read-only surfaces a
field worker opens with the device in hand). The animal list, and the animal lookups the 4.5.4
weight-recording path needs, come with that work rather than here.

**Not covered offline by design:** a filtered or later page with no cached copy renders empty
rather than showing a cached page that does not answer the query. A cold offline launch of the
animal detail page still redirects, because the animal record itself is not a cached collection
in this increment; the weights tab works from cache once the page is open.

**4.5.3 shipped:** the server side of queued work, and the only schema change in the whole set.
`POST /api/farm/{farmId}/sync/mutations` takes a batch of items and applies each one by calling
the workflow's own service method — `AddWeightAsync`, `CheckInAsync`/`CheckOutAsync`,
`CompleteTaskAsync` — with the route's farm id, so validation, farm scoping and message text
cannot drift from the live endpoints (asserted by comparing the two answers for the same input).
Three nullable `ClientMutationId` columns (weights, attendance, `FarmTasks.CompletionClientMutationId`)
behind filtered unique `(FarmId, ClientMutationId)` indexes, plus a `ProcessedMutation` ledger that
stores each applied mutation's original result; a retry is answered from the ledger byte for byte,
and a retry that races past it (or one whose ledger row died with the process) is refused by the
database and reported as already applied. Check-in, check-out and completion now accept a
device-supplied timestamp, validated against the 5-minute future tolerance the weight endpoint
already had, extracted into one shared rule so all three read the same standard.

Item outcomes are `Accepted`, `Superseded` and `Rejected` (the enum names the API writes for every other enum), with the import pipeline's result shape
(`requestedCount`/`successCount`/`failures[]`) plus a per-item list. Each item runs in its own DI
scope: a workflow that throws is reported as a rejected item without leaking the exception and
without stopping the ones after it — a queue that stalls on one bad row is how a device stays
permanently stuck.

**4.5.3 close-out decisions (2026-09-23):**

- **Idempotency is confined to the sync endpoint.** The mutation id is a service-method parameter,
  not a field of the live request DTOs, so `POST …/weights`, `…/check-in` and `…/complete` keep
  exactly their previous contract and behaviour; a duplicate id cannot reach the unique index
  through a live request. 4.5.4's client writes through the queue, so live duplication is not a
  path that exists.
- **An attendance conflict is superseded, not replayed forward.** The day already has a record, so
  the queued check-in is reported as superseded with the server's message and nothing is written.
  The other half of "earliest timestamp wins" — a queued check-in that is *earlier* than the stored
  one being moved into place through the existing `UpsertAttendanceAsync` — is attendance workflow
  policy and lands with 4.5.5, where the attendance UI owns it. Until then no sync moves a shift's
  start forward, which is what mattered.
- **Rejected and superseded items are not recorded as processed.** Neither wrote anything, so
  re-sending one re-derives the same answer while the state that produced it stands, and can still
  apply if that state is undone (a manager reopening a task is the test). Only an applied mutation
  is on the ledger — which is also what makes the "send the same batch twice" guarantee hold for
  the items that actually changed something.
- **The write order is apply, then record**, so the only possible crash window is "applied but
  unrecorded". The target row's own unique index catches that on the next attempt, and the endpoint
  answers "already applied" and records that answer, so no retry ever produces a second record.
  Recording first would have allowed the opposite — a ledger entry for work that never happened,
  which a replay would report as done: silent data loss.
- **A stale 4.5 finding, corrected.** The investigation recorded that weights, attendance and tasks
  have "no validators at all". Weights do: `CreateWeightRecordRequestValidator` mirrors the
  service's weight and timestamp rules word for word, which is why a test now asserts the two
  agree (message *and* the tolerance boundary) rather than assuming it. Attendance and task
  completion genuinely have none, so for those the service's inline checks are the only rule.

**4.5.4 shipped:** the client half — the write queue and the first workflow that uses it.
`fms-offline` moves to schema version 2 with an `outbox` store isolated exactly like the read
cache (compound `(accountId, farmId, mutationId)` key, so a flush cannot even address another
account's queue). `outbox.ts` is the only writer: items are appended and only ever have their
`status`/`attempts`/`lastError`/`serverMessage`/`appliedAt` fields updated, never their payload.
Weight recording now goes through the queue whether or not the device is online — the Record
weight screen enqueues, shows the row immediately as *Waiting to sync*, and the server's own row
replaces it on `accepted` (or the item is quarantined with the server's message).

The flush engine (`syncEngine.ts`) runs on the triggers the architecture names and no timer of
its own: the `online` event, window/app focus, after any successful request (the existing axios
success chokepoint), a successful token refresh, and an explicit "Sync now". It refreshes the
access token first if it is within two minutes of expiry, sends pending items oldest-first in
batches of at most `MAX_ITEMS_PER_REQUEST` (200, the server's own limit), sequential per farm,
paced `BATCH_SPACING_MS` (500 ms) apart — a pace chosen against the real limiter arithmetic, see
below — handles 2xx per item, 401 by refreshing once and then stopping with the items left in
place, 429/5xx with exponential backoff plus jitter capped at five minutes and honouring the
server's `retryAfter`, and 4xx per item by quarantining that item and continuing with the rest.
There is no polling; a single scheduled retry timer exists only while a backoff is pending.

A new Sync status screen (`/dashboard/records/sync`) lists each farm's queue with counts, labels
the animal from the cached lookup where one exists (falling back to the id where it does not),
and offers Retry and Dismiss-with-reason on quarantined items. Dismissing keeps the record and
the reason — nothing is ever silently dropped. A header badge shows the pending count.

**4.5.4 close-out decisions (2026-09-23):**

- **Cache-only animal lookup, and no hand-typed tag at sync time** (open question 1). The picker
  reads the device's stored `animals@lookup` collection; it does not fetch. With nothing cached
  the screen says so and offers no picker, rather than accepting a tag the server would have to
  resolve later — a tag typed offline can be wrong (renamed, reassigned) and the failure would
  surface hours later as a quarantined row with no way to tell what the user meant.
- **Two weights at the same timestamp are both kept** (open question 2). The queue never merges
  items and the server's `(AnimalId, RecordedAt)` index is non-unique, so an idempotent retry is
  protected by the mutation id while two genuine measurements remain two measurements.
- **The queue survives sign-out; the read cache does not.** `clearAll` was split into
  `clearCachedData` (the cache and its metadata) and `clearQueue`, and only the former runs on
  sign-out, so unsynced field work is not destroyed by a session ending — the user is warned
  about the outstanding count instead. "Clear offline data" in the banner invokes both, because
  that is an explicit request to discard the device's copies. Signing in as a different account
  cannot flush the previous account's queue: the engine reads the queue for the *token's* account
  and every key is account-scoped, so the other account's items are not addressable.
- **The rate limiter is per client IP, not per user, and the pacing is calculated against that.**
  `UseRateLimiter()` sits before `UseAuthentication()`, so the partition key at that point is the
  remote address: a whole field team behind one NAT shares the 300-per-minute budget, and there is
  no `Retry-After` header to obey (the 429 body carries `retryAfter: 60`). A week-old queue of 450
  weights therefore flushes as three paced batches of 200/200/50 rather than a burst — three
  requests against a 300-per-minute budget — and the engine reads `retryAfter` from the body when
  the budget is exhausted.
- **A revoked farm's queued items are kept, not sent and not deleted.** Farm-context enforcement
  on `/sync/mutations` re-checks membership at sync time, so items for a farm the user no longer
  belongs to are refused by the server the same way a live request would be — the queue does not
  trust the membership it had when the item was captured. The items stay visible on the sync
  screen rather than vanishing on a revocation the user may not have caused.
- **The animal lookup gained a dedicated cached collection separate from the paged list.** The
  picker needs one bounded farm-wide list (`animals@lookup`, `GET /animals?page=1&pageSize=100`),
  and reusing the paged list's key would have meant a stale page size answering a different
  question. Its cache lifetime is the shorter of the two, because a wrong animal on a weight is
  worse than a stale page.

**4.5.5 shipped:** the other two field workflows, entirely by registration onto 4.5.4's machinery —
**no new route, no new collection of machinery, and no migration** (`dotnet ef migrations
has-pending-model-changes` reports no change to the model). Three kinds were declared in the one
place kinds are declared (`attendance.checkIn`, `attendance.checkOut`, `task.complete`), the
attendance register became a cached work list (`attendance@firstPage`), and the check-in,
check-out and complete buttons now queue and flush exactly as the weight screen does — one write
path, the device's own time, the server's answer replacing the row.

The half of "earliest check-in wins" that 4.5.3 deferred is now implemented: a queued check-in
**earlier** than the stored one moves the stored `CheckInAt` back (accepted, and recorded on the
ledger because it wrote something), and a later one changes nothing and is reported
`Superseded`. Check-out is the mirror — a later device time moves `CheckOutAt` forward — and
task completion is "already done is done", with the two genuinely different states surfaced
instead: see the decisions below. `docs/API.md` carries the full per-workflow table.

One new outcome code, `Error.Superseded`, is what makes "your intent is already satisfied"
sayable by a service. It exists because both cases have always answered 409 over HTTP, and a
queue that had to guess between "already done" and "refused" by reading message text would
break the next time a message was reworded. The live controllers map it to the same 409, so no
status code moved.

**4.5.5 close-out decisions (2026-09-23):**

- **A day somebody entered by hand is never rewritten by a device.** A record with no check-in
  time (a Leave, an Absent) has no time for "earliest wins" to beat, and it says something a
  clock cannot overturn; the queued check-in is reported superseded with the status that stands
  named in the message, so a person decides. The alternative — letting a queued check-in flip
  an admin's Leave to Present — would make the device the authority on a human's judgement.
- **Only a device's own time may move a stored time.** A live request's `occurredAt` is the
  server's clock at arrival, not evidence about when a shift started or ended. So the rewind and
  the latest-wins update require a supplied time, and a live check-in or check-out on an
  occupied day is the conflict it has always been — wording included. This is also what keeps
  the two existing single-record tests (`CheckIn_Succeeds_AndDuplicateReturnsConflict`,
  `CheckOut_ComputesHours_AndDoubleCheckoutReturnsConflict`) passing unchanged: they exercise
  the live path, and the live path did not move.
- **Already complete is done; a real disagreement is not.** A completion for a task that is
  already complete with equivalent notes (absent ≡ blank), or by the same queued mutation, is
  `Superseded`: nothing is written twice and the device clears the item. When the notes differ,
  the record and the device disagree about a fact, so it is `Rejected` with the server's message
  and the recorded notes left untouched — surfaced to a person rather than overwriting either
  side. This is decided on *notes differing*, regardless of who completed it: the disagreement is
  the signal, not the author. (4.5.3 recorded the opposite classification for the no-notes case,
  which is why one 4.5.3 test was re-pointed at the case that still rejects — see below.)
- **`start`, `cancel`, `reopen`, task creation/editing and the manual attendance entry stay live
  requests.** They are desk actions: a manual entry is an administrative correction, and the
  architecture's offline targets are the check-in, the check-out and the completion.
- **The device never refuses a write because its cache is stale.** Out and Complete stay enabled
  offline; the server's message is what tells the truth if one turns out to be wrong. The
  ordering property that actually matters is already guaranteed — the flush is oldest-first, so
  a check-in queued before a check-out reaches the server first and the day exists by the time
  the check-out is applied.
- **One 4.5.3 acceptance test was re-pointed, deliberately.**
  `TaskCompletionConflict_ReportsWhatTheSingleRecordEndpointReports` used a completed task with
  no notes and asserted `Rejected` with "Open tasks only can be completed" — precisely the
  classification this increment changes. It now asserts the message-parity guarantee for the
  case that *does* still reject (a completion whose notes disagree with the record), and the
  old case is covered by `CompletionForAnAlreadyCompletedTask_IsApplied_NotRejected`. The
  guarantee the test exists to protect is unchanged: the same input gets the same message from
  the live endpoint and from a queued item.

**4.5.6 shipped:** the three cached collections can now be read as *deltas*, and the device has
an explicit lifetime policy.

Deltas rest on one new guarantee: `FmsDbContext` stamps `CreatedAt` on insert (only when unset)
and `ModifiedAt` on every modification, centrally — the context rather than the ~128 services,
because a row written without a timestamp is a row no device will ever learn about, and a soft
delete (a modification that only sets `IsDeleted`) is exactly the write a service performs
without touching `ModifiedAt`. `?updatedSince=` on the task list, the employee list and the
weight-check status then returns only what changed, plus — and this is the part a filter cannot
produce — the **ids of rows that were removed**: hard deletes are named from the audit log,
which already records every `(farm, entity type, entity id, time)` on delete and is indexed on
`(FarmId, Timestamp)`; soft-deleted employees name themselves. The reply also carries the
server's own `cursor`, which the client stores and sends back, and `requiresFullSync` for a
cursor the server cannot answer precisely (older than 30 days, in the future, or a change the
delta cannot narrow — such as an edited schedule in the computed weight-check projection).

On the client the cursor lives in the collection's bookkeeping, a delta is *merged* rather than
replacing the collection, and the tombstones are applied in the same IndexedDB transaction that
stores the new cursor — a tombstone that arrived but was not applied before the app died would
otherwise be lost forever. The first read of a collection stores the cursor it was given, so the
second read is a delta; a response that demands a full sync is followed by exactly one full read
and a replace.

The lifetime policy ties the device's offline-write window to the 30-day refresh-token ceiling:
warn at five days without reaching the API, refuse new offline writes at seven. It is driven by a
per-account `lastServerContactAt` marker written from the one chokepoint every successful
request passes through, so it measures whether the session can still deliver what the device
holds rather than what the device's clock says. The queue also has a hard cap of 5,000 items
with a warning from 4,500; both refusals come back through the one typed result the three write
screens already render, so a page can say *why* it refused instead of blaming storage.

**4.5.6 close-out decisions (2026-09-23):**

- **The stamp is the context's, not the caller's.** `SaveChanges`/`SaveChangesAsync` stamp the
  change tracker, and the audit interceptor's nested save is explicitly skipped so one write
  produces one timestamp — the row and the audit payload cannot disagree about the same write.
  A deliberate `CreatedAt` (an import, a seeder, a backfill) is never overwritten.
- **Tombstones come from the audit log, not from soft-deleting everything.** Making every
  cacheable entity soft-deletable would put a `!IsDeleted` condition in every query that reads
  it, where missing one resurrects a deleted row in a list. One indexed read of the audit log
  names a deleted row for any entity, including ones added later. Soft-deleted entities need no
  help: their own row is the tombstone.
- **A delta is bounded, not paged.** Paging one would require the client to walk every page
  before it could advance its cursor, and getting that wrong drops whatever changed between two
  pages forever. Past `DeltaCursor.MaxDeltaRows` (500) the answer is "read the collection in
  full", which is also smaller on the wire than the deltas it replaces.
- **One API response shape changed, deliberately.** `GET …/weight-schedules/status` returned a
  bare array and now returns the same envelope the other cached collections do. It has to: a
  client cannot be sent a delta without a server-owned cursor to send back, and there is nowhere
  in a bare array to put one. The one client caller reads `items`; `status/overdue` (the
  notification job's own read, not a cached collection) is unchanged.
- **A merged cached page can hold more rows than its page size.** A delta reports every changed
  row, including ones that would sort onto another page, and the alternative — dropping changes
  to rows the device does not hold — would keep a device showing a task that was edited last
  week. The rows are what they are; the freshness label says when they came from. A device whose
  cached view has grown this way is a device that has been offline a long time, which is exactly
  the case the freshness label exists for.
- **The read cache is disposable; the session marker is not data at all.** "Clear offline data"
  empties the rows, the bookkeeping and the queue, and deliberately keeps `lastServerContactAt`:
  if clearing reset it, clearing would be the way to keep recording past the window that protects
  the queue.
- **A view with rows on screen is no longer "loading".** `useCachedQuery.isLoading` now means
  "nothing to show yet" rather than "a request is in flight". The pages put it on a table's
  spinner, and an antd spinner blur makes the rows inert — so a background revalidation of data
  the device already has must not make the screen unclickable. This is the stale-while-revalidate
  behaviour the hook documented from the start, made true for the views that use it.
- **The deleted-animal fix changes two existing behaviours, deliberately.** The weight-check
  projection no longer lists soft-deleted animals and no longer re-creates their weight-check
  tasks on every read. Before this, a farm with deleted animals accumulated "Weight check due:
  \<tag\>" tasks nobody could act on. The dashboard's health alert and the notification
  dispatcher inherit the fix, because they read the same projection.

## Phase 5 — outcomes, and what is still owed

| Subphase | Outcome | Evidence |
|---|---|---|
| 5.1 | Offline shell + local store: service worker precaching the hashed bundle (never an API response), versioned IndexedDB with per-version migration, cache clearing on sign-out and farm denial, the MAUI overlay reduced to shell-load failure | see 4.5.1 |
| 5.2 | Cached read-only work lists behind one hook, freshness labels, render-time farm guard | see 4.5.2 |
| 5.3 | The one migration of the phase: `ClientMutationId` columns + the processed-mutation ledger, and `POST …/sync/mutations` applying each item through the *existing* service methods | see 4.5.3 |
| 5.4 | Weight recording offline end to end: the outbox, the flush algorithm, optimistic rows, quarantine, the sync screen | see 4.5.4 |
| 5.5 | Attendance and task completion on the same machinery, with the three conflict rules | see 4.5.5 |
| 5.6 | Delta reads with tombstones, the central modification stamp, the offline-write window and the queue cap | see 4.5.6 |

Two things in Phase 5 cannot be proven in this environment and are handed back as device
runbooks (see [VERIFICATION.md](VERIFICATION.md), item 6):

1. **`CacheModes.NoCache` × the service worker** — whether the Android WebView setting defeats the
   worker, and whether it needs to change. Nothing in this repository can exercise it; the
   architecture's constraint is instead enforced by the worker's same-origin allowlist and
   asserted by test.
2. **Surviving an app kill** — whether a queue and a cache survive the WebView's IndexedDB being
   torn down by the OS, including after 5.6's delta reads and with all three workflows queued.

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
  Pending/InProgress. **Changed by 4.5.3 for the queued path only:** each write target now carries
  the sync mutation's id behind a unique index, and the ledger replays the original result. The
  live endpoints still accept no idempotency key and behave exactly as described here.
- **Two paths stamp time server-side.** `CheckInAsync` uses `UtcNow`/`UtcNow.Date` and
  `CompleteTaskAsync` sets `CompletedAt = now`, so an offline check-in made at 06:55 and
  synced at 14:00 would be recorded as 14:00. Carrying the device timestamp is therefore a
  required change for those two workflows, not a nicety. `AddWeightAsync` already accepts a
  client `RecordedAt` (rejecting anything more than 5 minutes in the future), so weight
  recording is the one path that keeps time honestly today. **Changed by 4.5.3:** check-in,
  check-out and completion accept an optional device timestamp too, validated against that same
  tolerance (now one rule, `MutationTimestampRules`). With no timestamp — every live request —
  the server still stamps its own clock, unchanged.
- **No incremental sync exists:** no `updatedSince`, no ETag, no `If-None-Match`. `CreatedAt`
  and `DeletedAt` exist, but `ModifiedAt` is nullable and hand-stamped in 128 places across 26
  files, and `AuditLogInterceptor` does not stamp it at all — so deltas are possible only
  behind a central guarantee that does not exist yet. **Changed by 4.5.6:** re-confirmed before
  planning (the 128 hand-stamps were still there, and the interceptor still did not stamp), and
  then fixed rather than worked around — `FmsDbContext` now stamps both timestamps centrally, so
  a delta no longer rests on 128 call sites being correct. The three cached collections carry
  `updatedSince`; everything else still reads in full.
- **Validation has three homes:** FluentValidation validators (auto model-state), inline
  service checks, and the extracted rule helpers (`AnimalRules`/`EmployeeRules`/`InventoryItemRules`)
  that 3.4/4.3 built so bulk import and single create cannot disagree. Corrected in 4.5.3: weights
  *do* have a validator (`CreateWeightRecordRequestValidator`), mirroring the service's own weight
  and timestamp rules; attendance and task completion genuinely have none, so for those the inline
  service checks are the only rule. A queued item never passes through model validation, so the
  sync path reaches the service checks in every case — which is why the weight validator's text is
  asserted equal to the shared rule's rather than presumed.

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
weight recording (4.5.4) does not start until it is in place. *(Built in 4.5.3: the id is
stamped on the row the workflow wrote, behind a farm-scoped unique index, and the
`ProcessedMutation` ledger replays the original result — see the 4.5.3 sections above.)*

**Conflict resolution, per workflow — stated rather than assumed:**

| Workflow | Strategy | Why |
|---|---|---|
| Weight record | Append-only, never merged | Two measurements taken a day apart are two facts. A queued weight is added; it never overwrites one that arrived later. |
| Attendance check-in | Earliest timestamp wins | The unique `(EmployeeId, Date)` key means one record per employee-day, so the earliest check-in is the truthful one; a later sync must not move a shift's start forward. A day entered by hand is never rewritten at all. *(Fully built in 4.5.5: the rewind half landed there, the superseded half in 4.5.3.)* |
| Attendance check-out | Latest timestamp wins | The mirror rule: the row records when the shift actually ended, so a device reporting a later time moves it forward. *(Built in 4.5.5.)* |
| Task completion | Server state machine wins, and already-done is done | Completion is a transition, not a value. A cancelled task's queued completion is rejected with the server's own message rather than silently forced; one the task's state already satisfies is reported done; and differing notes are a disagreement between the record and the device, surfaced for a person. *(The classification landed in 4.5.5; 4.5.3 built the idempotency it rests on.)* |

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

1. ~~Should the animal lookup be cache-only offline, or should a weight for an animal that is not
   cached be refused?~~ **Answered in 4.5.4: cache-only; an uncached animal is not offered at all.**
2. ~~Two weight records with the same timestamp: reject the second, or keep both?~~ **Answered in
   4.5.4: keep both.**
3. ~~Is there a queue cap? If so, what does the app do at the cap — refuse new entries, or refuse
   to go offline?~~ **Answered in 4.5.6: 5,000 items, warn from 4,500, refuse past the cap.** The
   queue is refused, not the device: everything already recorded stays deliverable, and the
   sentence the write screens show says how many records are held and to sync.
4. ~~Should the device refuse to accept writes after a staleness threshold, rather than queue
   them against a cache that may be weeks old?~~ **Answered in 4.5.6: warn at five days without
   reaching the API, refuse new offline writes at seven.** The threshold is about the *session*
   (the refresh token's 30-day ceiling), not the age of the cache — 4.5.5's decision stands: the
   age of the data alone never refuses a write. The stop comes well before the token can expire,
   so the records already queued remain deliverable.

## What cannot be verified in this environment

Handed off as device runbooks rather than claimed — see
[VERIFICATION.md](VERIFICATION.md), item 6:

- whether `CacheModes.NoCache` defeats the service worker on a real Android WebView, and
  whether the setting needs to change;
- relaunching in airplane mode after a cold kill;
- whether a deploy reaches the device within one online launch, given GitHub Pages' 600-second
  `Cache-Control` on `index.html`;
- whether a **queued** write survives a cold kill and flushes exactly once, and whether the
  WebView's IndexedDB keeps the outbox across an OS-initiated process death (runbook item 6d);
- whether the device capture timestamps survive that round trip (the jsdom suite proves the
  payload carries them; only a device proves the stored row keeps them);
- whether the offline-write window and the queue cap behave on a real device. Both are proven
  in suites here, but the device runbook (VERIFICATION.md item 6f) exercises the window the way a
  user meets it, by editing the marker rather than by waiting seven days; the cap is asserted in
  the suites only, and VERIFICATION.md says so rather than pretending otherwise;
- whether two devices on one farm see each other's delta — one edits an employee, one deletes a
  task, and the other device's cached list follows without a full reload (runbook item 6g).

The idempotency unique indexes are in the same category: the InMemory provider the unit and E2E
suites run on ignores unique indexes entirely, so the race they prevent — a retry that races the
ledger, or one whose ledger row died with the process — is proven only against a real PostgreSQL
server. Two tests do exactly that and SKIP without one (see [VERIFICATION.md](VERIFICATION.md),
item 1, which runs them along with the other PostgreSQL-gated suites).

Client tests run in jsdom, which has no IndexedDB — the suite installs `fake-indexeddb`, and
the precache manifest is asserted at build time rather than by a browser run.
