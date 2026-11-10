using System.IO;
using System.Windows;
using LaserMesh.Engraving;
using LaserMesh.Geometry;
using LaserMesh.Hardware.LaserPecker.Bluetooth;
using LaserMesh.Projects;
using LaserMesh.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace LaserMesh.Studio;

/// <summary>Composes the desktop application and its local services.</summary>
public partial class App : Application
{
    private IHost? host;

    /// <inheritdoc/>
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LaserMeshStudio");
            var builder = Host.CreateApplicationBuilder();
            builder.Services.AddSerilog(configuration => configuration.MinimumLevel.Information()
                .WriteTo.File(Path.Combine(data, "logs", "studio-.log"), rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7));
            builder.Services.AddSingleton<StlCodec>();
            builder.Services.AddSingleton<ProjectStore>();
            builder.Services.AddSingleton<MeshAdapter>();
            builder.Services.AddSingleton<GCodeCodec>();
            builder.Services.AddSingleton<LaserPeckerBluetooth>();
            builder.Services.AddSingleton(new RecentProjects(Path.Combine(data, "recent.json")));
            builder.Services.AddSingleton<StudioViewModel>();
            builder.Services.AddSingleton(sp => new MainWindow(sp.GetRequiredService<StudioViewModel>(),
                sp.GetRequiredService<MeshAdapter>()));
            host = builder.Build();
            await host.StartAsync();
            MainWindow = host.Services.GetRequiredService<MainWindow>();
            MainWindow.Show();
            if (e.Args.Length > 0)
                await OpenStartupFileAsync(e.Args, host.Services.GetRequiredService<StudioViewModel>());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceError(ex.ToString());
            MessageBox.Show(ex.Message, "Error al iniciar LaserMesh Studio", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private async Task OpenStartupFileAsync(string[] args, StudioViewModel studio)
    {
        if (args is ["--gcode", var gcode])
        {
            await studio.OpenToolpathFileAsync(Path.GetFullPath(gcode));
            return;
        }
        if (args is [var project] && string.Equals(Path.GetExtension(project), ".lmeshproj", StringComparison.OrdinalIgnoreCase))
        {
            await studio.OpenRecentAsync(Path.GetFullPath(project));
            return;
        }

        if (args is ["--stl", var path, var unit] && string.Equals(Path.GetExtension(path), ".stl", StringComparison.OrdinalIgnoreCase))
        {
            var factor = unit.ToLowerInvariant() switch { "mm" => 1f, "cm" => 10f, "m" => 1000f, "in" => 25.4f, _ => 0f };
            if (factor > 0)
            {
                await studio.ImportFileAsync(Path.GetFullPath(path), factor);
                return;
            }
        }

        MessageBox.Show(MainWindow, "Usa un archivo .lmeshproj, --stl archivo.stl mm|cm|m|in o --gcode archivo.gcode.",
            "Argumentos de inicio", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <inheritdoc/>
    protected override void OnExit(ExitEventArgs e)
    {
        host?.Dispose();
        base.OnExit(e);
    }
}
