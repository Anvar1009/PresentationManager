namespace PresentationManager.Application.Common;

/// <summary>Framework-agnostic screen rectangle, so Application stays free of System.Drawing/WinForms types.</summary>
public readonly record struct ScreenBounds(int X, int Y, int Width, int Height);
