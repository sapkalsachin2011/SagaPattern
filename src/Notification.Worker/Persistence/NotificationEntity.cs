namespace Notification.Worker.Persistence;

public sealed class NotificationEntity
{
    public Guid OrderId { get; set; }
    public DateTimeOffset SentAt { get; set; }
    public string Status { get; set; } = string.Empty;
}