using System.Diagnostics;
using AspNetCorePerformanceBottlenecks.Api.DTOs;
using AspNetCorePerformanceBottlenecks.Api.Models;

namespace AspNetCorePerformanceBottlenecks.Api.Services;

public sealed class StatementGenerationService : IDisposable
{
    private readonly SemaphoreSlim _globalGate = new(1, 1);
    private readonly SemaphoreSlim[] _accountGates = Enumerable.Range(1, DemoAccounts.Count)
        .Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    public Task<(StatementSummary Statement, double WaitMs)> ViolationAsync(int accountId, CancellationToken cancellationToken)
    {
        // VIOLATION: unrelated accounts wait behind the same global gate.
        return GenerateAsync(accountId, _globalGate, cancellationToken);
    }

    public Task<(StatementSummary Statement, double WaitMs)> SolutionAsync(int accountId, CancellationToken cancellationToken)
    {
        // SOLUTION: a fixed, bounded gate per known account preserves required serialization.
        return GenerateAsync(accountId, _accountGates[accountId - 1], cancellationToken);
    }

    private static async Task<(StatementSummary Statement, double WaitMs)> GenerateAsync(
        int accountId, SemaphoreSlim gate, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        await gate.WaitAsync(cancellationToken);
        var waitMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        try
        {
            // Both variants simulate the same account-specific external statement work.
            await Task.Delay(80, cancellationToken);
            return (new StatementSummary(accountId, $"DEMO-STMT-{accountId:D2}-202601", 40, accountId * 10_000L + 820), waitMs);
        }
        finally
        {
            gate.Release();
        }
    }

    public void Dispose()
    {
        _globalGate.Dispose();
        foreach (var gate in _accountGates)
            gate.Dispose();
    }
}
