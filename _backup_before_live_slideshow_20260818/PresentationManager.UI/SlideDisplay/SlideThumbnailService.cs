using PresentationManager.Domain.Enums;
using SkiaSharp;

namespace PresentationManager.UI.SlideDisplay;

/// <summary>
/// Renders just the first page of a presentation's file as a small preview image — used by AdminForm to
/// show which file BOSHLASH is actually about to open, without spinning up the full WebView2 viewer
/// <see cref="PdfSlideDisplayService"/> uses for the real, on-screen slide.
/// </summary>
public static class SlideThumbnailService
{
    /// <summary>Returns null on any failure (missing file, corrupt PDF, PowerPoint COM unavailable, ...) —
    /// a missing preview is a cosmetic gap, not something that should ever surface as an error to the
    /// operator or block them from pressing Boshlash.</summary>
    public static async Task<Image?> GetFirstPageThumbnailAsync(string absoluteFilePath, PresentationFileType fileType, int width, int height)
    {
        try
        {
            var pdfPath = fileType == PresentationFileType.Pptx
                ? await PptxToPdfConverter.EnsureConvertedToPdfAsync(absoluteFilePath)
                : absoluteFilePath;

            var pdfBytes = await File.ReadAllBytesAsync(pdfPath);
            var options = new PDFtoImage.RenderOptions(Width: width, Height: height, WithAspectRatio: true);

            using var skBitmap = PDFtoImage.Conversion.ToImage(pdfBytes, page: 0, options: options);
            using var encoded = skBitmap.Encode(SKEncodedImageFormat.Png, 100);
            using var stream = new MemoryStream(encoded.ToArray());
            using var decoded = Image.FromStream(stream);

            // Copied into a fresh Bitmap rather than returning `decoded` directly: GDI+ images loaded via
            // FromStream keep reading from their backing stream lazily, so returning it as-is while `stream`
            // is disposed on the way out would corrupt the image the moment it's actually painted.
            return new Bitmap(decoded);
        }
        catch
        {
            return null;
        }
    }
}
