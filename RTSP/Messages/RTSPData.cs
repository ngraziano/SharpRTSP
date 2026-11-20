namespace Rtsp.Messages;

using System;
using System.Buffers;
using System.Text;

/// <summary>
/// Message which represent data. ($ limited message)
/// </summary>
public sealed class RtspData : RtspChunk, IDisposable
{
    private IMemoryOwner<byte>? _reservedData;
    // or an array of ArrayPool<byte>.Shared: no owner object of each packet
    private byte[]? _array;
    private bool _disposedValue;

    public RtspData() { }

    public RtspData(IMemoryOwner<byte> reservedData, int size)
    {
        _reservedData = reservedData;
        base.Data = reservedData.Memory[..size];
    }

    internal RtspData(byte[] array, int size)
    {
        _array = array;
        base.Data = array.AsMemory(0, size);
    }

    public override Memory<byte> Data
    {
        get => base.Data;
        set
        {
            _reservedData?.Dispose();
            _reservedData = null;
            if (_array != null) ArrayPool<byte>.Shared.Return(_array);
            _array = null;
            base.Data = value;
        }
    }

    /// <summary>
    /// Create a string of the message for debug.
    /// </summary>
    public override string ToString()
    {
        var stringBuilder = new StringBuilder();
        stringBuilder.AppendLine("Data message");
        stringBuilder.AppendLine(Data.IsEmpty ? "Data : null" : $"Data length :-{Data.Length}-");

        return stringBuilder.ToString();
    }

    public int Channel { get; set; }

    /// <summary>
    /// Clones this instance.
    /// <remarks>Listener is not cloned</remarks>
    /// </summary>
    /// <returns>a clone of this instance</returns>
    public override object Clone() => new RtspData
    {
        Channel = Channel,
        SourcePort = SourcePort,
        Data = Data,
    };

    private void Dispose(bool disposing)
    {
        if (_disposedValue) return;
        if (disposing)
        {
            _reservedData?.Dispose();
            _reservedData = null;
        }
        Data = Memory<byte>.Empty;
        _disposedValue = true;
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}