using System.IO.Compression;
using System.Numerics;
using LaserMesh.Engraving;
using LaserMesh.Geometry;
using LaserMesh.Projects;
using Xunit;

namespace LaserMesh.Tests;

public sealed class SceneEditingTests
{
    private static TriangleMesh Triangle => TriangleMesh.FromTriangles([new(0, 0, 0), new(4, 0, 0), new(0, 4, 0)]);

    [Fact]
    public void SelectionAndHistoryKeepRelativePlacementAndSavedCheckpoint()
    {
        var a = new ProjectModel(Guid.NewGuid(), "A", Triangle, ModelPose.Identity);
        var b = a with { Id = Guid.NewGuid(), Pose = new(10, 0, 0, 0, 1) };
        var selection = new SelectionState(); selection.Replace([a.Id, b.Id], b.Id);
        var initial = new SceneDocument { Models = [a, b] };
        var history = new EditHistory(); history.Reset(initial, selection);
        var moved = initial with { Models = initial.Models.Select(m => m with
            { Pose = m.Pose.Transform(Matrix4x4.CreateTranslation(5, 2, 0)) }).ToArray() };
        history.Push("Mover", moved, selection);
        Assert.True(history.IsDirty);
        var restored = history.Undo(selection);
        Assert.False(history.IsDirty); Assert.Equal(2, selection.Ids.Count); Assert.Equal(b.Id, selection.ActiveId);
        Assert.Equal(10, restored.Models[1].Pose.X);
        Assert.Equal(10, history.Redo(selection).Models[1].Pose.X - moved.Models[0].Pose.X);
        history.MarkSaved(); Assert.False(history.IsDirty);
        history.Undo(selection); history.Push("Otra rama", initial, selection); Assert.False(history.CanRedo);
    }

    [Fact]
    public void LegacyRotationAndFullOrientationComposeWithoutChangingUnits()
    {
        var legacy = new ModelPose(2, 3, 4, 90, 2);
        var point = Vector3.Transform(Vector3.UnitX, legacy.Matrix);
        Assert.True(Vector3.Distance(point, new(2, 5, 4)) < 0.00001);
        var rotated = legacy.Transform(Matrix4x4.CreateRotationX(MathF.PI / 2));
        Assert.True(rotated.IsValid());
        Assert.True(Vector3.Distance(Vector3.Transform(Vector3.UnitX, rotated.Matrix),
            Vector3.Transform(point, Matrix4x4.CreateRotationX(MathF.PI / 2))) < 0.0001);
    }

    [Theory]
    [InlineData(PlacementMode.Silhouette, 1)]
    [InlineData(PlacementMode.Individual, 2)]
    [InlineData(PlacementMode.Rectangle, 1)]
    public void CollectionGuidesIncludeLowerProjectingPiece(PlacementMode mode, int loops)
    {
        var request = new GuideRequest(17,
            [new(Guid.NewGuid(), Triangle, Matrix4x4.CreateTranslation(0, 0, 20)),
             new(Guid.NewGuid(), Triangle, Matrix4x4.CreateTranslation(2, 0, 0))], mode,
            new(ReferenceKind.Inferior));
        var result = PlacementGuide.Create(request);
        Assert.Equal(17, result.Revision); Assert.Equal(loops, result.Guide.Loops.Count);
        Assert.Equal(6, result.Guide.Maximum.X); Assert.Equal(0, result.Z);
    }

    [Fact]
    public void OpenSectionIsDiagnosedAndCoplanarSurfaceRetained()
    {
        var open = TriangleMesh.FromTriangles([new Vector3(0, 0, -1), new(4, 0, 1), new(0, 4, 1)]);
        var request = new GuideRequest(1, [new(Guid.NewGuid(), open, Matrix4x4.Identity)], PlacementMode.Section,
            new(ReferenceKind.Custom), 0);
        Assert.Throws<InvalidDataException>(() => PlacementGuide.Create(request));
        var flat = PlacementGuide.Create(request with { Models = [new(Guid.NewGuid(), Triangle, Matrix4x4.Identity)] });
        Assert.Single(flat.Guide.Loops); Assert.Equal(0, flat.Z);
    }

    [Fact]
    public async Task VersionTwoSceneRoundTripsAssetsGroupsAndOrientation()
    {
        var path = Path.Combine(Path.GetTempPath(), $"scene-{Guid.NewGuid():N}.lmeshproj");
        var id = Guid.NewGuid(); var assetId = Guid.NewGuid(); var groupId = Guid.NewGuid();
        var document = new SceneDocument
        {
            Models = [new(id, "Pieza", Triangle, ModelPose.Identity with { Orientation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.3f) })
                { Locked = true, Visible = false, GroupId = groupId }],
            Groups = [new(groupId, "Grupo")], Assets = [new(assetId, "Imagen", "image/png", [1, 2, 3])],
            Designs = [new(Guid.NewGuid(), assetId, id, new(1, 2, 3), Vector3.UnitZ, 10, 20, 15, 0.5, DesignProjection.Planar)],
            SupportZ = 4, SectionZ = 8
        };
        try
        {
            var store = new ProjectStore(new()); await store.SaveSceneAsync(path, document, default);
            var loaded = await store.OpenSceneAsync(path, default);
            Assert.Equal(document.Models[0].Pose, loaded.Models[0].Pose);
            Assert.False(loaded.Models[0].Visible); Assert.True(loaded.Models[0].Locked);
            Assert.Equal(document.Designs[0].Origin, loaded.Designs[0].Origin);
            Assert.Equal(document.Designs[0].ModelId, loaded.Designs[0].ModelId);
            Assert.Equal(document.Designs[0].Width, loaded.Designs[0].Width);
            Assert.Equal(document.Designs[0].Islands, loaded.Designs[0].Islands);
            Assert.Equal(document.Assets[0].Content, loaded.Assets[0].Content);
            Assert.Equal(4, loaded.SupportZ); Assert.Equal(8, loaded.SectionZ);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task EmptyVersionOneProjectStillOpens()
    {
        var path = Path.Combine(Path.GetTempPath(), $"v1-{Guid.NewGuid():N}.lmeshproj");
        try
        {
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            using (var writer = new StreamWriter(zip.CreateEntry("manifest.json").Open()))
                writer.Write("""{"Version":1,"Unit":"mm","Models":[]}""");
            Assert.Empty((await new ProjectStore(new()).OpenSceneAsync(path, default)).Models);
        }
        finally { File.Delete(path); }
    }
}
