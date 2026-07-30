using System.Runtime.InteropServices;
using Office = Microsoft.Office.Core;
using PowerPoint = Microsoft.Office.Interop.PowerPoint;

namespace PresentationManager.UI.SlideDisplay;

/// <summary>
/// Silently converts a .ppt/.pptx file to PDF via headless PowerPoint COM automation — WithWindow=false
/// and ExportAsFixedFormat never show any window, unlike running an actual slideshow — so the result can
/// be shown fully embedded through the same WebView2 viewer used for real PDFs, instead of a separate
/// PowerPoint window opening on its own. Trade-off: animations/transitions are flattened to static
/// per-slide pages.
/// </summary>
public static class PptxToPdfConverter
{
    /// <summary>Serializes every conversion app-wide — AdminForm's queue-preview thumbnail
    /// (<see cref="SlideThumbnailService"/>) and PresentationForm's real on-screen open both call into this
    /// converter independently, and two concurrent <c>new PowerPoint.Application()</c> automations racing
    /// each other (e.g. selecting a queue row and immediately pressing Boshlash) is a known source of
    /// intermittent COM errors (RPC_E_CALL_REJECTED/CO_E_SERVER_EXEC_FAILURE) that would otherwise surface
    /// as an unexplained failure to open the slide.</summary>
    private static readonly SemaphoreSlim ConversionLock = new(1, 1);

    /// <summary>Runs the (COM-requires-STA) conversion on its own dedicated STA thread so it doesn't block
    /// the UI thread, then hands the result back via the returned Task.</summary>
    public static Task<string> EnsureConvertedToPdfAsync(string pptxPath)
    {
        var tcs = new TaskCompletionSource<string>();
        var thread = new Thread(() =>
        {
            ConversionLock.Wait();
            try
            {
                tcs.SetResult(EnsureConvertedToPdf(pptxPath));
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
            finally
            {
                ConversionLock.Release();
            }
        })
        {
            IsBackground = true
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }

    private static string EnsureConvertedToPdf(string pptxPath)
    {
        var pdfPath = Path.ChangeExtension(pptxPath, ".pdf");

        // Reuse a previous conversion unless the source file has changed since (e.g. re-uploaded via Edit).
        if (File.Exists(pdfPath) && File.GetLastWriteTimeUtc(pdfPath) >= File.GetLastWriteTimeUtc(pptxPath))
        {
            return pdfPath;
        }

        PowerPoint.Application? app = null;
        PowerPoint.Presentation? presentation = null;
        try
        {
            app = new PowerPoint.Application();
            presentation = app.Presentations.Open(
                pptxPath,
                Office.MsoTriState.msoTrue,  // ReadOnly
                Office.MsoTriState.msoFalse, // Untitled
                Office.MsoTriState.msoFalse  // WithWindow — no window shown at any point
            );

            presentation.ExportAsFixedFormat(pdfPath, PowerPoint.PpFixedFormatType.ppFixedFormatTypePDF);
        }
        finally
        {
            try
            {
                presentation?.Close();
            }
            catch
            {
                // best-effort teardown — the app Quit below is the real safety net
            }

            try
            {
                app?.Quit();
            }
            catch
            {
                // ignored
            }

            if (presentation is not null)
            {
                Marshal.ReleaseComObject(presentation);
            }

            if (app is not null)
            {
                Marshal.ReleaseComObject(app);
            }

            // Without this, orphaned POWERPNT.EXE processes accumulate across the meeting as RCWs go
            // uncollected — releasing the COM objects alone isn't enough to guarantee the process exits.
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        return pdfPath;
    }
}
