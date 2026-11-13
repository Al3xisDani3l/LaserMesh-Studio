using System.Numerics;
using System.Text.Json;
using LaserMesh.Calibration;
using LaserMesh.Geometry;
using LaserMesh.Hardware.LaserPecker;
using LaserMesh.Imaging;
using LaserMesh.Projects;
using Xunit;

namespace LaserMesh.Tests;

public sealed class ImagingUvCalibrationTests
{
    [Fact]
    public void DitheringAndPackingMatchPyroGraphByteForByte()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "raster-vectors.json")));
        foreach (var item in json.RootElement.EnumerateArray())
        {
            var gray = Bytes("gray");
            foreach (var packed in new[] { false, true })
            {
                var result = RasterProcessor.Prepare(gray, item.GetProperty("width").GetInt32(), item.GetProperty("height").GetInt32(), packed,
                    item.GetProperty("brightness").GetDouble(), item.GetProperty("contrast").GetDouble(), item.GetProperty("inverse").GetBoolean());
                Assert.Equal(Bytes(packed ? "packed" : "mono"), result.Payload);
            }
            byte[] Bytes(string name) => item.GetProperty(name).EnumerateArray().Select(x => x.GetByte()).ToArray();
        }
    }

    [Fact]
    public async Task NativeAtlasPreservesCornersAndRoundTripsItsBinaryResource()
    {
        var mesh = TriangleMesh.FromTriangles([new(0, 0, 0), new(10, 0, 0), new(0, 10, 0),
            new(10, 0, 0), new(10, 10, 0), new(0, 10, 0)]);
        var original = mesh.Positions.ToArray();
        var atlas = await new XatlasUnwrapper().UnwrapAsync(mesh, default);
        Assert.Equal(original.Length, atlas.Coordinates.Length); Assert.Equal(original, mesh.Positions.ToArray());
        Assert.All(atlas.Coordinates, uv => { Assert.InRange(uv.X, 0, 1); Assert.InRange(uv.Y, 0, 1); });
        var restored = UvAtlas.Decode(atlas.Encode()); Assert.Equal(atlas.Coordinates, restored.Coordinates); Assert.Equal(atlas.Charts, restored.Charts);
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new XatlasUnwrapper().UnwrapAsync(mesh, cancel.Token));
    }

    [Fact]
    public void MeasuredAffineCalibrationRejectsDegeneracyAndRecoversRegistration()
    {
        Vector2[] scene = [new(0, 0), new(10, 0), new(0, 10), new(10, 10)];
        var expected = Matrix3x2.CreateRotation(0.3f) * Matrix3x2.CreateScale(1.02f) * Matrix3x2.CreateTranslation(4, 7);
        var measured = scene.Select(p => Vector2.Transform(p, expected)).ToArray();
        var (matrix, rms) = AffineCalibration.Fit(scene, measured);
        Assert.InRange(rms, 0, 0.0001);
        for (var i = 0; i < scene.Length; i++) Assert.True(Vector2.Distance(Vector2.Transform(scene[i], matrix), measured[i]) < 0.0001);
        Assert.Throws<ArgumentException>(() => AffineCalibration.Fit([new(0, 0), new(1, 1), new(2, 2)], [new(0, 0), new(1, 1), new(2, 2)]));
    }

    [Fact]
    public async Task RasterJobUsesExplicitParametersAndBlocksUnvalidatedPhysicalConnection()
    {
        var profile = new EngravingProfile("316", AccessoryMode.Fixed, "test", "");
        var job = EngravingJob.Create(RasterProcessor.Prepare([0, 255, 255, 0], 2, 2), 49, 49, 4, new(1, 1, 1), profile);
        Assert.Equal(68, job.File.Length); Assert.Equal(0x10, job.File.Span[0]);
        Assert.True(LaserPeckerProtocol.TryDecode(job.Start.Span, out var start));
        Assert.Equal<byte?>(1, start.Function); Assert.Equal(1, start.Payload.Span[1]); Assert.Equal(100, start.Payload.Span[2]);
        Assert.Equal(job.File.Span.Slice(5, 4).ToArray(), start.Payload.Span.Slice(3, 4).ToArray());
        await Assert.ThrowsAsync<InvalidOperationException>(() => new RasterEngravingSession().RunAsync(
            () => throw new Exception("No physical connection allowed"), job, null, false, new Progress<string>(), default));
        Assert.Throws<ArgumentOutOfRangeException>(() => EngravingJob.Create(new(1, 1, [0], false), 0, 0, 4, new(0, 1, 1), profile));
        Assert.Throws<NotSupportedException>(() => EngravingJob.Create(new(1, 1, [0], false), 0, 0, 4, new(1, 1, 1), profile with { Mode = AccessoryMode.Cylinder }));
    }

    [Theory]
    [InlineData(AccessoryMode.Cylinder)]
    [InlineData(AccessoryMode.Slab)]
    [InlineData(AccessoryMode.Trolley)]
    public void AccessorySimulationUsesMeasuredDirectionAndTravel(AccessoryMode mode)
    {
        var calibration = new AccessoryCalibration(mode, 100, 98, 40, -1);
        Assert.Equal(-100, calibration.CommandFor(98), 6);
        if (mode == AccessoryMode.Cylinder) Assert.Equal(-100 / (Math.PI * 40) * 360, calibration.AngleFor(98), 6);
        else Assert.Throws<InvalidOperationException>(() => calibration.AngleFor(98));
    }

    [Fact]
    public async Task RasterSessionCompletesAndRejectsTransferWithoutPhysicalPorts()
    {
        var profile = new EngravingProfile("316", AccessoryMode.Fixed, "fixture", "");
        var job = EngravingJob.Create(new(2, 2, [0, 255, 255, 0], false), 49, 49, 4, new(1, 1, 1), profile);
        var messages = new List<string>();
        await new RasterEngravingSession().RunAsync(() => new SimulatedRasterConnection(), job, null, true,
            new InlineProgress<string>(messages.Add), default);
        Assert.Contains(messages, m => m.Contains("terminado", StringComparison.Ordinal));
        var session = new RasterEngravingSession();
        await Assert.ThrowsAsync<IOException>(() => session.RunAsync(() => new SimulatedRasterConnection { RejectTransfer = true },
            job, null, true, new InlineProgress<string>(_ => { }), default));
        Assert.False(session.UnknownState);
    }

    [Fact]
    public async Task CancellationStopsSimulatedRasterAndDisconnectionMarksUnknown()
    {
        var job = EngravingJob.Create(new(1, 1, [0], false), 50, 50, 4, new(1, 1, 1), new("316", AccessoryMode.Fixed, "", ""));
        using var cancel = new CancellationTokenSource();
        var session = new RasterEngravingSession();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.RunAsync(() => new SimulatedRasterConnection(), job, null, true,
            new InlineProgress<string>(s => { if (s.Contains("grabando", StringComparison.Ordinal)) cancel.Cancel(); }), cancel.Token));
        Assert.False(session.UnknownState);
        var disconnected = new SimulatedRasterConnection();
        session = new RasterEngravingSession();
        await Assert.ThrowsAsync<IOException>(() => session.RunAsync(() => disconnected, job, null, true,
            new InlineProgress<string>(s => { if (s.Contains("grabando", StringComparison.Ordinal)) disconnected.Disconnect(); }), default));
        Assert.True(session.UnknownState);
    }

    [Theory]
    [InlineData(AccessoryMode.Cylinder)]
    [InlineData(AccessoryMode.Slab)]
    [InlineData(AccessoryMode.Trolley)]
    public async Task AccessorySessionCanBeCancelledWithoutAnyTransport(AccessoryMode mode)
    {
        using var cancel = new CancellationTokenSource(); var messages = new List<AccessorySimulationState>();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => AccessorySimulator.RunAsync(new(mode, 10, 9.8, 30, 1), 15,
            new InlineProgress<AccessorySimulationState>(state => { messages.Add(state); cancel.Cancel(); }), cancel.Token));
        Assert.NotEmpty(messages); Assert.False(messages[^1].Finished);
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    { public void Report(T value) => report(value); }

    [Fact]
    public void CompositorResolvesHigherSurfaceOcclusionBeforeSamplingImages()
    {
        var triangle = TriangleMesh.FromTriangles([new(0, 0, 0), new(4, 0, 0), new(0, 4, 0)]);
        var lower = new ProjectModel(Guid.NewGuid(), "lower", triangle, ModelPose.Identity);
        var upper = lower with { Id = Guid.NewGuid(), Pose = new(0, 0, 3, 0, 1) };
        var asset = Guid.NewGuid();
        var design = new SurfaceDesign(Guid.NewGuid(), asset, lower.Id, Vector3.Zero, Vector3.UnitZ, 20, 20, 0, 1, DesignProjection.Planar);
        var image = new ImagePixels(1, 1, [0, 0, 0, 255]);
        var doc = new SceneDocument { Models = [lower], Designs = [design] };
        var visible = SurfaceCompositor.Compose(doc, new Dictionary<Guid, ImagePixels> { [asset] = image });
        Assert.Contains((byte)0, visible.Gray);
        var hidden = SurfaceCompositor.Compose(doc with { Models = [lower, upper] }, new Dictionary<Guid, ImagePixels> { [asset] = image });
        Assert.All(hidden.Gray, v => Assert.Equal(255, v));
    }
}
