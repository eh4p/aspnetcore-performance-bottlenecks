using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;

if (args.Contains("--help"))
{
    Console.WriteLine(Options.Usage);
    return 0;
}

Options options;
try
{
    options = Options.Parse(args);
}
catch (ArgumentException error)
{
    Console.Error.WriteLine(error.Message);
    Console.Error.WriteLine(Options.Usage);
    return 2;
}

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};
using var handler = new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false, MaxConnectionsPerServer = 100 };
using var client = new HttpClient(handler) { BaseAddress = options.BaseUrl, Timeout = TimeSpan.FromSeconds(15) };

try
{
    if (options.VerifyEquivalence)
    {
        foreach (var accountId in options.AccountId.HasValue ? new[] { options.AccountId.Value } : Enumerable.Range(1, 20))
        {
            var violation = JsonNode.Parse(await client.GetStringAsync(options.Path(accountId - 1, "violation"), cancellation.Token));
            var solution = JsonNode.Parse(await client.GetStringAsync(options.Path(accountId - 1, "solution"), cancellation.Token));
            if (!JsonNode.DeepEquals(violation, solution))
            {
                Console.Error.WriteLine($"Business JSON differs for account {accountId}.");
                return 1;
            }
            if (options.Scenario == "n-plus-one")
                break;
        }
        Console.WriteLine($"Business JSON equivalent: {options.Scenario}, {options.Distribution}.");
        return 0;
    }

    Console.WriteLine($"{options.Scenario}/{options.Variant}; {options.Distribution}; concurrency={options.Concurrency}; requests={options.Requests}; warmup={options.Warmup}; timeout=15s");
    // Warm the same concurrency/connection paths outside the measured interval.
    await Parallel.ForEachAsync(Enumerable.Range(0, options.Warmup),
        new ParallelOptions { MaxDegreeOfParallelism = options.Concurrency, CancellationToken = cancellation.Token },
        async (i, token) =>
        {
            using var response = await client.GetAsync(options.Path(i, options.Variant), token);
            response.EnsureSuccessStatusCode();
        });
}
catch (Exception error) when (error is HttpRequestException or OperationCanceledException)
{
    Console.Error.WriteLine($"Warmup/equivalence failed ({error.GetType().Name}); no benchmark recorded.");
    return 1;
}

var samples = new Sample[options.Requests];
var timer = Stopwatch.StartNew();
try
{
    await Parallel.ForEachAsync(Enumerable.Range(0, options.Requests),
        new ParallelOptions { MaxDegreeOfParallelism = options.Concurrency, CancellationToken = cancellation.Token },
        async (index, token) =>
        {
            var started = Stopwatch.GetTimestamp();
            try
            {
                // Default completion buffers the whole body: latency includes body transfer.
                using var response = await client.GetAsync(options.Path(index, options.Variant), token);
                samples[index] = new Sample(response.IsSuccessStatusCode ? "success" : "non2xx",
                    Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                    Header(response, "X-Demo-Sql-Commands"), Header(response, "X-Demo-Lock-Wait-Ms"), Header(response, "X-Demo-Operations"));
            }
            catch (OperationCanceledException)
            {
                samples[index] = new Sample(cancellation.IsCancellationRequested ? "cancelled" : "timeout",
                    Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            }
            catch (HttpRequestException)
            {
                samples[index] = new Sample("exception", Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            }
        });
}
catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
{
    // Print all completed attempts and explicitly report work never started.
}
timer.Stop();

var completed = samples.Where(s => s.Outcome is not null).ToArray();
var successful = completed.Where(s => s.Outcome == "success").ToArray();
var latencies = successful.Select(s => s.LatencyMs).Order().ToArray();
var non2xx = completed.Count(s => s.Outcome == "non2xx");
var timeouts = completed.Count(s => s.Outcome == "timeout");
var exceptions = completed.Count(s => s.Outcome == "exception");
var cancelled = completed.Count(s => s.Outcome == "cancelled");
var failures = completed.Length - successful.Length;
var rps = successful.Length / timer.Elapsed.TotalSeconds;
var sql = successful.Where(s => s.SqlCommands.HasValue).Select(s => s.SqlCommands!.Value).ToArray();
var waits = successful.Where(s => s.LockWaitMs.HasValue).Select(s => s.LockWaitMs!.Value).Order().ToArray();
var operations = successful.Where(s => s.Operations.HasValue).Select(s => s.Operations!.Value).ToArray();

Console.WriteLine($"Completed={completed.Length}/{options.Requests}; success={successful.Length}; failures={failures}; non-2xx={non2xx}; timeouts={timeouts}; exceptions={exceptions}; cancelled={cancelled}; unstarted={options.Requests - completed.Length}");
Console.WriteLine($"Elapsed={timer.Elapsed.TotalSeconds:F3}s; successful RPS={rps:F2}");
Console.WriteLine($"Successful latency (ms, nearest rank): p50={Display(Percentile(latencies, 0.50))}; p95={Display(Percentile(latencies, 0.95))}; p99={Display(Percentile(latencies, 0.99))}");
if (sql.Length > 0)
    Console.WriteLine($"SQL commands/request: min={sql.Min():F0}; mean={sql.Average():F2}; max={sql.Max():F0}");
if (waits.Length > 0)
    Console.WriteLine($"Lock wait: mean={waits.Average():F3}ms; p95={Percentile(waits, 0.95):F3}ms");
if (operations.Length > 0)
    Console.WriteLine($"Algorithm-specific operations/request: mean={operations.Average():F0}");

if (options.Csv is not null)
{
    var header = "utc,scenario,variant,distribution,concurrency,requests,warmup,completed,success,failures,non2xx,timeouts,exceptions,cancelled,unstarted,elapsed_s,successful_rps,p50_ms,p95_ms,p99_ms,sql_mean,lock_wait_mean_ms,lock_wait_p95_ms,operations_mean";
    var fields = new object?[] { DateTimeOffset.UtcNow.ToString("O"), options.Scenario, options.Variant, options.Distribution,
        options.Concurrency, options.Requests, options.Warmup, completed.Length, successful.Length, failures, non2xx, timeouts,
        exceptions, cancelled, options.Requests - completed.Length, timer.Elapsed.TotalSeconds, rps,
        Percentile(latencies, 0.50), Percentile(latencies, 0.95), Percentile(latencies, 0.99),
        sql.Length > 0 ? sql.Average() : null, waits.Length > 0 ? waits.Average() : null,
        Percentile(waits, 0.95), operations.Length > 0 ? operations.Average() : null };
    try
    {
        var newFile = !File.Exists(options.Csv) || new FileInfo(options.Csv).Length == 0;
        await File.AppendAllTextAsync(options.Csv, (newFile ? header + Environment.NewLine : "") +
            string.Join(",", fields.Select(f => Convert.ToString(f, CultureInfo.InvariantCulture))) + Environment.NewLine);
    }
    catch (Exception error) when (error is IOException or UnauthorizedAccessException)
    {
        Console.Error.WriteLine($"Could not write CSV ({error.GetType().Name}). Console results remain valid.");
        return 1;
    }
}
return failures == 0 && completed.Length == options.Requests ? 0 : 1;

static double? Header(HttpResponseMessage response, string name) =>
    response.Headers.TryGetValues(name, out var values) && double.TryParse(values.FirstOrDefault(),
        NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;

static double? Percentile(double[] sorted, double fraction) => sorted.Length == 0
    ? null : sorted[Math.Max(0, (int)Math.Ceiling(sorted.Length * fraction) - 1)];

static string Display(double? value) => value?.ToString("F3", CultureInfo.InvariantCulture) ?? "n/a";

readonly record struct Sample(string? Outcome, double LatencyMs, double? SqlCommands = null, double? LockWaitMs = null, double? Operations = null);

sealed record Options(Uri BaseUrl, string Scenario, string Variant, int Concurrency, int Requests, int Warmup,
    int? AccountId, string? Csv, bool VerifyEquivalence)
{
    public const string Usage = """
        Local-only usage:
          --base-url http://localhost:5080
          --scenario blocking-io|n-plus-one|lock-contention|cpu-hotspot (required)
          --variant violation|solution (default: solution)
          --concurrency 1..100 (default: 10) --requests 1..10000 (default: 200)
          --warmup 0..200 (default: 30)
          --account-id 1..20 (optional: fixed account; default: round robin 1..20)
          --csv local-results/run.csv (optional: append summary; parent must exist)
          --verify-equivalence (compare both business JSON responses; no load run)
        Use only against your own local demo API. Ctrl+C cancels pending requests.
        """;

    public string Distribution => Scenario == "n-plus-one" ? "all-20" : AccountId.HasValue ? $"account-{AccountId}" : "round-robin-1-20";

    public string Path(int index, string variant) => Scenario == "n-plus-one"
        ? $"/api/performance/{Scenario}/{variant}"
        : $"/api/performance/{Scenario}/{AccountId ?? index % 20 + 1}/{variant}";

    public static Options Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var verify = false;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--verify-equivalence" && !verify)
            {
                verify = true;
                continue;
            }
            if (args[i] is not ("--base-url" or "--scenario" or "--variant" or "--concurrency" or "--requests" or "--warmup" or "--account-id" or "--csv") ||
                i + 1 == args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal) || !values.TryAdd(args[i], args[++i]))
                throw new ArgumentException("Unknown, duplicate, or incomplete option. Use --help.");
        }
        var baseUrl = values.GetValueOrDefault("--base-url", "http://localhost:5080");
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") ||
            !(uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || IPAddress.TryParse(uri.Host.Trim('[', ']'), out var address) && IPAddress.IsLoopback(address)) ||
            uri.AbsolutePath != "/" || uri.Query.Length > 0 || uri.Fragment.Length > 0 || uri.UserInfo.Length > 0)
            throw new ArgumentException("--base-url must be an HTTP(S) loopback origin without credentials, path, query, or fragment.");
        var scenario = values.GetValueOrDefault("--scenario", "");
        if (scenario is not ("blocking-io" or "n-plus-one" or "lock-contention" or "cpu-hotspot"))
            throw new ArgumentException("Choose a valid --scenario.");
        var variant = values.GetValueOrDefault("--variant", "solution");
        if (variant is not ("violation" or "solution"))
            throw new ArgumentException("--variant must be violation or solution.");
        return new Options(uri, scenario, variant, Number("--concurrency", 10, 1, 100), Number("--requests", 200, 1, 10_000),
            Number("--warmup", 30, 0, 200), values.ContainsKey("--account-id") ? Number("--account-id", 1, 1, 20) : null,
            values.GetValueOrDefault("--csv"), verify);

        int Number(string name, int fallback, int min, int max)
        {
            if (!values.TryGetValue(name, out var raw))
                return fallback;
            if (!int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value < min || value > max)
                throw new ArgumentException($"{name} must be between {min} and {max}.");
            return value;
        }
    }
}
