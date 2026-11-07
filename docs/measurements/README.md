# Local measurement record

These files contain actual local measurements from **2026-11-02**. No expected or simulated benchmark values are stored as measurements.

## Environment and method

| Item | Recorded condition |
| --- | --- |
| OS | Ubuntu 24.04.4 LTS, Linux x86-64 |
| CPU | Intel Core i7-6700HQ @ 2.60 GHz, 4 physical cores / 8 logical CPUs |
| Memory | Approximately 16 GiB installed (15 GiB reported usable) |
| SDK | 10.0.403 |
| ASP.NET Core / .NET runtime | 10.0.14 |
| EF Core / EF relational | 10.0.14 |
| Npgsql EF provider / Npgsql | 10.0.3 |
| PostgreSQL | 18.3, locally installed server binaries; loopback TCP port 55439 |
| Build | Release, warnings treated as errors; no ThreadPool tuning |
| Logging during benchmark | Default repository configuration (operation and SQL logs suppressed) |
| Client | Same machine; one shared HttpClient per run, proxies/redirects disabled, 15 s request timeout |
| Dataset | 20 accounts, 100 transactions/account; 1,800 CPU references/account, 300 duplicated values |
| Dependency work | Verification 125 ms; statement generation 80 ms, identical between paired variants |
| Low load | Concurrency 1, 80 measured requests/run, 30 warmup requests |
| Higher load | Concurrency 30, 240 measured requests/run, 30 warmup requests |
| Repetitions | Three for every scenario/variant/concurrency combination; 48 main runs |
| Order | Violation then solution in repetitions 1 and 3; solution then violation in repetition 2 |
| Account distribution | Request index `i` maps to `1 + i % 20`; N+1 always returns the ordered 20-account list |
| Warmup | Bounded concurrent warmup for each selected variant; excluded from measured interval |
| Startup | Database/schema/seed and immutable input creation completed before readiness; startup excluded |

The PostgreSQL verification instance used an isolated disposable cluster under `/tmp`, created with the installed PostgreSQL binaries rather than a container. Its dedicated non-superuser role owned only the demo database. No existing application databases or real credentials were used. Normal developer setup in the main README uses a locally chosen password and the usual local PostgreSQL authentication configuration.

## Files and interpretation

- [local-runs.csv](local-runs.csv): one row per actual main run, in execution order. Every row includes its UTC timestamp, workload, successful percentiles, errors and available diagnostic-header averages.
- [same-account.csv](same-account.csv): fixed-account lock control, one run per variant at concurrency 10, 40 measured requests and 10 warmup requests. This control is separate from the three-repeat mixed-account results.
- [latency.svg](latency.svg): median of the three per-run p95 values at concurrency 30. Each scenario panel has its own explicitly labeled linear scale; bars do not share a common scale.
- [runtime-counters.csv](runtime-counters.csv): original counter samples merged with scenario/variant labels from separate instrumented runs; local timestamps use Africa/Cairo.

The main README reports **medians of per-run statistics**, not percentiles calculated from pooled individual request samples. The console stores summary rows, not all latencies. The raw CSV preserves run-to-run variation. Successful RPS divides successful completions by the full measured interval. Non-2xx, request timeouts, exceptions, cancellations and unstarted requests are separate columns. A comparison must consider those counts alongside successful-request latency.

The console was launched through its compiled Release DLL with the selected SDK's `dotnet` host. This invokes the same program as the README's `dotnet run --no-build` commands; process startup is outside the console's measured interval in either case.

```bash
dotnet tools/AspNetCorePerformanceBottlenecks.LoadGenerator/bin/Release/net10.0/AspNetCorePerformanceBottlenecks.LoadGenerator.dll --scenario blocking-io --variant violation --concurrency 30 --requests 240 --warmup 30 --csv local-results/results.csv
```

See the [main README protocol](../../README.md#run-bounded-equivalent-loads) to repeat all 48 comparisons. Machine load, JIT tiering, scheduling, connection pools and the process's ThreadPool history can change results. These short, closed-loop, shared-machine runs are a demonstration, not a capacity certification.

## Executed verification

- `dotnet --info` confirmed SDK 10.0.403 and runtime 10.0.14.
- `dotnet restore --packages /tmp/aspnetcore-nuget-packages` restored both projects successfully.
- `dotnet build -c Release -warnaserror --no-restore --disable-build-servers` succeeded with zero warnings and zero errors.
- `dotnet run --project src/AspNetCorePerformanceBottlenecks.Api --configuration Release --no-build --no-launch-profile -- --urls http://localhost:5080` started the API after schema initialization/seeding.
- `curl --fail --silent --show-error http://localhost:5080/health` returned HTTP 200 and `{"status":"ready"}`.
- All eight routes returned 200. Each of the four pairs returned identical parsed business JSON.
- For both variants of every account-specific family, `abc`, `0`, `-1`, and an overflowing integer returned 400; account `21` returned 404.
- The tool's `--verify-equivalence` passed for all four families, covering all 20 account IDs (one ordered list for N+1).
- Concurrent N+1 checks (20 requests/variant with 10 clients) always reported 21 versus 1 SQL commands, with no cross-request count leakage.
- Both CPU routes returned 300 duplicated reference values. The nested scans performed 2,698,800 ordinal equality comparisons; the hash path performed 2,100 Add attempts. The units differ.
- A second API startup seeded idempotently and became ready without duplicate-key errors.
- Invalid CLI concurrency/request bounds, variant values and a non-loopback base URL returned exit code 2. Bounded manual checks against a temporary local HTTP stub confirmed separate non-2xx and timeout counts; an unavailable local port produced separate connection-exception counts. These intentionally failed requests are outside all benchmark CSVs.

SQL logging was enabled only for initial smoke/translation checks, then disabled for the benchmark. The observed optimized command was:

```sql
SELECT a."Id", a."Name", a."Currency", COALESCE((
    SELECT COALESCE(sum(t."AmountMinor"), 0.0)::bigint
    FROM "Transactions" AS t
    WHERE a."Id" = t."AccountId"), 0)
FROM "Accounts" AS a
ORDER BY a."Id"
LIMIT @p
```

`@p` was the bounded account limit. This confirms PostgreSQL-side aggregation in one command; the correlated subquery is not evidence of a single scan or an optimal query plan at every scale.

## Runtime and stack evidence

Installed `dotnet-counters`, `dotnet-stack`, and `dotnet-trace` version **10.0.750501** into a temporary tool directory. The documented `ps`, `monitor`, `report`, and `collect` syntax was checked against the selected tools; counter collection and stack/trace capture were executed against the API. Profiler artifacts and complete stack dumps remain outside the repository.

After the main benchmark, each diagnostic variant ran repeated, bounded batches at concurrency 30, 240 measured requests and 30 warmup requests per batch, for approximately 17 seconds. Batch count varied with execution speed; process startup between batches and instrumentation affect these samples. CPU trace capture ran only during the CPU violation, making it an instrumented investigation rather than a fair timing comparison. These diagnostic samples must not be substituted for the main load-generator results.

The 18-second collector used the legacy EventCounters view explicitly so the reported CPU percentage and ThreadPool values have a familiar single-series format. Newer `System.Runtime` meter views use different instrument names.

```bash
dotnet-counters collect --process-id <API_PID> --counters 'EventCounters\System.Runtime[cpu-usage,threadpool-thread-count,threadpool-queue-length]' --format csv --output local-results/runtime.csv --duration 00:00:00:18
dotnet-stack report --process-id <API_PID>
dotnet-trace collect --process-id <API_PID> --duration 00:00:00:05 --output local-results/cpu.nettrace
dotnet-trace report local-results/cpu.nettrace topN --number 10
```

| Diagnostic variant | CPU %, median (max) | Thread count, min–max | Queue length, median (max) |
| --- | ---: | ---: | ---: |
| Blocking violation | 2.26 (8.31) | 4–39 | 0 (1) |
| Blocking solution | 1.07 (4.66) | 38–39 | 0 (0) |
| CPU violation | 55.29 (79.86) | 14–15 | 8 (20) |
| CPU solution | 5.36 (8.08) | 15–21 | 0 (29) |

Each row summarizes 18 samples per counter. The CPU column is the tool's reported API-process CPU percentage. Short spikes, background work and the batched load pattern limit interpretation. The isolated queue spike in the CPU solution is a reason to correlate signals rather than diagnose from one maximum. The blocking solution ran after the violation in the same process and inherited its larger ThreadPool. It therefore did not show fewer total threads, despite not blocking them. The main warmed benchmark likewise did not reproduce sustained ThreadPool starvation.

The violation stack snapshot contained **30** occurrences of the blocking handler; the solution snapshot contained **0**. One actual violation stack was:

```text
System.Threading.Tasks.Task.InternalWaitCore(...)
System.Threading.Tasks.Task.InternalWait(...)
System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(...)
AspNetCorePerformanceBottlenecks.Api.Endpoints.BlockingIoEndpoints.Violation(...)
```

This locates the synchronous wait. It does not prove that every slow request in another application has the same cause.

The five-second CPU-violation trace completed successfully. The exclusive top-10 report included `DuplicateReferenceService.Violation` and `SpanHelpers.SequenceEqual`, supporting the repeated-comparison hot path. The tool defaulted to `dotnet-common` plus `dotnet-sampled-thread-time`; its report also includes waiting threads. Report percentages are sampled thread-time shares, not CPU-utilization percentages, and should not be compared numerically with the CPU counter.

## Environment issues resolved before measurement

The system `PATH` did not include the installed older .NET SDK. The current stable SDK was made available under `/tmp/aspnetcore-dotnet`; the CLI first-run directory and NuGet package cache were also redirected to `/tmp`. Initial sandboxed commands could not write the default first-run sentinel, access network sockets, or complete MSBuild process communication. Restore/build/run were rerun with the necessary execution permissions. The first temporary PostgreSQL port attempted was occupied; port 55439 was used instead. None of these failed setup attempts is reported as a benchmark result.
