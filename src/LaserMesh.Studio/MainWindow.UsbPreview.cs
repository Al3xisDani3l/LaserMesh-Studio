using System.IO;
using System.Numerics;
using System.Threading.Channels;
using System.Windows;
using System.Windows.Controls;
using LaserMesh.Engraving;
using LaserMesh.Hardware.LaserPecker;
using Serilog;

namespace LaserMesh.Studio;

public partial class MainWindow
{
    private readonly UsbPreviewSession usbPreview = new();
    private Vector2 previewOffset;
    private Channel<UsbPreviewJob>? liveUpdates;
    private CancellationTokenSource? liveCancellation;
    private Task? liveTask;
    private CancellationTokenSource? liveDebounce;
    private bool liveStarting;

    private async void OnLivePreview(object sender, RoutedEventArgs e)
    {
        if (liveStarting || liveTask is not null || usbPreview.UnknownState)
            return;
        liveStarting = true;
        LivePreview.IsEnabled = false;
        try
        {
            if (placementGuide is null)
            {
                placementEnabled = true;
                await RefreshPlacementAsync();
                if (placementGuide is null)
                    return;
            }
            var guide = placementGuide;
            var rectangle = PlacementModePicker.SelectedIndex == 1;
            var job = await Task.Run(() => UsbPreviewJob.Create(guide.Loops, rectangle));
            if (placementGuide != guide || closing)
                return;
            var port = UsbPort.Text.Trim();
            liveUpdates = Channel.CreateBounded<UsbPreviewJob>(new BoundedChannelOptions(1)
            {
                FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true, SingleWriter = true
            });
            liveUpdates.Writer.TryWrite(job);
            liveCancellation = new CancellationTokenSource();
            LivePreview.IsEnabled = false;
            UsbPort.IsEnabled = false;
            StopPreview.IsEnabled = true;
            StatusStopPreview.Visibility = Visibility.Visible;
            UsbPreviewStatus.Text = "USB · verificando reposo y firmware…";
            Log.Information("Starting explicit LP2 USB preview on {Port}", port);
            liveTask = usbPreview.RunAsync(() => new UsbPreviewConnection(port), liveUpdates.Reader,
                new Progress<string>(message =>
                {
                    UsbPreviewStatus.Text = message;
                    viewModel.ReportDeviceStatus(message);
                    Log.Information("LP2 preview: {State}", message);
                }), liveCancellation.Token);
            await liveTask;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            UsbPreviewStatus.Text = ex.Message; viewModel.ReportDeviceStatus(ex.Message);
            Log.Error(ex, "USB preview ended with an error");
        }
        finally
        {
            liveDebounce?.Cancel();
            liveUpdates = null;
            liveCancellation?.Dispose();
            liveCancellation = null;
            liveTask = null;
            liveStarting = false;
            LivePreview.IsEnabled = !usbPreview.UnknownState;
            UsbPort.IsEnabled = true;
            StopPreview.IsEnabled = false;
            StatusStopPreview.Visibility = Visibility.Collapsed;
        }
    }

    private async void QueueLiveGuide(PlacementGuide guide)
    {
        if (gestureStart is not null || viewModel.IsEditingNumbers || liveUpdates is not { } channel || liveCancellation is not { IsCancellationRequested: false })
            return;
        liveDebounce?.Cancel();
        using var debounce = new CancellationTokenSource();
        liveDebounce = debounce;
        try
        {
            await Task.Delay(400, debounce.Token);
            var rectangle = PlacementModePicker.SelectedIndex == 1;
            var job = await Task.Run(() => UsbPreviewJob.Create(guide.Loops, rectangle), debounce.Token);
            if (liveDebounce == debounce && liveUpdates == channel)
                channel.Writer.TryWrite(job);
        }
        catch (OperationCanceledException) { }
        catch (ArgumentException ex)
        {
            await StopLiveAsync();
            UsbPreviewStatus.Text = $"Guía fuera del campo USB o demasiado compleja. {ex.Message}";
        }
        finally
        {
            if (liveDebounce == debounce) liveDebounce = null;
        }
    }

    private void OnMovePreview(object sender, RoutedEventArgs e)
    {
        var delta = (string)((Button)sender).Tag;
        previewOffset = delta switch
        {
            "-1,0" => previewOffset + new Vector2(-1, 0),
            "1,0" => previewOffset + new Vector2(1, 0),
            "0,-1" => previewOffset + new Vector2(0, -1),
            "0,1" => previewOffset + new Vector2(0, 1),
            _ => Vector2.Zero
        };
        PreviewOffset.Text = $"ΔX {previewOffset.X:0.###} · ΔY {previewOffset.Y:0.###} mm";
        placementEnabled = true;
        RefreshPlacement();
    }

    private async void OnStopPreview(object sender, RoutedEventArgs e) => await StopLiveAsync();

    private async Task StopLiveAsync()
    {
        liveDebounce?.Cancel();
        liveCancellation?.Cancel();
        if (liveTask is { } task)
        {
            try { await task; }
            catch (Exception ex) when (ex is OperationCanceledException or IOException or TimeoutException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
            {
                if (ex is not OperationCanceledException) UsbPreviewStatus.Text = ex.Message;
            }
        }
        await viewModel.StopRasterAsync();
    }

    private void OnPhysicalEngravingChanged(bool active)
    {
        StatusStopPreview.Visibility = active || liveTask is not null ? Visibility.Visible : Visibility.Collapsed;
        StopPreview.IsEnabled = active || liveTask is not null;
        LivePreview.IsEnabled = !active && liveTask is null && !usbPreview.UnknownState;
    }
}
