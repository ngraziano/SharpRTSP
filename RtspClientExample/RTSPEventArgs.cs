using System;
using System.Collections.Generic;

#pragma warning disable MA0048

namespace RtspClientExample
{
    public class SpsPpsEventArgs : EventArgs
    {
        public SpsPpsEventArgs(byte[] sps, byte[] pps)
        {
            Sps = sps;
            Pps = pps;
        }

        public byte[] Sps { get; }
        public byte[] Pps { get; }
    }

    public class VpsSpsPpsEventArgs : EventArgs
    {
        public VpsSpsPpsEventArgs(byte[] vps, byte[] sps, byte[] pps)
        {
            Vps = vps;
            Sps = sps;
            Pps = pps;
        }

        public byte[] Vps { get; }
        public byte[] Sps { get; }
        public byte[] Pps { get; }
    }

    public class SimpleDataEventArgs : EventArgs
    {
        public SimpleDataEventArgs(IEnumerable<ReadOnlyMemory<byte>> data, DateTime timeStamp)
        {
            Data = data;
            TimeStamp = timeStamp;
        }

        public DateTime TimeStamp { get; }
        public IEnumerable<ReadOnlyMemory<byte>> Data { get; }
    }

    public class G711EventArgs : EventArgs
    {
        public G711EventArgs(string format, IEnumerable<ReadOnlyMemory<byte>> data, DateTime timeStamp)
        {
            Format = format;
            Data = data;
            TimeStamp = timeStamp;
        }

        public DateTime TimeStamp { get; }
        public string Format { get; }
        public IEnumerable<ReadOnlyMemory<byte>> Data { get; }
    }

    public class AMREventArgs : EventArgs
    {
        public AMREventArgs(string format, IEnumerable<ReadOnlyMemory<byte>> data, DateTime timeStamp)
        {
            Format = format;
            Data = data;
            TimeStamp = timeStamp;
        }

        public DateTime TimeStamp { get; }
        public string Format { get; }
        public IEnumerable<ReadOnlyMemory<byte>> Data { get; }
    }

    public class AACEventArgs : EventArgs
    {
        public AACEventArgs(string format, IEnumerable<ReadOnlyMemory<byte>> data, int objectType, int frequencyIndex, int channelConfiguration, DateTime timeStamp)
        {
            Format = format;
            Data = data;
            ObjectType = objectType;
            FrequencyIndex = frequencyIndex;
            ChannelConfiguration = channelConfiguration;
            TimeStamp = timeStamp;
        }
        public DateTime TimeStamp { get; }
        public string Format { get; }
        public IEnumerable<ReadOnlyMemory<byte>> Data { get; }
        public int ObjectType { get; }
        public int FrequencyIndex { get; }
        public int ChannelConfiguration { get; }
    }
}
