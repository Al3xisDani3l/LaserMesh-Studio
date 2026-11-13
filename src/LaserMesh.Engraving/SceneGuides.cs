using System.Numerics;
using Clipper2Lib;
using LaserMesh.Geometry;

namespace LaserMesh.Engraving;

/// <summary>Reference plane policy, independent of outline geometry.</summary>
public enum ReferenceKind
{
    /// <summary>Highest transformed vertex.</summary>
    Superior,
    /// <summary>Lowest transformed vertex.</summary>
    Inferior,
    /// <summary>Configured fixture support.</summary>
    Support,
    /// <summary>Explicit horizontal height.</summary>
    Custom,
    /// <summary>Picked object-local face.</summary>
    Face
}

/// <summary>Plane used to present a guide in scene coordinates.</summary>
/// <param name="Kind">Reference policy.</param>
/// <param name="Z">Support or custom scene height.</param>
public sealed record SurfaceReference(ReferenceKind Kind, double Z = 0)
{
    /// <summary>Picked face origin in scene coordinates.</summary>
    public Vector3 Origin { get; init; }
    /// <summary>Picked face normal in scene coordinates.</summary>
    public Vector3 Normal { get; init; } = Vector3.UnitZ;
}

/// <summary>Independent geometry instance supplied to guide preparation.</summary>
/// <param name="Id">Scene identity.</param>
/// <param name="Mesh">Object-local triangle mesh.</param>
/// <param name="Transform">Object-to-scene matrix.</param>
public sealed record GuideModel(Guid Id, TriangleMesh Mesh, Matrix4x4 Transform);

/// <summary>Snapshot of a guide computation.</summary>
/// <param name="Revision">Scene revision for rejecting outdated results.</param>
/// <param name="Models">Explicit scope of instances.</param>
/// <param name="Mode">Geometry operation.</param>
/// <param name="Reference">Independent presentation reference.</param>
/// <param name="SectionZ">Horizontal cut height.</param>
public sealed record GuideRequest(long Revision, IReadOnlyList<GuideModel> Models, PlacementMode Mode,
    SurfaceReference Reference, double SectionZ = 0);

/// <summary>Prepared outline and the revision/height it represents.</summary>
/// <param name="Revision">Input revision.</param>
/// <param name="Guide">Shared export, rendering and device geometry.</param>
/// <param name="Z">Presentation height.</param>
/// <param name="ModelCount">Number of contributing instances.</param>
/// <param name="Reference">Reference plane used for presentation.</param>
public sealed record GuideResult(long Revision, PlacementGuide Guide, double Z, int ModelCount, SurfaceReference Reference)
{
    /// <summary>Places an XY guide point on the requested presentation plane.</summary>
    public Vector3 Present(Vector2 point)
    {
        var height = Reference.Kind == ReferenceKind.Face
            ? Reference.Origin.Z - ((point.X - Reference.Origin.X) * Reference.Normal.X
                + (point.Y - Reference.Origin.Y) * Reference.Normal.Y) / Reference.Normal.Z : (float)Z;
        return new(point, height);
    }
}

public sealed partial class PlacementGuide
{
    /// <summary>Prepares union, independent contours, bounds or a true section for a collection.</summary>
    public static GuideResult Create(GuideRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Models.Count == 0 || !Enum.IsDefined(request.Mode) || !double.IsFinite(request.SectionZ)
            || !double.IsFinite(request.Reference.Z) || !Enum.IsDefined(request.Reference.Kind)
            || request.Reference.Kind == ReferenceKind.Face && (!float.IsFinite(request.Reference.Normal.LengthSquared())
                || Math.Abs(request.Reference.Normal.Z) < 0.1f || !float.IsFinite(request.Reference.Origin.LengthSquared())))
            throw new InvalidDataException("Selecciona piezas y una referencia válida.");
        var all = new Paths64();
        var low = float.PositiveInfinity;
        var high = float.NegativeInfinity;
        foreach (var model in request.Models)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var positions = model.Mesh.Positions.ToArray();
            for (var i = 0; i < positions.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                positions[i] = Vector3.Transform(positions[i], model.Transform);
                low = Math.Min(low, positions[i].Z); high = Math.Max(high, positions[i].Z);
            }
            if (request.Mode == PlacementMode.Section)
                all.AddRange(Section(positions, (float)request.SectionZ, cancellationToken));
            else
            {
                var mesh = TriangleMesh.FromTriangles(positions, cancellationToken);
                var guide = Create(mesh, Matrix3x2.Identity, PlacementMode.Silhouette, cancellationToken);
                foreach (var loop in guide.Loops) all.Add(new Path64(loop.Select(Quantize)));
            }
        }
        if (all.Count == 0) throw new InvalidDataException("Sin sección cerrada en este plano.");
        if (request.Mode == PlacementMode.Rectangle)
        {
            var points = all.SelectMany(p => p).ToArray();
            var minX = points.Min(p => p.X); var maxX = points.Max(p => p.X);
            var minY = points.Min(p => p.Y); var maxY = points.Max(p => p.Y);
            all = [new Path64 { new(minX, minY), new(maxX, minY), new(maxX, maxY), new(minX, maxY) }];
        }
        else if (request.Mode != PlacementMode.Individual)
        {
            var tree = new PolyTree64();
            var clipper = new Clipper64(); clipper.AddSubject(all);
            if (!clipper.Execute(ClipType.Union, FillRule.NonZero, tree))
                throw new InvalidDataException("No se pudo preparar la unión del conjunto.");
            all = [];
            for (var i = 0; i < tree.Count; i++) if (tree[i].Polygon is { Count: >= 3 } p) all.Add(p);
        }
        if (all.Count == 0 || all.Sum(p => p.Count + 1L) > MaxSegments)
            throw new InvalidDataException("Guía vacía o demasiado compleja.");
        var z = request.Mode == PlacementMode.Section ? request.SectionZ : request.Reference.Kind switch
        {
            ReferenceKind.Superior => high,
            ReferenceKind.Inferior => low,
            _ => request.Reference.Z
        };
        cancellationToken.ThrowIfCancellationRequested();
        return new(request.Revision, new PlacementGuide(all), z, request.Models.Count,
            request.Mode == PlacementMode.Section ? new SurfaceReference(ReferenceKind.Custom, request.SectionZ) : request.Reference);
    }

    private static Point64 Quantize(Vector2 p)
    {
        if (!float.IsFinite(p.X) || !float.IsFinite(p.Y) || Math.Abs(p.X) > MaxCoordinate || Math.Abs(p.Y) > MaxCoordinate)
            throw new InvalidDataException("Coordenadas de guía fuera de rango.");
        return new((long)Math.Round(p.X * UnitsPerMillimetre), (long)Math.Round(p.Y * UnitsPerMillimetre));
    }

    private static Paths64 Section(Vector3[] positions, float z, CancellationToken token)
    {
        const float tolerance = 0.00001f;
        var result = new Paths64();
        var edges = new HashSet<((long X, long Y) A, (long X, long Y) B)>();
        for (var i = 0; i < positions.Length; i += 3)
        {
            token.ThrowIfCancellationRequested();
            var triangle = positions.AsSpan(i, 3);
            if (Math.Abs(triangle[0].Z - z) <= tolerance && Math.Abs(triangle[1].Z - z) <= tolerance
                && Math.Abs(triangle[2].Z - z) <= tolerance)
            {
                var path = new Path64(); foreach (var p in triangle) path.Add(Quantize(new(p.X, p.Y)));
                if (Clipper.Area(path) < 0) path.Reverse();
                if (Math.Abs(Clipper.Area(path)) > 0) result.Add(path);
                continue;
            }
            var hits = new HashSet<(long X, long Y)>();
            for (var j = 0; j < 3; j++)
            {
                var a = triangle[j]; var b = triangle[(j + 1) % 3];
                if (Math.Abs(a.Z - z) <= tolerance) Add(a);
                if ((a.Z < z - tolerance && b.Z > z + tolerance) || (b.Z < z - tolerance && a.Z > z + tolerance))
                    Add(Vector3.Lerp(a, b, (z - a.Z) / (b.Z - a.Z)));
            }
            if (hits.Count == 2)
            {
                var pair = hits.OrderBy(p => p.X).ThenBy(p => p.Y).ToArray();
                edges.Add((pair[0], pair[1]));
            }
            void Add(Vector3 p) { var q = Quantize(new(p.X, p.Y)); hits.Add((q.X, q.Y)); }
        }
        var adjacent = new Dictionary<(long X, long Y), List<(long X, long Y)>>();
        foreach (var (a, b) in edges)
        {
            if (!adjacent.TryGetValue(a, out var aa)) adjacent[a] = aa = [];
            if (!adjacent.TryGetValue(b, out var bb)) adjacent[b] = bb = [];
            aa.Add(b); bb.Add(a);
        }
        if (adjacent.Values.Any(v => v.Count != 2))
            throw new InvalidDataException("La sección contiene extremos abiertos o ramificados; no se inventaron cierres.");
        var remaining = adjacent.Keys.ToHashSet();
        while (remaining.Count > 0)
        {
            token.ThrowIfCancellationRequested();
            var start = remaining.First(); var at = start; var previous = (long.MinValue, long.MinValue);
            var path = new Path64();
            do
            {
                if (!remaining.Remove(at)) throw new InvalidDataException("Sección no manifold.");
                path.Add(new Point64(at.X, at.Y));
                var next = adjacent[at].First(p => p != previous); previous = at; at = next;
            } while (at != start);
            if (Clipper.Area(path) < 0) path.Reverse();
            if (path.Count >= 3 && Math.Abs(Clipper.Area(path)) > 0) result.Add(path);
        }
        return result;
    }
}
