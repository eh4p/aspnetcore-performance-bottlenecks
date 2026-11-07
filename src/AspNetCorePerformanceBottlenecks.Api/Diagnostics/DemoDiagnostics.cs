using System.Diagnostics;
using System.Globalization;

namespace AspNetCorePerformanceBottlenecks.Api.Diagnostics;

public static class DemoDiagnostics
{
    public static void Complete(HttpContext context, ILogger logger, string scenario, string variant,
        long started, int? sqlCommands = null, double? lockWaitMs = null, long? operations = null)
    {
        var durationMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        context.Response.Headers["X-Demo-Duration-Ms"] = Format(durationMs);
        if (sqlCommands.HasValue)
            context.Response.Headers["X-Demo-Sql-Commands"] = sqlCommands.Value.ToString(CultureInfo.InvariantCulture);
        if (lockWaitMs.HasValue)
            context.Response.Headers["X-Demo-Lock-Wait-Ms"] = Format(lockWaitMs.Value);
        if (operations.HasValue)
            context.Response.Headers["X-Demo-Operations"] = operations.Value.ToString(CultureInfo.InvariantCulture);

        logger.LogInformation(
            "Demo {Scenario} {Variant}: duration {DurationMs:F3} ms, SQL {SqlCommands}, lock wait {LockWaitMs} ms, operations {Operations}",
            scenario, variant, durationMs, sqlCommands, lockWaitMs, operations);
    }

    private static string Format(double value) => value.ToString("F3", CultureInfo.InvariantCulture);
}
