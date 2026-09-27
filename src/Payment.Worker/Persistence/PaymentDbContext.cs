using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Payment.Worker.Persistence;

public sealed class PaymentDbContext(DbContextOptions<PaymentDbContext> options) : DbContext(options)
{
    public DbSet<PaymentEntity> Payments => Set<PaymentEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PaymentEntity>(entity =>
        {
            entity.HasKey(payment => payment.OrderId);
            entity.Property(payment => payment.Status).HasMaxLength(32).IsRequired();
        });

        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}