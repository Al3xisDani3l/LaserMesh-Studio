namespace LaserMesh.Hardware.LaserPecker;

/// <summary>Raster-only test device that never opens a serial port or emits light.</summary>
public sealed class SimulatedRasterConnection : IPreviewConnection
{
    private readonly Queue<LaserPeckerFrame> replies = new();
    private int receiveStage;
    private int engravingPolls;
    private bool connected = true;
    /// <summary>When set, the next upload announcement is rejected.</summary>
    public bool RejectTransfer { get; set; }
    /// <summary>Simulates disappearance of the connection.</summary>
    public void Disconnect() => connected = false;
    /// <inheritdoc/>
    public void Send(byte[] bytes)
    {
        if (!connected) throw new IOException("USB simulado desconectado.");
        if (receiveStage > 0)
        {
            if (receiveStage++ == 2) { receiveStage = 0; Reply(5, [2, 1, 0]); }
            return;
        }
        if (!LaserPeckerProtocol.TryDecode(bytes, out var frame)) throw new InvalidDataException("Frame simulado inválido.");
        if (frame.Function == 5)
        { Reply(5, [1, RejectTransfer ? (byte)0 : (byte)1, 0]); if (!RejectTransfer) receiveStage = 1; }
        else if (frame.Function == 1) { engravingPolls = 4; Reply(1, [1, 0, 0, 0, 0]); }
        else if (frame.Function == 0xFF) engravingPolls = 0;
        else if (frame.Function == 0)
        {
            if (frame.Payload.Span[0] == 3) { Reply(0, [1, 60, 0, 0, 0, 0]); return; }
            var status = new byte[21]; status[0] = engravingPolls > 0 ? (byte)1 : (byte)6;
            status[1] = engravingPolls > 0 ? (byte)1 : (byte)255; status[20] = 2;
            if (engravingPolls > 0) engravingPolls--; Reply(0, status);
        }
    }
    /// <inheritdoc/>
    public LaserPeckerFrame Receive(Func<LaserPeckerFrame, bool> matches, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!connected) throw new IOException("USB simulado desconectado.");
        while (replies.TryDequeue(out var frame)) if (matches(frame)) return frame;
        throw new TimeoutException("No hay respuesta simulada compatible.");
    }
    private void Reply(byte function, byte[] payload)
    {
        if (LaserPeckerProtocol.TryDecode(LaserPeckerProtocol.Encode(function, payload), out var frame)) replies.Enqueue(frame);
    }
    /// <inheritdoc/>
    public void Dispose() => connected = false;
}
