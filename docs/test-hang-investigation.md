# Test-hang investigation: the parallel backend run deadlock

## Summary

The combined backend run (`dotnet test tests/FMS.Domain.Tests`) used to stall forever when
xUnit ran test collections in parallel. The fix (commit `a093a1e`) was to serialize
collections via `xunit.runner.json`. This document records the follow-up investigation that
found the actual cause, the fix, and how to reproduce the original hang.

**Root cause:** two PostgreSQL integration fixtures probe for a reachable server with
`GetAwaiter().GetResult()` in their **constructors**. xUnit constructs class fixtures on its
bounded `MaxConcurrencySyncContext`, and the probe's `await`s captured that same context.
Blocking a worker while the awaited continuation needs that same worker starves the context
once enough fixtures block at once, so nothing finishes. Running the probe on the thread pool
via `Task.Run` clears the captured context and removes the deadlock.

The serialization is therefore no longer needed and has been removed.

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

## The fix

Run the probes on the thread pool so their continuations never need the xUnit sync context:

- `tests/FMS.Domain.Tests/E2E/PostgresIntegrationFactory.cs` — constructor probe.
- `tests/FMS.Domain.Tests/Jobs/HangfirePostgresIntegrationTests.cs` — `PostgresDatabaseFixture`
  constructor probe.

```csharp
UnavailableReason = Task.Run(ProbeAndPrepareAsync).GetAwaiter().GetResult();
```

`Task.Run` runs the probe with `SynchronizationContext.Current == null`, so its `await`s post
continuations to the thread pool; the blocking `GetResult()` then always unblocks.

Removed as part of the same change:
- `tests/FMS.Domain.Tests/xunit.runner.json` (the `parallelizeTestCollections: false` override).
- The corresponding `<None Update="xunit.runner.json">` item in
  `tests/FMS.Domain.Tests/FMS.Domain.Tests.csproj`.

## Verification

- `dotnet build FarmManagementSystem.slnx` → **0 errors**.
- `dotnet test tests/FMS.Domain.Tests/FMS.Domain.Tests.csproj` with collections back in
  parallel, run three times:

  | Run | Result | Time |
  | --- | --- | --- |
  | 1 | 1105 passed, 17 skipped, 0 failed | 1m18s |
  | 2 | 1105 passed, 17 skipped, 0 failed | 1m18s |
  | 3 | 1105 passed, 17 skipped, 0 failed | 1m13s |

  Each run was also guarded with `--blame-hang --blame-hang-timeout 180s`; none aborted.

## Open questions / residual risk

- `TestWebApplicationFactory.CreateHost` also blocks (`builder.Start()` and
  `ApiSeedData.SeedAsync(db).GetAwaiter().GetResult()`), but it is reached during test bodies,
  not fixture construction, and its inner ASP.NET Core/EF work uses `ConfigureAwait(false)`,
  so it does not return to the xUnit context and did not reproduce the deadlock. It is left
  as-is.
- xUnit's `MaxParallelThreads` defaults to the processor count; raising it would also mask a
  recurrence rather than fix it, which is why the fix targets the context capture instead.
- The `HangfirePostgresIntegrationTests` fixture lives in the same `Jobs` area that
  `SchedulerTestProvider` documents as sharing process-global Hangfire state
  (`JobStorage.Current`, the Hangfire log provider). That shared state is a separate,
  documented concern and is not the cause of this hang.
