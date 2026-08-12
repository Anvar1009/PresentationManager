using PresentationManager.Domain.Entities;
using PresentationManager.Domain.Enums;

namespace PresentationManager.API.Dtos;

public sealed record HistoryEntryDto(int Id, int PresentationId, HistoryEventType EventType, string Message, DateTime Timestamp)
{
    public static HistoryEntryDto FromEntity(HistoryEntry h) => new(h.Id, h.PresentationId, h.EventType, h.Message, h.Timestamp);

    public HistoryEntry ToEntity() => new()
    {
        Id = Id,
        PresentationId = PresentationId,
        EventType = EventType,
        Message = Message,
        Timestamp = Timestamp
    };
}

public sealed record CreateHistoryEntryRequest(int PresentationId, HistoryEventType EventType, string Message, DateTime Timestamp);
