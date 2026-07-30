using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using PresentationManager.Application.Common;
using PresentationManager.Application.Interfaces;
using PresentationManager.Domain.Enums;

namespace PresentationManager.UI.SlideDisplay;

/// <summary>
/// Renders PDFs using an embedded WebView2 control hosted inside <c>PresentationForm.ContentHost</c>,
/// relying on WebView2's built-in PDF viewer. Fully controlled by this app (reliable open/close/hide),
/// unlike shelling out to whatever the OS's default PDF app happens to be.
/// </summary>
/// <remarks>
/// The WebView2 control is created once and kept alive for the app's lifetime instead of being disposed
/// on every "close" and recreated on every "open". Recreating it repeatedly means creating a brand new
/// CoreWebView2Environment against the same default user-data folder every time — the previous one's
/// msedgewebview2.exe process doesn't always finish tearing down before the next environment tries to
/// claim the same folder, which made every presentation after the very first one fail to open (the
/// exact "works once, then nothing starts again" bug). Navigating to about:blank and hiding the control
/// achieves the same visual "closed" effect without ever touching the environment again.
/// </remarks>
public sealed class PdfSlideDisplayService : ISlideDisplayService
{
    private readonly Panel _hostPanel;
    private WebView2? _webView;
    private bool _open;

    public PdfSlideDisplayService(Panel hostPanel)
    {
        _hostPanel = hostPanel;
    }

    public PresentationFileType SupportedType => PresentationFileType.Pdf;

    public bool IsOpen => _open;

    public async Task OpenAsync(string absoluteFilePath, ScreenBounds targetBounds, CancellationToken ct = default)
    {
        // Navigating to a file:// URI that doesn't exist doesn't reliably surface as a navigation failure
        // below (Chromium's PDF viewer can render its own "couldn't load" page as a successful navigation)
        // — catching the common case explicitly here gives a much clearer error than a silent blank/black
        // screen.
        if (!File.Exists(absoluteFilePath))
        {
            throw new FileNotFoundException("Slayd fayli topilmadi.", absoluteFilePath);
        }

        var isFirstCreate = _webView is null;
        _webView ??= new WebView2 { Dock = DockStyle.Fill };
        if (!_hostPanel.Controls.Contains(_webView))
        {
            _hostPanel.Controls.Add(_webView);
        }

        await _webView.EnsureCoreWebView2Async();

        if (isFirstCreate)
        {
            // Subscribed once, not on every OpenAsync — the WebView2/CoreWebView2 instance now lives for
            // the app's lifetime (see class remarks), so re-subscribing on every open would stack up
            // duplicate handlers that each re-fire on every future navigation.
            //
            // Keyboard Up/Down/PageUp/PageDown to move between slides is Chromium's PDF viewer's own
            // built-in behavior — it only needs the control to actually have keyboard focus, which isn't
            // automatic once it becomes visible inside our own Form. NavigationCompleted re-focuses once
            // the PDF plugin itself is actually ready to receive input (focusing right after Navigate() is
            // too early).
            _webView.CoreWebView2.NavigationCompleted += (_, _) => _webView.Focus();
        }

        // WebView2's built-in PDF viewer defaults to a small, centered zoom level with a lot of empty
        // margin — these are the same "open parameters" Adobe Acrobat/Chromium's PDF viewer support:
        // FitH fits the page to the viewer's width (filling the space properly for a presentation), and
        // toolbar=0/navpanes=0 hide the print/zoom/sidebar chrome that's just clutter for an audience.
        //
        // Navigate() itself is fire-and-forget — awaiting NavigationCompleted here (rather than the old
        // "call it and hope" approach) is what actually catches a failed load instead of silently leaving
        // the operator on a black panel with no error anywhere.
        var completed = await NavigateAndWaitAsync(
            new Uri(absoluteFilePath).AbsoluteUri + "#toolbar=0&navpanes=0&view=FitH");
        if (!completed.IsSuccess)
        {
            throw new InvalidOperationException($"Faylni ochib bo'lmadi ({completed.WebErrorStatus}).");
        }

        _webView.Visible = true;
        _webView.Focus();
        _open = true;
    }

    private Task<CoreWebView2NavigationCompletedEventArgs> NavigateAndWaitAsync(string url)
    {
        var tcs = new TaskCompletionSource<CoreWebView2NavigationCompletedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            _webView!.CoreWebView2.NavigationCompleted -= OnNavigationCompleted;
            tcs.TrySetResult(e);
        }

        _webView!.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
        _webView.CoreWebView2.Navigate(url);
        return tcs.Task;
    }

    public Task ShowAsync(CancellationToken ct = default)
    {
        if (_webView is not null)
        {
            _webView.Visible = true;
            _webView.Focus();
        }

        return Task.CompletedTask;
    }

    public Task HideAsync(CancellationToken ct = default)
    {
        if (_webView is not null)
        {
            _webView.Visible = false;
        }

        return Task.CompletedTask;
    }

    /// <summary>Navigates away and hides the control instead of disposing it — see the class remarks for
    /// why disposing/recreating the WebView2 on every presentation broke every presentation after the
    /// first one.</summary>
    public Task CloseAsync(CancellationToken ct = default)
    {
        if (_webView?.CoreWebView2 is not null)
        {
            _webView.CoreWebView2.Navigate("about:blank");
        }

        if (_webView is not null)
        {
            _webView.Visible = false;
        }

        _open = false;
        return Task.CompletedTask;
    }
}
