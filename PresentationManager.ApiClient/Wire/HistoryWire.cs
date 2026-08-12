using PresentationManager.Domain.Entities;
using PresentationManager.Domain.Enums;

namespace PresentationManager.ApiClient.Wire;

/// <summary>Mirrors PresentationManager.API.Dtos.HistoryEntryDto/CreateHistoryEntryRequest.</summary>
internal sealed record HistoryEntryWire(int Id, int PresentationId, HistoryEventType EventType, string Message, DateTime Timestamp)
{
    public HistoryEntry ToEntity() => new()
    {
        Id = Id,
        PresentationId = PresentationId,
        EventType = EventType,
        Message = Message,
        Timestamp = Timestamp
    };
}

internal sealed record CreateHistoryEntryWireRequest(int PresentationId, HistoryEventType EventType, string Message, DateTime Timestamp)
{
    public static CreateHistoryEntryWireRequest FromEntity(HistoryEntry h) => new(h.PresentationId, h.EventType, h.Message, h.Timestamp);
}
