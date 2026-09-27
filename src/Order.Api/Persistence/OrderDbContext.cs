using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Order.Api.Persistence;

public sealed class OrderDbContext(DbContextOptions<OrderDbContext> options) : DbContext(options)
{
    public DbSet<OrderEntity> Orders => Set<OrderEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OrderEntity>(entity =>
        {
            entity.HasKey(order => order.Id);
            entity.Property(order => order.CustomerEmail).HasMaxLength(320).IsRequired();
            entity.Property(order => order.Status).HasMaxLength(40).IsRequired();
            entity.Property(order => order.FailureReason).HasMaxLength(500);
        });

        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}