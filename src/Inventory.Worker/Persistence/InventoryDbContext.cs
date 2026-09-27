using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Worker.Persistence;

public sealed class InventoryDbContext(DbContextOptions<InventoryDbContext> options) : DbContext(options)
{
    public DbSet<InventoryItem> Items => Set<InventoryItem>();
    public DbSet<InventoryReservation> Reservations => Set<InventoryReservation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<InventoryItem>(entity =>
        {
            entity.HasKey(item => item.Sku);
            entity.Property(item => item.Sku).HasMaxLength(80);
            entity.Property(item => item.AvailableQuantity).IsConcurrencyToken();
        });

        modelBuilder.Entity<InventoryReservation>(entity =>
        {
            entity.HasKey(reservation => reservation.OrderId);
            entity.Property(reservation => reservation.Sku).HasMaxLength(80).IsRequired();
            entity.Property(reservation => reservation.Status).HasMaxLength(32).IsRequired();
        });

        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}