# Test-hang investigation: the parallel backend run deadlock

## Summary

The combined backend run (`dotnet test tests/FMS.Domain.Tests`) used to stall forever when
xUnit ran test collections in parallel. The fix (commit `a093a1e`) was to serialize
collections via `xunit.runner.json`. This document records the follow-up investigation that
found the actual cause, the fix, and how to reproduce the original hang.

**Root cause:** two PostgreSQL integration fixtures probed for a reachable server with
`GetAwaiter().GetResult()` in their **constructors**. xUnit constructs class fixtures on its
bounded `MaxConcurrencySyncContext`, and the probe's `await`s captured that same context.
Blocking a worker while the awaited continuation needs that same worker starves the context
once enough fixtures block at once, so nothing finishes.

It was first worked around by running the probe on the thread pool via `Task.Run` (`e945431`),
and then fixed properly by moving the probe to xUnit's `IAsyncLifetime.InitializeAsync`
(`6eb32a6`), which xUnit awaits before the class's tests run — so no fixture-construction
worker blocks at all. The serialization workaround (`a093a1e`) is gone (`8b92298`).

## Symptoms

- Full run: `dotnet test tests/FMS.Domain.Tests/FMS.Domain.Tests.csproj` never finishes;
  the test host stalls with many host-based tests in flight (~25 in flight, 17 of them
  `E2E`/`TestServer`).
- Each filter partition passes on its own:
  - `--filter "FullyQualifiedName!~FMS.Domain.Tests.E2E"`
  - `--filter "FullyQualifiedName~FMS.Domain.Tests.E2E"`
- `parallelizeTestCollections: false` made the combined run pass (~2m23s), which is why it was
  shipped as a workaround.

## How it was diagnosed

`--blame-hang` reported no single stuck test — only an inactivity timeout with many tests in
flight — so the process was dumped and its managed thread stacks were inspected.

```powershell
# Reproduce the hang with collections in parallel, and capture a mini dump.
dotnet test tests/FMS.Domain.Tests/FMS.Domain.Tests.csproj `
  --blame-hang --blame-hang-timeout 120s --blame-hang-dump-type mini
```

The run aborts after the timeout (it reported `Failed: 0, Passed: 658, Skipped: 1,
Total: 659` before aborting) and writes `TestResults/<guid>/testhost_*_hangdump.dmp`.

```powershell
# Install dotnet-dump if needed, then inspect the dump.
dotnet tool install --global dotnet-dump
dotnet-dump analyze <path-to>\testhost_*_hangdump.dmp -c "clrthreads" -c "clrstack -all" -c "exit"
```

### Evidence

`clrthreads` showed 21 threads, all in `Preemptive` GC mode (nothing running managed code).
`clrstack -all` showed OS threads blocked in a managed wait:

```
System.Threading.Tasks.Task.SpinThenBlockingWait(...)
System.Runtime.CompilerServices.TaskAwaiter`1[[System.String, ...]].GetResult()
FMS.Domain.Tests.E2E.PostgresIntegrationFactory..ctor()        [PostgresIntegrationFactory.cs:41]
...
Xunit.Sdk.MaxConcurrencySyncContext.RunOnSyncContext(...)
Xunit.Sdk.MaxConcurrencySyncContext.WorkerThreadProc()
```

and a second:

```
System.Threading.Tasks.Task.SpinThenBlockingWait(...)
System.Runtime.CompilerServices.TaskAwaiter`1[[System.String, ...]].GetResult()
FMS.Domain.Tests.Jobs.HangfirePostgresIntegrationTests+PostgresDatabaseFixture..ctor()
                                                              [HangfirePostgresIntegrationTests.cs:58]
...
Xunit.Sdk.MaxConcurrencySyncContext.RunOnSyncContext(...)
```

Both stacks bottom out in xUnit's `MaxConcurrencySyncContext`, and the frames above
`GetResult()` are `XunitTestClassRunner.CreateClassFixture`, i.e. the blocking happens while
xUnit is **constructing class fixtures** on its bounded sync-context workers.

### Why it deadlocks

`MaxConcurrencySyncContext` throttles parallelism with a bounded set of worker threads. A
fixture constructor that blocks with `GetAwaiter().GetResult()` holds one worker; the probe's
`await conn.OpenAsync()` continuation was captured to that same context and is only posted
back to a `MaxConcurrencySyncContext` worker. When enough fixtures block at the same time
(the two PostgreSQL fixtures are reached by four test classes:
`DashboardPostgresIntegrationTests`, `IdempotencyIndexPostgresIntegrationTests`,
`FarmExportLargeFarmPostgresTests` via `Factory`, and `HangfirePostgresIntegrationTests`),
the context has no free worker to run the continuations it is waiting on — a classic
sync-over-async deadlock on a single-threaded/limited `SynchronizationContext`.

This also explains the intermittency and why serialization hid it: with collections
serialized, at most one blocking fixture exists at a time, so a worker is always free to run
the continuation.

## The fixes

The probe lived in the constructors of two fixtures:

- `tests/FMS.Domain.Tests/E2E/PostgresIntegrationFactory.cs`
- `tests/FMS.Domain.Tests/Jobs/HangfirePostgresIntegrationTests.cs` — `PostgresDatabaseFixture`

**1. Workaround (`e945431`).** Run the probe on the thread pool so its continuations never
need the xUnit sync context:

```csharp
UnavailableReason = Task.Run(ProbeAndPrepareAsync).GetAwaiter().GetResult();
```

`Task.Run` runs the probe with `SynchronizationContext.Current == null`, so its `await`s post
continuations to the thread pool and the blocking `GetResult()` always unblocks. It removes the
deadlock but still blocks a thread in the constructor.

**2. Proper fix (`6eb32a6`).** Implement `IAsyncLifetime` and move the probe into
`InitializeAsync`, which xUnit awaits (`XunitTestClassRunner.CreateClassFixtureAsync`) before
the class's tests run. `UnavailableReason` is set by then, so `Skip.If` still skips the
Postgres-gated tests when no server is reachable. Cleanup moves behind an idempotency guard in
`Dispose`, because xUnit calls **both** `DisposeAsync` and `Dispose` for a fixture that
implements both `IAsyncLifetime` and `IDisposable`.

```csharp
public async Task InitializeAsync() => UnavailableReason = await ProbeAndPrepareAsync();

public Task DisposeAsync() { Dispose(); return Task.CompletedTask; }
```

**3. Leaked test hosts (`c8c0e38`).** Three E2E call sites created a `TestWebApplicationFactory`
and never disposed it (`DashboardE2ETests.cs`, `ReportsE2ETests.cs`, `CostReportE2ETests.cs`);
each now wraps the factory and its client in `using` declarations. No assertions changed.

Removed with the workaround (`8b92298`):
- `tests/FMS.Domain.Tests/xunit.runner.json` (the `parallelizeTestCollections: false` override).
- The corresponding `<None Update="xunit.runner.json">` item in
  `tests/FMS.Domain.Tests/FMS.Domain.Tests.csproj`.

## Verification

- `dotnet build FarmManagementSystem.slnx` → **0 errors**, and no new warnings from the changed
  files.
- `dotnet test tests/FMS.Domain.Tests/FMS.Domain.Tests.csproj`, collections parallel, each run
  guarded with `--blame-hang --blame-hang-timeout 180s`:

  | Stage | Run | Result | Time |
  | --- | --- | --- | --- |
  | `Task.Run` workaround | 1 | 1105 passed, 17 skipped, 0 failed | 1m18s |
  | `Task.Run` workaround | 2 | 1105 passed, 17 skipped, 0 failed | 1m18s |
  | `Task.Run` workaround | 3 | 1105 passed, 17 skipped, 0 failed | 1m13s |
  | `IAsyncLifetime` fix | 1 | 1105 passed, 17 skipped, 0 failed | 1m07s |
  | `IAsyncLifetime` fix | 2 | 1105 passed, 17 skipped, 0 failed | 1m07s |
  | `IAsyncLifetime` fix | 3 | 1105 passed, 17 skipped, 0 failed | 1m12s |

  None aborted. The `IAsyncLifetime` change is verified by these runs: the Postgres-gated tests
  still skip (not fail), which is only possible if xUnit awaited `InitializeAsync` and set
  `UnavailableReason` before the tests ran.
- Postgres gating, isolated: `dotnet test tests/FMS.Domain.Tests/FMS.Domain.Tests.csproj
  --filter "FullyQualifiedName~Postgres"` → **3 passed, 14 skipped, 0 failed**. The 14 skipped
  are exactly the gated integration tests, each reporting the "no PostgreSQL server reachable"
  message; the 3 that pass are unrelated tests whose names merely contain "Postgres".

## Open questions / residual risk

- `TestWebApplicationFactory.CreateHost` also blocks (`builder.Start()` and
  `ApiSeedData.SeedAsync(db).GetAwaiter().GetResult()`), but it is reached during test bodies,
  not fixture construction, and its inner ASP.NET Core/EF work uses `ConfigureAwait(false)`,
  so it does not return to the xUnit context and did not reproduce the deadlock. It is left
  as-is.
- xUnit's `MaxParallelThreads` defaults to the processor count; raising it would also mask a
  recurrence rather than fix it, which is why the fix removes the blocking rather than the
  parallelism bound.
- The `HangfirePostgresIntegrationTests` fixture lives in the same `Jobs` area that
  `SchedulerTestProvider` documents as sharing process-global Hangfire state
  (`JobStorage.Current`, the Hangfire log provider). That shared state is a separate,
  documented concern and is not the cause of this hang.
