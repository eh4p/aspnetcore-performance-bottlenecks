using AspNetCorePerformanceBottlenecks.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace AspNetCorePerformanceBottlenecks.Api.Data;

public static class DemoDataSeeder
{
    public const int TransactionsPerAccount = 100;

    public static async Task SeedAsync(FintechDbContext db, ILogger logger, CancellationToken cancellationToken)
    {
        // Refuse a missing database so EnsureCreated cannot try to provision a database with elevated rights.
        if (!await db.Database.CanConnectAsync(cancellationToken))
            throw new InvalidOperationException("Pre-create the local demo database and check ConnectionStrings:FintechDb. See README.");

        await db.Database.EnsureCreatedAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var existingAccounts = (await db.Accounts.Select(a => a.Id).ToListAsync(cancellationToken)).ToHashSet();
        var existingTransactions = (await db.Transactions.Select(t => t.Id).ToListAsync(cancellationToken)).ToHashSet();
        for (var id = 1; id <= DemoAccounts.Count; id++)
        {
            if (!existingAccounts.Contains(id))
                db.Accounts.Add(new Account { Id = id, Name = DemoAccounts.Name(id) });
            for (var n = 1; n <= TransactionsPerAccount; n++)
            {
                var transactionId = (id - 1) * TransactionsPerAccount + n;
                if (!existingTransactions.Contains(transactionId))
                    db.Transactions.Add(new Transaction
                    {
                        Id = transactionId,
                        AccountId = id,
                        AmountMinor = id * 100L + n * 7L,
                        Reference = $"DEMO-{id:D2}-TX-{n:D3}"
                    });
            }
        }
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Synthetic dataset ready: {Accounts} accounts, {Transactions} transactions; amounts in USD minor units",
            DemoAccounts.Count, DemoAccounts.Count * TransactionsPerAccount);
    }
}
