using PresentationManager.Application.Common;
using PresentationManager.Domain.Enums;

namespace PresentationManager.Application.Interfaces;

/// <summary>
/// Displays real slide content (PPTX via PowerPoint COM, or PDF via an embedded viewer) full screen
/// on a target monitor. One implementation per <see cref="PresentationFileType"/>.
/// </summary>
public interface ISlideDisplayService
{
    PresentationFileType SupportedType { get; }

    bool IsOpen { get; }

    Task OpenAsync(string absoluteFilePath, ScreenBounds targetBounds, CancellationToken ct = default);

    /// <summary>Re-reveals a previously opened file (e.g. resuming the presentation after a discussion
    /// break) without reopening it, so slide position/scroll is preserved.</summary>
    Task ShowAsync(CancellationToken ct = default);

    /// <summary>Hides the slide content (e.g. switching to a discussion break) while keeping it open so
    /// <see cref="ShowAsync"/> can bring it straight back.</summary>
    Task HideAsync(CancellationToken ct = default);

    Task CloseAsync(CancellationToken ct = default);
}
