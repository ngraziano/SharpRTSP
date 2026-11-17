namespace Rtsp.Messages;

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;

/// <summary>
/// Message which represent data. ($ limited message)
/// </summary>
public sealed class RtspData : RtspChunk, IDisposable
{
    private IMemoryOwner<byte>? _reservedData;
    private bool _disposedValue;

    // the messages of the listener's own, kept once disposed to be filled again
    private bool _pooled;
    private static readonly Stack<RtspData> Idle = new();
    private const int MaxIdle = 64;

    /// <summary>
    /// A message of the data of <paramref name="reservedData"/>, of one disposed before where there is one: disposed, it
    /// is kept to be filled again; one never disposed is collected as any other.
    /// </summary>
    internal static RtspData Rent(IMemoryOwner<byte> reservedData, int size, int channel)
    {
        RtspData? data = null;
        lock (Idle)
        {
            if (Idle.Count > 0) data = Idle.Pop();
        }
        data ??= new RtspData { _pooled = true };
        data._reservedData = reservedData;
        data.SetData(reservedData.Memory[..size]);
        data.Channel = channel;
        data.SourcePort = null;
        data._disposedValue = false;
        return data;
    }

    // the data without the setter's release of the buffer
    private void SetData(Memory<byte> data) => base.Data = data;

    public RtspData() { }

    public RtspData(IMemoryOwner<byte> reservedData, int size)
    {
        _reservedData = reservedData;
        base.Data = reservedData.Memory[..size];
    }

    public override Memory<byte> Data
    {
        get => base.Data;
        set
        {
            _reservedData?.Dispose();
            _reservedData = null;
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
        }
        // released once: the setter would release it again, which a recycled buffer must not be
        _reservedData = null;
        Data = Memory<byte>.Empty;
        _disposedValue = true;
        if (_pooled)
        {
            SourcePort = null;
            lock (Idle)
            {
                if (Idle.Count < MaxIdle) Idle.Push(this);
            }
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}