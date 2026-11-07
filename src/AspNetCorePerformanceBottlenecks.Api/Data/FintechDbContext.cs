using AspNetCorePerformanceBottlenecks.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace AspNetCorePerformanceBottlenecks.Api.Data;

public sealed class FintechDbContext(DbContextOptions<FintechDbContext> options) : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Transaction> Transactions => Set<Transaction>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Account>(entity =>
        {
            entity.Property(a => a.Id).ValueGeneratedNever();
            entity.Property(a => a.Name).HasMaxLength(80);
            entity.Property(a => a.Currency).HasMaxLength(3);
            entity.HasMany(a => a.Transactions).WithOne().HasForeignKey(t => t.AccountId);
        });
        modelBuilder.Entity<Transaction>(entity =>
        {
            entity.Property(t => t.Id).ValueGeneratedNever();
            entity.Property(t => t.Reference).HasMaxLength(80);
            entity.HasIndex(t => t.AccountId);
        });
    }
}
