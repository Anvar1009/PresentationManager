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

    /// <summary>Generous, but bounded: a hung PowerPoint COM automation (no PowerPoint installed/activated,
    /// a stuck modal dialog on an invisible window, ...) used to wait on <see cref="ConversionLock"/> and
    /// the underlying <c>new PowerPoint.Application()</c> call forever, with no way back except restarting
    /// the whole app - every later queue-preview thumbnail and every later Boshlash press queued up behind
    /// the same never-released lock, which is what "the whole panel stops responding" actually was. This
    /// caps how long any single attempt can block the rest of the app, in exchange for real conversions of
    /// unusually large decks needing to stay under it.</summary>
    private static readonly TimeSpan ConversionTimeout = TimeSpan.FromSeconds(45);

    /// <summary>Runs the (COM-requires-STA) conversion on its own dedicated STA thread so it doesn't block
    /// the UI thread, then hands the result back via the returned Task. Bounded by
    /// <see cref="ConversionTimeout"/> on both ends - waiting for the lock, and waiting for the conversion
    /// itself - so a single stuck attempt degrades to one clear, catchable error instead of wedging every
    /// later slide/thumbnail open for the rest of the session (.NET can't safely abort a thread blocked
    /// inside a native COM call, so a timed-out attempt is abandoned running rather than killed - the next
    /// attempt tries fresh rather than piling up behind it, since the lock itself is also acquired with a
    /// timeout instead of an unbounded wait).</summary>
    public static async Task<string> EnsureConvertedToPdfAsync(string pptxPath)
    {
        if (Type.GetTypeFromProgID("PowerPoint.Application") is null)
        {
            // Fails immediately instead of ever attempting the COM call - this is the actual root cause on
            // a machine where PowerPoint desktop isn't installed/registered, and without this check that
            // machine's very first real .pptx/.ppt open (as opposed to a .pdf one, which never reaches this
            // class at all) is exactly the hang this whole method now guards against.
            throw new InvalidOperationException(
                "PowerPoint dasturi topilmadi. Taqdimotni konvertatsiya qilish uchun shu kompyuterda Microsoft PowerPoint o'rnatilgan bo'lishi kerak.");
        }

        var tcs = new TaskCompletionSource<string>();
        var thread = new Thread(() =>
        {
            if (!ConversionLock.Wait(ConversionTimeout))
            {
                tcs.SetException(new TimeoutException(
                    "Oldingi konvertatsiya hali tugamagan (PowerPoint qotib qolgan bo'lishi mumkin) - operator kompyuterida barcha POWERPNT.EXE jarayonlarini tugating va qayta urinib ko'ring."));
                return;
            }

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

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(ConversionTimeout));
        if (completed != tcs.Task)
        {
            // The background thread is abandoned running (see remarks above) - this call just stops
            // waiting on it so the operator gets their app back instead of a permanent freeze.
            throw new TimeoutException(
                "Taqdimotni PDF'ga aylantirish vaqti tugadi. PowerPoint javob bermayapti - operator kompyuterida barcha POWERPNT.EXE jarayonlarini tugating (Ish jarayonlari boshqaruvchisi orqali) va qaytadan urinib ko'ring.");
        }

        return await tcs.Task;
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
