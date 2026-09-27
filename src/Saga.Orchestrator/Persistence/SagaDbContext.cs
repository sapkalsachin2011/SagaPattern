using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Saga.Orchestrator.Persistence;

public sealed class SagaDbContext(DbContextOptions<SagaDbContext> options) : DbContext(options)
{
    public DbSet<OrderSagaState> OrderSagas => Set<OrderSagaState>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OrderSagaState>(entity =>
        {
            entity.HasKey(saga => saga.CorrelationId);
            entity.Property(saga => saga.CurrentState).HasMaxLength(64).IsRequired();
            entity.Property(saga => saga.CustomerEmail).HasMaxLength(320).IsRequired();
            entity.Property(saga => saga.FailureReason).HasMaxLength(500);
        });

        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}