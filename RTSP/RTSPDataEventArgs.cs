using System;
using System.Collections.Generic;

namespace Rtsp;

/// <summary>
/// Event args containing information for message events.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="RTSPDataEventArgs"/> class.
/// </remarks>
/// <param name="data">A data.</param>
public class RtspDataEventArgs(byte[] data, int length) : EventArgs
{

    /// <summary>
    /// Gets or sets the message.
    /// </summary>
    /// <value>The message.</value>
    public byte[] Data { get; set; } = data;
    public int DataLength { get; set; } = length;
}

/// <summary>
/// H264 SPS - PPS received values...
/// </summary>
/// <param name="sps"></param>
/// <param name="pps"></param>
public class SpsPpsEventArgs(byte[] sps, byte[] pps) : EventArgs
{
    public byte[] Sps { get; } = sps;
    public byte[] Pps { get; } = pps;
}
/// <summary>
/// H265 VPS - SPS - PPS received values...
/// </summary>
/// <param name="vps"></param>
/// <param name="sps"></param>
/// <param name="pps"></param>
public class VpsSpsPpsEventArgs(byte[] vps, byte[] sps, byte[] pps) : EventArgs
{
    public byte[] Vps { get; } = vps;
    public byte[] Sps { get; } = sps;
    public byte[] Pps { get; } = pps;
}
/// <summary>
/// byte array data...
/// </summary>
/// <param name="data"></param>
public class SimpleDataEventArgs(List<ReadOnlyMemory<byte>> data) : EventArgs
{
    public List<ReadOnlyMemory<byte>> Data { get; } = data;
}
//public delegate void ReceivedSimpleDataDelegate(List<ReadOnlyMemory<byte>> data);
public class G711EventArgs(string format, List<ReadOnlyMemory<byte>> data) : EventArgs
{
    public string Format { get; } = format;
    public List<ReadOnlyMemory<byte>> Data { get; } = data;
}
//public delegate void Received_G711_Delegate(string format, List<ReadOnlyMemory<byte>> g711);
public class AMREventArgs(string format, List<ReadOnlyMemory<byte>> data) : EventArgs
{
    public string Format { get; } = format;
    public List<ReadOnlyMemory<byte>> Data { get; } = data;
}
//public delegate void Received_AMR_Delegate(string format, List<ReadOnlyMemory<byte>> amr);
public class AACEventArgs(string format, List<ReadOnlyMemory<byte>> data, uint objectType, uint frequencyIndex, uint channelConfiguration) : EventArgs
{
    public string Format { get; } = format;
    public List<ReadOnlyMemory<byte>> Data { get; } = data;
    public uint ObjectType { get; } = objectType;
    public uint FrequencyIndex { get; } = frequencyIndex;
    public uint ChannelConfiguration { get; } = channelConfiguration;
}
//public delegate void Received_AAC_Delegate(string format, List<ReadOnlyMemory<byte>> aac, uint ObjectType, uint FrequencyIndex, uint ChannelConfiguration);
