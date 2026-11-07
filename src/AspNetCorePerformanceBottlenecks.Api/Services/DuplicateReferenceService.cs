using System.Collections.Frozen;
using System.Collections.Immutable;
using AspNetCorePerformanceBottlenecks.Api.Models;

namespace AspNetCorePerformanceBottlenecks.Api.Services;

public sealed class DuplicateReferenceService
{
    public const int ReferenceCount = 1_800;
    private readonly FrozenDictionary<int, ImmutableArray<string>> _batches = Enumerable.Range(1, DemoAccounts.Count)
        .ToFrozenDictionary(id => id, id => Enumerable.Range(0, ReferenceCount)
            .Select(i => $"DEMO-{id:D2}-REF-{i % 1_500:D4}").ToImmutableArray());

    public (int Count, long Operations) Violation(int accountId, CancellationToken cancellationToken)
    {
        var references = _batches[accountId];
        var duplicates = 0;
        long comparisons = 0;
        // VIOLATION: repeatedly scan the batch; count each duplicated value only once, O(n²).
        for (var i = 0; i < references.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var seenBefore = false;
            for (var j = 0; j < i; j++)
            {
                comparisons++;
                if (StringComparer.Ordinal.Equals(references[i], references[j]))
                {
                    seenBefore = true;
                    break;
                }
            }
            if (seenBefore)
                continue;
            for (var j = i + 1; j < references.Length; j++)
            {
                comparisons++;
                if (StringComparer.Ordinal.Equals(references[i], references[j]))
                {
                    duplicates++;
                    break;
                }
            }
        }
        return (duplicates, comparisons);
    }

    public (int Count, long Operations) Solution(int accountId, CancellationToken cancellationToken)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var duplicated = new HashSet<string>(StringComparer.Ordinal);
        long hashOperations = 0;
        // SOLUTION: track seen and duplicated references in expected O(n) time, using O(n) memory.
        foreach (var reference in _batches[accountId])
        {
            cancellationToken.ThrowIfCancellationRequested();
            hashOperations++;
            if (!seen.Add(reference))
            {
                hashOperations++;
                duplicated.Add(reference);
            }
        }
        return (duplicated.Count, hashOperations);
    }
}
