using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace LaserMesh.Geometry;

/// <summary>Per-corner UVs and chart identifiers preserving original triangle order.</summary>
/// <param name="Coordinates">One normalized UV per original triangle corner.</param>
/// <param name="Charts">Chart identity at each corner.</param>
public sealed record UvAtlas(Vector2[] Coordinates, int[] Charts)
{
    /// <summary>Encodes per-corner data into an embedded binary resource.</summary>
    public byte[] Encode()
    {
        if (Coordinates.Length != Charts.Length) throw new InvalidDataException("Atlas inconsistente.");
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(Coordinates.Length);
        for (var i = 0; i < Coordinates.Length; i++) { writer.Write(Coordinates[i].X); writer.Write(Coordinates[i].Y); writer.Write(Charts[i]); }
        return stream.ToArray();
    }
    /// <summary>Decodes a bounded, validated embedded atlas.</summary>
    public static UvAtlas Decode(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes); using var reader = new BinaryReader(stream);
        if (bytes.Length < 4) throw new InvalidDataException("Atlas truncado.");
        var count = reader.ReadInt32();
        if (count <= 0 || count > 3_000_000 || count % 3 != 0 || bytes.Length != 4L + count * 12L)
            throw new InvalidDataException("Longitud de atlas inválida.");
        var uv = new Vector2[count]; var charts = new int[count];
        for (var i = 0; i < count; i++)
        {
            uv[i] = new(reader.ReadSingle(), reader.ReadSingle()); charts[i] = reader.ReadInt32();
            if (!float.IsFinite(uv[i].LengthSquared()) || charts[i] < -1) throw new InvalidDataException("Datos UV inválidos.");
        }
        return new(uv, charts);
    }
}

/// <summary>Generates a cancellable surface atlas without changing source mesh positions.</summary>
public interface IUvUnwrapper
{
    /// <summary>Generates a UV atlas off the caller thread.</summary>
    Task<UvAtlas> UnwrapAsync(TriangleMesh mesh, CancellationToken cancellationToken);
}

/// <summary>Owned xatlas C ABI integration for Windows x64.</summary>
public sealed class XatlasUnwrapper : IUvUnwrapper
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ProgressCallback(int percent);
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeResult { internal IntPtr Uv; internal IntPtr Charts; internal uint Count; }
    private sealed class AtlasHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        internal AtlasHandle(IntPtr pointer) : base(true) => SetHandle(pointer);
        protected override bool ReleaseHandle() { Free(handle); return true; }
    }
    [DllImport("LaserMesh.Uv.Native", EntryPoint = "lm_uv_generate", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr Generate(float[] positions, uint count, ProgressCallback progress);
    [DllImport("LaserMesh.Uv.Native", EntryPoint = "lm_uv_free", CallingConvention = CallingConvention.Cdecl)]
    private static extern void Free(IntPtr result);

    /// <inheritdoc/>
    public Task<UvAtlas> UnwrapAsync(TriangleMesh mesh, CancellationToken cancellationToken)
        => Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = mesh.Positions.ToArray();
            var positions = new float[source.Length * 3];
            for (var i = 0; i < source.Length; i++)
            { positions[i * 3] = source[i].X; positions[i * 3 + 1] = source[i].Y; positions[i * 3 + 2] = source[i].Z; }
            ProgressCallback progress = _ => cancellationToken.IsCancellationRequested ? 0 : 1;
            using var owned = new AtlasHandle(Generate(positions, (uint)source.Length, progress));
            GC.KeepAlive(progress);
            cancellationToken.ThrowIfCancellationRequested();
            if (owned.IsInvalid) throw new InvalidDataException("xatlas no pudo desplegar esta malla.");
            var result = Marshal.PtrToStructure<NativeResult>(owned.DangerousGetHandle());
            if (result.Count != source.Length) throw new InvalidDataException("Correspondencia UV inválida.");
            var uv = new float[source.Length * 2]; var charts = new int[source.Length];
            Marshal.Copy(result.Uv, uv, 0, uv.Length); Marshal.Copy(result.Charts, charts, 0, charts.Length);
            var coordinates = Enumerable.Range(0, source.Length).Select(i => new Vector2(uv[i * 2], uv[i * 2 + 1])).ToArray();
            if (coordinates.Any(p => !float.IsFinite(p.X) || !float.IsFinite(p.Y))) throw new InvalidDataException("UV no finitas.");
            return new UvAtlas(coordinates, charts);
        }, cancellationToken);
}
