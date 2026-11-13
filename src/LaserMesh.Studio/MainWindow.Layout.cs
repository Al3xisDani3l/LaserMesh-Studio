using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace LaserMesh.Studio;

public partial class MainWindow
{
    private double explorerWidth = 235;
    private double inspectorWidth = 280;
    private void OnToggleExplorer(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem item) SetPanel(true, item.IsChecked);
    }
    private void OnToggleInspector(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem item) SetPanel(false, item.IsChecked);
    }
    private void SetPanel(bool explorer, bool visible)
    {
        var column = explorer ? WorkspaceGrid.ColumnDefinitions[0] : WorkspaceGrid.ColumnDefinitions[4];
        var panel = explorer ? ExplorerDock : InspectorDock;
        if (!visible && column.ActualWidth > 0)
        { if (explorer) explorerWidth = column.ActualWidth; else inspectorWidth = column.ActualWidth; }
        column.MinWidth = visible ? explorer ? 180 : 220 : 0;
        column.Width = new GridLength(visible ? explorer ? explorerWidth : inspectorWidth : 0);
        panel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }
    private void OnResetLayout(object sender, RoutedEventArgs e)
    {
        explorerWidth = 235; inspectorWidth = 280; ShowExplorerMenu.IsChecked = true; ShowInspectorMenu.IsChecked = true;
        SetPanel(true, true); SetPanel(false, true);
    }
    private async void OnRestoreLayout(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = LayoutPath;
            if (!File.Exists(path) || new FileInfo(path).Length > 4096) return;
            var layout = JsonSerializer.Deserialize<WorkspaceLayout>(await File.ReadAllBytesAsync(path));
            if (layout is null || !double.IsFinite(layout.Explorer) || !double.IsFinite(layout.Inspector)) return;
            explorerWidth = Math.Clamp(layout.Explorer, 180, Math.Max(180, ActualWidth * 0.25));
            inspectorWidth = Math.Clamp(layout.Inspector, 220, Math.Max(220, ActualWidth * 0.3));
            ShowExplorerMenu.IsChecked = layout.ShowExplorer; ShowInspectorMenu.IsChecked = layout.ShowInspector;
            SetPanel(true, layout.ShowExplorer); SetPanel(false, layout.ShowInspector);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { Serilog.Log.Warning(ex, "Could not restore workspace layout"); }
    }
    private async Task SaveLayoutAsync()
    {
        var state = new WorkspaceLayout(ExplorerDock.IsVisible ? WorkspaceGrid.ColumnDefinitions[0].ActualWidth : explorerWidth,
            InspectorDock.IsVisible ? WorkspaceGrid.ColumnDefinitions[4].ActualWidth : inspectorWidth,
            ExplorerDock.Visibility == Visibility.Visible, InspectorDock.Visibility == Visibility.Visible);
        try
        {
            var path = LayoutPath; Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path + ".tmp", JsonSerializer.SerializeToUtf8Bytes(state));
            File.Move(path + ".tmp", path, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { Serilog.Log.Warning(ex, "Could not persist workspace layout"); }
    }
    private static string LayoutPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LaserMeshStudio", "layout.json");
    private sealed record WorkspaceLayout(double Explorer, double Inspector, bool ShowExplorer, bool ShowInspector);
}
