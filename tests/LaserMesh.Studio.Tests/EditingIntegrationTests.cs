using System.Numerics;
using System.IO;
using System.Windows.Threading;
using LaserMesh.Studio;
using LaserMesh.Rendering;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LaserMesh.Projects;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LaserMesh.Studio.Tests;

public sealed class EditingIntegrationTests
{
    [Fact]
    public Task MultipleSelectionGestureAndUndoRestoreRelativePosesAndSelection() => OnDispatcher(async () =>
    {
        var model = Create(); var sample = Path.Combine(AppContext.BaseDirectory, "sample.stl");
        await model.ImportFilesAsync([sample, sample], 1);
        Assert.Equal(2, model.Models.Count); Assert.Equal(2, model.SelectedItems.Count);
        var identities = model.Models.Select(m => m.Model.Id).ToArray();
        model.BeginGesture(); model.UpdateGesture(Matrix4x4.CreateTranslation(8, 3, 2)); model.EndGesture(false);
        Assert.All(model.Models, m => Assert.Equal(8, m.Model.Pose.X, 4));
        await model.UndoCommand.ExecuteAsync(null);
        Assert.All(model.Models, m => Assert.Equal(0, m.Model.Pose.X, 4));
        Assert.Equal(identities.Order(), model.Selection.Ids.Order());
        await model.RedoCommand.ExecuteAsync(null);
        Assert.All(model.Models, m => Assert.Equal(2, m.Model.Pose.Z, 4));
        model.BeginGesture(); model.UpdateGesture(Matrix4x4.CreateTranslation(100, 0, 0)); model.EndGesture(true);
        Assert.All(model.Models, m => Assert.Equal(8, m.Model.Pose.X, 4));
    });

    [Fact]
    public Task NumericEditsAreLiveAndOneTransactionWithNoApplyButton() => OnDispatcher(async () =>
    {
        var model = Create(); await model.ImportFilesAsync([Path.Combine(AppContext.BaseDirectory, "sample.stl")], 1);
        model.X = "4"; Assert.Equal(4, model.Selected!.Model.Pose.X, 4);
        model.X = "7"; model.Y = "3"; model.CommitNumericEdit();
        Assert.Equal(7, model.Selected!.Model.Pose.X, 4); Assert.Equal(3, model.Selected.Model.Pose.Y, 4);
        await model.UndoCommand.ExecuteAsync(null);
        Assert.Equal(0, model.Selected!.Model.Pose.X, 4); Assert.Equal(0, model.Selected.Model.Pose.Y, 4);
        model.X = "-"; Assert.Equal(0, model.Selected.Model.Pose.X, 4); model.CancelNumericEdit();
        Assert.Equal("0", model.X);
    });

    [Fact]
    public Task GroupDuplicationAndLockingUseSharedSelectionState() => OnDispatcher(async () =>
    {
        var model = Create(); var sample = Path.Combine(AppContext.BaseDirectory, "sample.stl");
        await model.ImportFilesAsync([sample, sample], 1);
        model.GroupSelectionCommand.Execute(null);
        Assert.Single(model.Models.Select(m => m.Model.GroupId).Distinct());
        model.SelectItems([]); model.SelectHit(model.Models[0], false); Assert.Equal(2, model.SelectedItems.Count);
        model.ToggleLockCommand.Execute(model.Models[0]);
        model.BeginGesture(); model.UpdateGesture(Matrix4x4.CreateTranslation(10, 0, 0)); model.EndGesture(false);
        Assert.Equal(0, model.Models[0].Model.Pose.X, 4); Assert.Equal(10, model.Models[1].Model.Pose.X, 4);
        model.DuplicateCommand.Execute(null); Assert.Equal(4, model.Models.Count); Assert.Equal(2, model.SelectedItems.Count);
    });

    [Fact]
    public Task ImageAtlasRasterAndVersionTwoProjectRemainAssociatedAfterReopen() => OnDispatcher(async () =>
    {
        var imagePath = Path.Combine(Path.GetTempPath(), $"image-{Guid.NewGuid():N}.png");
        var projectPath = Path.Combine(Path.GetTempPath(), $"image-project-{Guid.NewGuid():N}.lmeshproj");
        try
        {
            var pixels = new byte[] { 0, 0, 0, 255, 255, 255, 255, 255, 255, 255, 255, 255, 0, 0, 0, 255 };
            var bitmap = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, pixels, 8); bitmap.Freeze();
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(imagePath)) encoder.Save(stream);
            var model = Create(); await model.ImportFilesAsync([Path.Combine(AppContext.BaseDirectory, "sample.stl")], 1);
            await model.AddImageFileAsync(imagePath, model.Models[0].Model.Id);
            Assert.Single(model.Designs); Assert.Equal(model.Models[0].Model.Id, model.Designs[0].ModelId);
            await model.GenerateUvCommand.ExecuteAsync(null);
            Assert.NotNull(model.SelectedDesign!.AtlasId);
            await model.PrepareRasterCommand.ExecuteAsync(null);
            Assert.NotNull(model.PreparedImage); Assert.NotNull(model.RasterPreview);
            var store = new ProjectStore(new()); await store.SaveSceneAsync(projectPath, model.CaptureDocument(), default);
            var restored = Create(); await restored.OpenRecentAsync(projectPath);
            Assert.Single(restored.Designs); Assert.Equal(DesignProjection.Uv, restored.Designs[0].Projection);
            Assert.Equal(2, restored.CaptureDocument().Assets.Count);
            await restored.PrepareRasterCommand.ExecuteAsync(null); Assert.NotNull(restored.PreparedImage);
            restored.BeginGesture(); restored.UpdateGesture(Matrix4x4.CreateTranslation(1, 0, 0)); restored.EndGesture(false);
            Assert.Null(restored.PreparedImage);
        }
        finally { File.Delete(imagePath); File.Delete(projectPath); }
    });

    private static StudioViewModel Create()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lasermesh-recents-{Guid.NewGuid():N}.json");
        return new(new(), new(new()), new MeshAdapter(), new RecentProjects(path), new(), new(), NullLogger<StudioViewModel>.Instance);
    }

    private static async Task OnDispatcher(Func<Task> work)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(async () =>
            {
                try { await work(); completion.TrySetResult(); }
                catch (Exception ex) { completion.TrySetException(ex); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            });
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA); thread.IsBackground = true; thread.Start();
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }
}
