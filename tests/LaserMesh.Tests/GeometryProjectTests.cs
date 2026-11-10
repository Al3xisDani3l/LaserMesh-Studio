using System.IO.Compression;
using System.Numerics;
using System.Text;
using LaserMesh.Geometry;
using LaserMesh.Projects;
using Xunit;

namespace LaserMesh.Tests;

public sealed class GeometryProjectTests
{
    private const string Ascii = """
        solid sample
          facet normal 0 0 1
            outer loop
              vertex 0 0 0
              vertex 2 0 0
              vertex 0 3 0
            endloop
          endfacet
        endsolid sample
        """;

    private static readonly StlCodec Codec = new();

    [Fact]
    public void AsciiImportAppliesUnitsAndComputesGeometry()
    {
        var mesh = ReadAscii(Ascii, 25.4f);
        Assert.Equal(new Vector3(50.8f, 76.2f, 0), mesh.Dimensions);
        Assert.Equal(Vector3.UnitZ, mesh.FaceNormals[0]);
        Assert.Equal(3, mesh.VertexCount);
        Assert.Equal(1, mesh.TriangleCount);
        Assert.Equal(mesh.Dimensions / 3, mesh.Centroid);
    }

    [Fact]
    public void BinaryImportWinsEvenIfItsHeaderStartsWithSolid()
    {
        using var binary = new MemoryStream();
        Codec.WriteBinary(binary, ReadAscii(Ascii), default);
        binary.Position = 0;
        binary.Write("solid ambiguous"u8);
        binary.Position = 0;
        var mesh = Codec.Read(binary, binary.Length, 10, default);
        Assert.Equal(new Vector3(20, 30, 0), mesh.Dimensions);
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("2e10")]
    public void InvalidCoordinatesAreRejected(string coordinate)
        => Assert.Throws<InvalidDataException>(() => ReadAscii(Ascii.Replace("vertex 2", $"vertex {coordinate}")));

    [Fact]
    public void TruncatedAsciiIsRejected()
        => Assert.Throws<InvalidDataException>(() => ReadAscii(Ascii[..Ascii.IndexOf("endfacet", StringComparison.Ordinal)]));

    [Fact]
    public async Task ProjectRoundTripsEmbeddedMeshesAndTransformAndCanReplaceItself()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.lmeshproj");
        try
        {
            var store = new ProjectStore(Codec);
            var item = new ProjectModel(Guid.NewGuid(), "Muestra", ReadAscii(Ascii), new(1, -2, 3, 45, 2));
            await store.SaveAsync(path, [item], TestContextToken);
            var restored = Assert.Single(await store.OpenAsync(path, TestContextToken));
            Assert.Equal(item.Id, restored.Id);
            Assert.Equal(item.Name, restored.Name);
            Assert.Equal(item.Pose, restored.Pose);
            Assert.Equal(item.Mesh.Positions.ToArray(), restored.Mesh.Positions.ToArray());

            var changed = item with { Name = "Renombrado" };
            await store.SaveAsync(path, [changed], TestContextToken);
            Assert.Equal(changed.Name, Assert.Single(await store.OpenAsync(path, TestContextToken)).Name);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task FailedSavePreservesPreviousProject()
    {
        var directory = Directory.CreateTempSubdirectory();
        var path = Path.Combine(directory.FullName, "sample.lmeshproj");
        try
        {
            var store = new ProjectStore(Codec);
            var model = new ProjectModel(Guid.NewGuid(), "Muestra", ReadAscii(Ascii), ModelPose.Identity);
            await store.SaveAsync(path, [model], TestContextToken);
            var before = await File.ReadAllBytesAsync(path, TestContextToken);
            await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(path, [model, model], TestContextToken));
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveAsync(path, [], cancelled.Token));
            Assert.Equal(before, await File.ReadAllBytesAsync(path, TestContextToken));
            Assert.Single(directory.GetFiles());
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task FutureProjectVersionIsRejected()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.lmeshproj");
        try
        {
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            using (var writer = new StreamWriter(zip.CreateEntry("manifest.json").Open()))
                writer.Write("""{"Version":2,"Unit":"mm","Models":[]}""");
            await Assert.ThrowsAsync<InvalidDataException>(() => new ProjectStore(Codec).OpenAsync(path, TestContextToken));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static CancellationToken TestContextToken => CancellationToken.None;

    private static TriangleMesh ReadAscii(string content, float factor = 1)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        return Codec.Read(stream, stream.Length, factor, default);
    }
}
