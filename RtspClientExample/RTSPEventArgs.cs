using System;
using System.Collections.Generic;

namespace RtspClientExample
{
    public class NewStreamEventArgs : EventArgs
    {
        public NewStreamEventArgs(string streamType, IStreamConfigurationData? streamConfigurationData)
        {
            StreamType = streamType;
            StreamConfigurationData = streamConfigurationData;
        }

        public string StreamType { get; }
        public IStreamConfigurationData? StreamConfigurationData { get; }
    }

    public interface IStreamConfigurationData;

    public record H264StreamConfigurationData : IStreamConfigurationData
    {
        public required List<byte[]> OutOfBandNal { get; init; }
    }

    public record H265StreamConfigurationData : IStreamConfigurationData
    {
        public required List<byte[]> OutOfBandNal { get; init; }
    }

    public record AacStreamConfigurationData : IStreamConfigurationData
    {
        public int ObjectType { get; init; }
        public int FrequencyIndex { get; init; }
        public int SamplingFrequency { get; init; }
        public int ChannelConfiguration { get; init; }
    }

    public class SimpleDataEventArgs(List<ReadOnlyMemory<byte>> data, DateTime clockTimeStamp, ulong rtpTimeStamp, int baseClock, int payloadType) : EventArgs
    {

        public int PayloadType { get; } = payloadType;
        public int BaseClock { get; } = baseClock;
        public ulong RtpTimestamp { get; } = rtpTimeStamp;
        public DateTime ClockTimeStamp { get; } = clockTimeStamp;
        //public DateTime TimeStamp { get; } = timeStamp;
        public List<ReadOnlyMemory<byte>> Data { get; } = data;
    }
}
