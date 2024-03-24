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
        public required byte[] SPS { get; init; }
        public required byte[] PPS { get; init; }
    }

    public record H265StreamConfigurationData: IStreamConfigurationData
    {
        public required byte[] VPS { get; init; }
        public required byte[] SPS { get; init; }
        public required byte[] PPS { get; init; }
    }

    public record AacStreamConfigurationData : IStreamConfigurationData
    {
        public int ObjectType { get; init; }
        public int FrequencyIndex { get; init; }
        public int SamplingFrequency { get; init; }
        public int ChannelConfiguration { get; init; }
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
}
