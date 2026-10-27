using System.Diagnostics;
using System.Runtime.InteropServices;
using MegaCopier.App.Theming;
using MegaCopier.App.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;

namespace MegaCopier.App;

public sealed partial class MainWindow
{
    private bool _watchingFrames, _detailsTransition;
    private bool _settingsNavigationEntering;
    private bool _resizePreparing, _resizePresenting, _restoreTransitionsDisabled;
    private double _detailsFrom, _detailsTo;
    private long _lastResizeFrame;
    private RectInt32 _resizeArea;
    private int _resizeGeneration, _resizeFrames;
    private (double Width, double Height)? _resizeRestoreFrom;
    private ResizeSurface? _resizeSurface;
    private readonly RenderTargetBitmap _resizeRendered = new();
    private readonly SemaphoreSlim _resizePreparationGate = new(1);
    private Microsoft.UI.Xaml.Media.Animation.TransitionCollection? _tabContentTransitions;

    private void SuspendTabEntrance()
    {
        // La entrada implícita de ContentControl puede reiniciarse al recuperar su alto visible.
        // Más/Menos ya tiene su propio deslizamiento; reservar la entrada para cambiar de pestaña.
        _tabContentTransitions ??= TabHost.ContentTransitions;
        TabHost.ContentTransitions = null;
    }

    private void PrepareAnimatedRestore()
    {
        _resizeRestoreFrom = (AppWindow.Size.Width, AppWindow.Size.Height);
        var client = ResizeClientSize();
        _resizeSurface ??= ResizeSurface.TryCreate(Handle, (int)client.Width, (int)client.Height);
        int disabled = 1;
        _restoreTransitionsDisabled = DwmSetWindowAttribute(Handle, 3, ref disabled, 4) == 0;
    }

    private void AnimateResizeTo(double width, double height)
    {
        // Una copia que llega mientras se decide un conflicto no debe mover el
        // Popup ni sus botones. Aplicar el nuevo tamaño en cuanto desaparezca.
        if (_conflictDialogShowing)
        {
            _resizeAfterConflict = true;
            return;
        }
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Maximized })
        {
            StopResizeAnimation(); ApplyConstraints(); return;
        }
        RelaxConstraints();
        bool restoring = _resizeRestoreFrom is not null;
        _resizeFrom = _resizeRestoreFrom ?? (AppWindow.Size.Width, AppWindow.Size.Height);
        _resizeRestoreFrom = null;
        _resizeTo = (Scaled(width), Scaled(height));
        _resizeArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        _resizeStart = 0;
        int generation = ++_resizeGeneration;
        _resizePreparing = true;
        _resizePresenting = false;
        _resizeFrames = 0;
        // Preparar el búfer completo, conservando el diseño al tamaño visible del padre.
        PinNativeSurface();
        FitScaledRoot();
        if (restoring) { MoveResizeSurface((int)_resizeFrom.Width, (int)_resizeFrom.Height); FitScaledRoot(); }
        if (BarAppearance.ReduceMotion || _resizeFrom == _resizeTo)
        {
            MoveResizeSurface((int)_resizeTo.Width, (int)_resizeTo.Height);
            FinishResize(); return;
        }
        if (!_watchingFrames)
        {
            CompositionTarget.Rendering += OnResizeFrame;
            _watchingFrames = true;
        }
        _lastResizeFrame = Stopwatch.GetTimestamp();
        _resizeTimer.Start();
        _ = PrepareResizeAsync(generation);
    }

    private async Task PrepareResizeAsync(int generation)
    {
        await _resizePreparationGate.WaitAsync();
        try
        {
            await Task.Yield(); // terminar también las visibilidades del controlador de navegación
            if (IsClosed || generation != _resizeGeneration) return;
            Root.UpdateLayout();
            // Reutilizar la textura y serializar su preparación, también con pulsaciones rápidas.
            await _resizeRendered.RenderAsync(Root);
            await PresentResizeAsync(fullPresent: true);
            if (IsClosed || generation != _resizeGeneration) return;
            _resizePreparing = false;
            _resizeStart = 0;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ArgumentException)
        {
            if (generation != _resizeGeneration || IsClosed) return;
            if (App.TestMode) App.TestLog("resize.log", $"prepare-failed={ex.GetType().Name}");
            _resizePreparing = false;
        }
        finally { _resizePreparationGate.Release(); }
    }

    private async Task PresentResizeAsync(bool fullPresent = false)
    {
        await ElementCompositionPreview.GetElementVisual(Root).Compositor.RequestCommitAsync();
        if (fullPresent) await Task.Run(() => DwmFlush());
    }

    private void OnResizeFrame(object? sender, object e)
    {
        _lastResizeFrame = Stopwatch.GetTimestamp();
        ResizeWithoutRendering(force: true);
    }

    private void ResizeWithoutRendering(bool force = false)
    {
        if (!_resizeTimer.IsRunning || _resizePreparing || _resizePresenting || IsClosed) return;
        if (!force && Stopwatch.GetElapsedTime(_lastResizeFrame).TotalMilliseconds < 100) return;
        _ = ResizeStepAsync(_resizeGeneration);
    }

    private async Task ResizeStepAsync(int generation)
    {
        _resizePresenting = true;
        try
        {
            if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Maximized })
            { StopResizeAnimation(); ApplyConstraints(); return; }
            if (_resizeStart == 0) _resizeStart = Stopwatch.GetTimestamp();
            double t = Math.Min(1, Stopwatch.GetElapsedTime(_resizeStart).TotalMilliseconds / 320);
            double eased = t * t * (3 - 2 * t);
            if (_settingsNavigationEntering) SettingsView.SetNavigationEntrance(eased);
            int w = (int)Math.Round(_resizeFrom.Width + (_resizeTo.Width - _resizeFrom.Width) * eased);
            int h = (int)Math.Round(_resizeFrom.Height + (_resizeTo.Height - _resizeFrom.Height) * eased);
            if (_detailsTransition)
            {
                double pixel = (Root.XamlRoot?.RasterizationScale ?? 1) * UiScale;
                DetailsRevealTransform.TranslateY = Math.Round((_detailsFrom + (_detailsTo - _detailsFrom) * eased) * pixel) / pixel;
            }
            // Solo el búfer conserva el tamaño completo: el diseño sigue el área visible del padre.
            MoveResizeSurface(w, h);
            FitScaledRoot();
            Root.UpdateLayout();
            _programmaticUntil = Stopwatch.GetTimestamp() + Stopwatch.Frequency / 3;
            if (App.TestMode)
                App.TestLog("resize-frames.log", $"{++_resizeFrames};size={w}x{h};slide={DetailsRevealTransform.TranslateY:0.00};t={t:0.000};root={Root.Width:0.00}x{Root.Height:0.00};panel={PanelCard.ActualWidth:0.00};detail={ExpandedDetails.ActualWidth:0.00}");
            // Rendering agrupa marco y diseño en el mismo fotograma. El búfer estable permite
            // preparar el siguiente sin esperar otra confirmación y perder un refresco de pantalla.
            // Al acabar sí se confirma el dibujo antes de liberar la superficie temporal.
            if (t >= 1)
            {
                await PresentResizeAsync();
                if (!IsClosed && generation == _resizeGeneration) FinishResize();
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ArgumentException)
        {
            if (generation == _resizeGeneration && !IsClosed)
            { if (App.TestMode) App.TestLog("resize.log", $"frame-failed={ex.GetType().Name}"); FinishResize(); }
        }
        finally { if (generation == _resizeGeneration) _resizePresenting = false; }
    }

    private void MoveResizeSurface(int width, int height)
    {
        if (AppWindow.Size.Width == width && AppWindow.Size.Height == height) return;
        int x = Math.Clamp(AppWindow.Position.X, _resizeArea.X, Math.Max(_resizeArea.X, _resizeArea.X + _resizeArea.Width - width));
        int y = Math.Clamp(AppWindow.Position.Y, _resizeArea.Y, Math.Max(_resizeArea.Y, _resizeArea.Y + _resizeArea.Height - height));
        AppWindow.MoveAndResize(new RectInt32(x, y, width, height));
    }

    private void PinNativeSurface()
    {
        var client = ResizeClientSize();
        int width = (int)(client.Width + Math.Max(_resizeFrom.Width, _resizeTo.Width) - AppWindow.Size.Width);
        int height = (int)(client.Height + Math.Max(_resizeFrom.Height, _resizeTo.Height) - AppWindow.Size.Height);
        if (_resizeSurface is not null) _resizeSurface.SetSize(width, height);
        else _resizeSurface = ResizeSurface.TryCreate(Handle, width, height);
        if (App.TestMode) App.TestLog("resize.log", $"native-surface={_resizeSurface is not null};buffer={width}x{height}");
    }

    private void FinishResize()
    {
        // Ocultar lo que se pliega antes de liberar el diseño, no un fotograma después.
        if (!_expanded) ExpandedDetails.Visibility = Visibility.Collapsed;
        MoveResizeSurface((int)_resizeTo.Width, (int)_resizeTo.Height);
        if (App.TestMode) App.TestLog("resize.log", $"page={PageOpen};frames={_resizeFrames};size={AppWindow.Size.Width}x{AppWindow.Size.Height};target={_resizeTo.Width}x{_resizeTo.Height}");
        StopResizeAnimation();
        ApplyConstraints();
    }

    private void StopResizeAnimation()
    {
        _resizeGeneration++;
        _resizeTimer.Stop();
        if (_watchingFrames) { CompositionTarget.Rendering -= OnResizeFrame; _watchingFrames = false; }
        _resizeStart = 0;
        _resizePreparing = _resizePresenting = _detailsTransition = false;
        _resizeRestoreFrom = null;
        if (_resizeSurface is { } surface)
        {
            if (!IsClosed && GetClientRect(Handle, out var current))
                surface.SetSize(current.Right - current.Left, current.Bottom - current.Top);
            surface.Dispose();
            _resizeSurface = null;
        }
        Root.Clip = null;
        if (_settingsNavigationEntering)
        {
            SettingsView.SetNavigationEntrance(1);
            _settingsNavigationEntering = false;
        }
        DetailsRevealTransform.TranslateY = 0;
        ExpandedDetails.Visibility = _expanded ? Visibility.Visible : Visibility.Collapsed;
        if (_restoreTransitionsDisabled)
        {
            _restoreTransitionsDisabled = false;
            int enabled = 0;
            if (!IsClosed) DwmSetWindowAttribute(Handle, 3, ref enabled, 4);
        }
        if (!IsClosed) FitScaledRoot();
    }

    private (double Width, double Height) ResizeClientSize()
    {
        if (GetClientRect(Handle, out var rect)) return (rect.Right - rect.Left, rect.Bottom - rect.Top);
        double dpi = Root.XamlRoot?.RasterizationScale ?? 1;
        return ((_scaleHost?.ActualWidth ?? 0) * dpi, (_scaleHost?.ActualHeight ?? 0) * dpi);
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct ResizeClientRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(nint hwnd, out ResizeClientRect rect);
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, uint attribute, ref int value, uint size);
    [DllImport("dwmapi.dll")]
    private static extern int DwmFlush();
}
