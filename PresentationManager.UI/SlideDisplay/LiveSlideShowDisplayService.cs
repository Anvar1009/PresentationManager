using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using PresentationManager.Application.Common;
using PresentationManager.Application.Interfaces;
using PresentationManager.Domain.Enums;
using Office = Microsoft.Office.Core;
using PowerPoint = Microsoft.Office.Interop.PowerPoint;

namespace PresentationManager.UI.SlideDisplay;

/// <summary>
/// Runs a .pptx/.ppt as a real, live PowerPoint slideshow - not the flattened-to-static-PDF path
/// <see cref="PptxToPdfConverter"/>/<see cref="PdfSlideDisplayService"/> use - and embeds that slideshow's own
/// native window directly inside the operator's <see cref="Panel"/> host via Win32 window reparenting
/// (SetParent), the same way an in-process control would sit there. Because it's the real PowerPoint engine
/// rendering, animations/transitions and embedded video+audio all play exactly as they would in PowerPoint
/// itself, and a physical presentation clicker (which just sends PageDown/Right-arrow keystrokes) advances
/// slides on its own the instant the embedded window has keyboard focus - no code here needs to know a
/// clicker exists at all.
/// </summary>
/// <remarks>
/// <para><b>Why a dedicated worker thread, not just a background STA thread per call (unlike
/// <see cref="PptxToPdfConverter"/>):</b> that class opens, converts, and closes everything within one single
/// call, so a fire-and-forget STA thread is enough. Here the whole point is that the slideshow stays open
/// across several separate calls (<see cref="OpenAsync"/>, then <see cref="ShowAsync"/>/<see cref="HideAsync"/>
/// for discussion breaks, then eventually <see cref="CloseAsync"/>) - COM objects must only ever be touched
/// from the single STA thread that created them, so this keeps one dedicated thread alive with a work queue
/// for as long as a slideshow is open, and every COM call (not the plain Win32 window calls, which are safe
/// from any thread) is marshaled onto it.</para>
/// <para><b>Known rough edges that need validating on real hardware</b> (this can't be exercised inside a
/// sandbox with no PowerPoint/no physical clicker): reparented-window resize/DPI behavior across different
/// monitor configurations, and whether PowerPoint's own slideshow window ever tries to reassert itself as
/// topmost/exclusive after being reparented. If either turns out to be a real problem in practice, the
/// existing <see cref="PdfSlideDisplayService"/> path (assign <see cref="PresentationFileType.Pptx"/> back to
/// it, going through <see cref="PptxToPdfConverter"/> again) remains a safe fallback.</para>
/// </remarks>
public sealed class LiveSlideShowDisplayService : ISlideDisplayService
{
    /// <summary>Same trade-off as <see cref="PptxToPdfConverter.ConversionTimeout"/> - bounds how long any
    /// single COM call is allowed to block before this class gives up and surfaces a catchable error instead
    /// of the operator's whole app silently hanging.</summary>
    private static readonly TimeSpan ComCallTimeout = TimeSpan.FromSeconds(30);

    private readonly Panel _hostPanel;

    private Thread? _comThread;
    private BlockingCollection<Action>? _workQueue;

    private PowerPoint.Application? _app;
    private PowerPoint.Presentation? _presentation;
    private PowerPoint.SlideShowWindow? _slideShowWindow;
    private IntPtr _slideShowHwnd;

    public LiveSlideShowDisplayService(Panel hostPanel)
    {
        _hostPanel = hostPanel;
        _hostPanel.Resize += (_, _) => ResizeEmbeddedWindow();
    }

    public PresentationFileType SupportedType => PresentationFileType.Pptx;

    public bool IsOpen { get; private set; }

    public async Task OpenAsync(string absoluteFilePath, ScreenBounds targetBounds, CancellationToken ct = default)
    {
        if (!File.Exists(absoluteFilePath))
        {
            throw new FileNotFoundException("Slayd fayli topilmadi.", absoluteFilePath);
        }

        if (IsOpen)
        {
            await CloseAsync(ct);
        }

        if (Type.GetTypeFromProgID("PowerPoint.Application") is null)
        {
            throw new InvalidOperationException(
                "PowerPoint dasturi topilmadi. Taqdimotni jonli (animatsiya/video bilan) ko'rsatish uchun shu kompyuterda Microsoft PowerPoint o'rnatilgan bo'lishi kerak.");
        }

        StartComThread();

        try
        {
            await RunOnComThreadAsync(() =>
            {
                _app = new PowerPoint.Application();
                // Suppresses every COM-automation alert PowerPoint would otherwise pop up during this
                // session - the one that actually mattered in practice was "Do you want to save changes to
                // <file>?" on close, which PowerPoint can ask even for a ReadOnly-opened presentation (just
                // running the slideshow can dirty transient state like the last-viewed-slide bookmark). Safe
                // here specifically because this app never intends to persist anything back into the
                // presenter's source file - see CloseAsync's Saved=true below for the same guarantee applied
                // a second way, directly on the presentation itself rather than relying on this app-wide
                // setting alone.
                _app.DisplayAlerts = PowerPoint.PpAlertLevel.ppAlertsNone;
                _presentation = _app.Presentations.Open(
                    absoluteFilePath,
                    Office.MsoTriState.msoTrue,  // ReadOnly
                    Office.MsoTriState.msoFalse, // Untitled
                    Office.MsoTriState.msoFalse  // WithWindow - no edit window; only the slideshow window below matters
                );

                var settings = _presentation.SlideShowSettings;

                // "In a window" (not the default full-screen kiosk/speaker mode) is what makes this
                // embeddable at all - a full-screen slideshow takes over the monitor at the OS level and
                // ignores our window hierarchy entirely, so there would be nothing to reparent.
                settings.ShowType = PowerPoint.PpSlideShowType.ppShowTypeWindow;

                _slideShowWindow = settings.Run();
                _slideShowHwnd = new IntPtr(_slideShowWindow.HWND);

                EmbedIntoHostPanel(_slideShowHwnd);
            }, ct);
        }
        catch
        {
            StopComThread();
            throw;
        }

        IsOpen = true;
    }

    public Task ShowAsync(CancellationToken ct = default)
    {
        if (_slideShowHwnd != IntPtr.Zero)
        {
            NativeMethods.ShowWindow(_slideShowHwnd, NativeMethods.SW_SHOW);
            ResizeEmbeddedWindow();
            // The clicker sends its Next/Previous keystrokes straight to whichever window has OS keyboard
            // focus - without this, they'd silently do nothing until the operator clicked the embedded
            // window with a mouse first.
            NativeMethods.SetForegroundWindow(_slideShowHwnd);
        }

        return Task.CompletedTask;
    }

    public Task HideAsync(CancellationToken ct = default)
    {
        if (_slideShowHwnd != IntPtr.Zero)
        {
            NativeMethods.ShowWindow(_slideShowHwnd, NativeMethods.SW_HIDE);
        }

        return Task.CompletedTask;
    }

    public async Task CloseAsync(CancellationToken ct = default)
    {
        if (!IsOpen)
        {
            return;
        }

        try
        {
            await RunOnComThreadAsync(() =>
            {
                try { _slideShowWindow?.View.Exit(); } catch { /* best-effort - Quit below is the real teardown */ }
                // Belt-and-suspenders alongside DisplayAlerts=ppAlertsNone in OpenAsync - marking the
                // presentation itself as "nothing to save" means PowerPoint has no reason to prompt on Close
                // even if some other code path ever touches DisplayAlerts back to its default in between.
                try { if (_presentation is not null) _presentation.Saved = Office.MsoTriState.msoTrue; } catch { /* ignored */ }
                try { _presentation?.Close(); } catch { /* ignored, same as PptxToPdfConverter */ }
                try { _app?.Quit(); } catch { /* ignored */ }

                if (_slideShowWindow is not null) Marshal.ReleaseComObject(_slideShowWindow);
                if (_presentation is not null) Marshal.ReleaseComObject(_presentation);
                if (_app is not null) Marshal.ReleaseComObject(_app);

                _slideShowWindow = null;
                _presentation = null;
                _app = null;

                // Without this, orphaned POWERPNT.EXE processes accumulate across the event as RCWs go
                // uncollected - same reasoning as PptxToPdfConverter's identical cleanup step.
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }, ct);
        }
        finally
        {
            StopComThread();
            _slideShowHwnd = IntPtr.Zero;
            IsOpen = false;
        }
    }

    private void EmbedIntoHostPanel(IntPtr hwnd)
    {
        // Strip title bar/borders/system-menu and the popup/topmost styles a standalone slideshow window
        // normally has, mark it a child window instead, then hand it over to the host panel - the same shape
        // of operation as reparenting any native HWND into a .NET container.
        var style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_STYLE).ToInt64();
        style &= ~(NativeMethods.WS_POPUP | NativeMethods.WS_CAPTION | NativeMethods.WS_THICKFRAME
            | NativeMethods.WS_SYSMENU | NativeMethods.WS_MAXIMIZEBOX | NativeMethods.WS_MINIMIZEBOX);
        style |= NativeMethods.WS_CHILD;
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_STYLE, new IntPtr(style));

        var exStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        exStyle &= ~(NativeMethods.WS_EX_TOPMOST | NativeMethods.WS_EX_DLGMODALFRAME);
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(exStyle));

        NativeMethods.SetParent(hwnd, _hostPanel.Handle);

        NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, _hostPanel.ClientSize.Width, _hostPanel.ClientSize.Height,
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_FRAMECHANGED);
        NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOW);
    }

    /// <summary>Plain Win32 window calls (unlike the COM calls above) are safe from any thread, so this runs
    /// directly on whichever thread raises <see cref="Panel.Resize"/> (the UI thread) with no COM-thread
    /// marshaling needed.</summary>
    private void ResizeEmbeddedWindow()
    {
        if (_slideShowHwnd == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.MoveWindow(_slideShowHwnd, 0, 0, _hostPanel.ClientSize.Width, _hostPanel.ClientSize.Height, true);
    }

    /// <summary>How often the idle COM thread (see <see cref="StartComThread"/>) checks for PowerPoint's own
    /// "end of slide show, click to exit" black screen and snaps back to the last real slide instead -
    /// frequent enough that the black screen is never really visible, without the COM thread just running a
    /// tight, no-yield state-check loop.</summary>
    private static readonly TimeSpan EndOfShowPollInterval = TimeSpan.FromMilliseconds(200);

    private void StartComThread()
    {
        _workQueue = new BlockingCollection<Action>();
        _comThread = new Thread(() =>
        {
            // TryTake with a timeout (rather than GetConsumingEnumerable's unbounded wait) so this thread
            // gets a chance to run TrapAtLastSlideIfDone between queued Open/Close calls - both run on this
            // same STA thread since the COM objects below can only ever be touched from the thread that
            // created them.
            while (!_workQueue.IsCompleted)
            {
                if (_workQueue.TryTake(out var action, EndOfShowPollInterval))
                {
                    action();
                }
                else
                {
                    TrapAtLastSlideIfDone();
                }
            }
        })
        {
            IsBackground = true
        };
        _comThread.SetApartmentState(ApartmentState.STA);
        _comThread.Start();
    }

    /// <summary>By default, advancing past a presentation's last slide shows PowerPoint's own black "end of
    /// slide show, click to exit" screen, and a further click/keypress closes the slideshow entirely - not
    /// what a clicker-driven presentation wants (the operator asked for it to simply stop on the last slide).
    /// Polled from the idle COM thread loop instead of handled via a PowerPoint "SlideShowNextSlide" COM
    /// event, since a proper event sink needs this thread pumping Windows messages (Application.Run-style),
    /// which the plain work-queue loop above deliberately doesn't do - polling needs no message pump and is
    /// simple to reason about at the cost of an up-to-<see cref="EndOfShowPollInterval"/> delay before the
    /// correction lands, which in practice reads as instant.</summary>
    private void TrapAtLastSlideIfDone()
    {
        try
        {
            if (_slideShowWindow is null || _presentation is null)
            {
                return;
            }

            if (_slideShowWindow.View.State != PowerPoint.PpSlideShowState.ppSlideShowDone)
            {
                return;
            }

            var lastSlide = _presentation.Slides.Count;
            if (lastSlide > 0)
            {
                _slideShowWindow.View.GotoSlide(lastSlide);
            }
        }
        catch
        {
            // Best-effort - a transient COM error here (e.g. the window was just closed from CloseAsync on
            // another call) must not take down the polling loop.
        }
    }

    private void StopComThread()
    {
        _workQueue?.CompleteAdding();
        _comThread?.Join(TimeSpan.FromSeconds(5));
        _workQueue?.Dispose();
        _workQueue = null;
        _comThread = null;
    }

    private async Task RunOnComThreadAsync(Action action, CancellationToken ct)
    {
        if (_workQueue is null)
        {
            throw new InvalidOperationException("Slaydshou uchun ichki jarayon ishga tushmagan.");
        }

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _workQueue.Add(() =>
        {
            try
            {
                action();
                tcs.SetResult();
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(ComCallTimeout, ct));
        if (completed != tcs.Task)
        {
            throw new TimeoutException(
                "PowerPoint javob bermayapti (vaqt tugadi). Operator kompyuterida barcha POWERPNT.EXE jarayonlarini tugating va qaytadan urinib ko'ring.");
        }

        await tcs.Task;
    }

    private static class NativeMethods
    {
        public const int GWL_STYLE = -16;
        public const int GWL_EXSTYLE = -20;

        public const long WS_POPUP = 0x80000000;
        public const long WS_CAPTION = 0x00C00000;
        public const long WS_THICKFRAME = 0x00040000;
        public const long WS_SYSMENU = 0x00080000;
        public const long WS_MAXIMIZEBOX = 0x00010000;
        public const long WS_MINIMIZEBOX = 0x00020000;
        public const long WS_CHILD = 0x40000000;

        public const long WS_EX_TOPMOST = 0x00000008;
        public const long WS_EX_DLGMODALFRAME = 0x00000001;

        public const uint SWP_NOZORDER = 0x0004;
        public const uint SWP_NOACTIVATE = 0x0010;
        public const uint SWP_FRAMECHANGED = 0x0020;

        public const int SW_HIDE = 0;
        public const int SW_SHOW = 5;

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)]
        public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
        public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool MoveWindow(IntPtr hWnd, int x, int y, int width, int height, [MarshalAs(UnmanagedType.Bool)] bool repaint);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr hWnd);
    }
}
