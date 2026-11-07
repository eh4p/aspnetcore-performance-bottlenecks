using System.Diagnostics;
using AspNetCorePerformanceBottlenecks.Api.Data;
using AspNetCorePerformanceBottlenecks.Api.Diagnostics;
using AspNetCorePerformanceBottlenecks.Api.DTOs;
using AspNetCorePerformanceBottlenecks.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace AspNetCorePerformanceBottlenecks.Api.Endpoints;

public static class NPlusOneEndpoints
{
    public static void MapNPlusOne(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/performance/n-plus-one");
        group.MapGet("/violation", ViolationAsync);
        group.MapGet("/solution", SolutionAsync);
    }

    private static async Task<IResult> ViolationAsync(FintechDbContext db, DbCommandCountingInterceptor counter,
        HttpContext context, ILoggerFactory loggerFactory, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        counter.Reset();
        var accounts = await db.Accounts.AsNoTracking().OrderBy(a => a.Id).Take(DemoAccounts.Count).ToListAsync(cancellationToken);
        var summaries = new List<AccountSummary>(accounts.Count);
        // VIOLATION: one list query followed by one SQL SUM per account (1 + N round trips).
        foreach (var account in accounts)
        {
            var total = await db.Transactions.AsNoTracking().Where(t => t.AccountId == account.Id)
                .SumAsync(t => (long?)t.AmountMinor, cancellationToken) ?? 0L;
            summaries.Add(new AccountSummary(account.Id, account.Name, account.Currency, total));
        }
        DemoDiagnostics.Complete(context, loggerFactory.CreateLogger("NPlusOne"), "n-plus-one", "violation", started, counter.Count);
        return Results.Ok(summaries);
    }

    private static async Task<IResult> SolutionAsync(FintechDbContext db, DbCommandCountingInterceptor counter,
        HttpContext context, ILoggerFactory loggerFactory, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        counter.Reset();
        // SOLUTION: EF translates the projection and correlated SUM to one database-side SQL command.
        var summaries = await db.Accounts.AsNoTracking().OrderBy(a => a.Id).Take(DemoAccounts.Count)
            .Select(a => new AccountSummary(a.Id, a.Name, a.Currency,
                a.Transactions.Sum(t => (long?)t.AmountMinor) ?? 0L))
            .ToListAsync(cancellationToken);
        DemoDiagnostics.Complete(context, loggerFactory.CreateLogger("NPlusOne"), "n-plus-one", "solution", started, counter.Count);
        return Results.Ok(summaries);
    }
}
