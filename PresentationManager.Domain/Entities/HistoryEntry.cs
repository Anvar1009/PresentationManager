using PresentationManager.Domain.Enums;

namespace PresentationManager.Domain.Entities;

public class HistoryEntry
{
    public int Id { get; set; }

    public int PresentationId { get; set; }

    public HistoryEventType EventType { get; set; }

    public required string Message { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
