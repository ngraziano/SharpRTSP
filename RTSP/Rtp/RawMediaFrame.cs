using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;

namespace Rtsp.Rtp
{
    public class RawMediaFrame : IDisposable
    {
        private bool disposedValue;
        private IEnumerable<ReadOnlyMemory<byte>> _data;
        private IEnumerable<IMemoryOwner<byte>> _owners;
        private DateTime _clockTimestamp;
        private uint _rtpTimestamp;

        // a frame of the parsers' own: its lists, and the frames disposed kept to be filled again
        private List<ReadOnlyMemory<byte>>? _dataList;
        private List<IMemoryOwner<byte>>? _ownerList;
        private static readonly Stack<RawMediaFrame> Idle = new();
        private const int MaxIdle = 64;

        public IEnumerable<ReadOnlyMemory<byte>> Data
        {
            get
            {
                if (disposedValue) throw new ObjectDisposedException(nameof(RawMediaFrame));
                return _data;
            }
        }

        public required DateTime ClockTimestamp { get => _clockTimestamp; init => _clockTimestamp = value; }
        public required uint RtpTimestamp { get => _rtpTimestamp; init => _rtpTimestamp = value; }

        public RawMediaFrame(IEnumerable<ReadOnlyMemory<byte>> data, IEnumerable<IMemoryOwner<byte>> owners)
        {
            _data = data;
            _owners = owners;
        }

        /// <summary>
        /// A frame of copies of the lists, of one disposed before where there is one: disposed, it is kept to be filled
        /// again; one never disposed is collected as any other.
        /// </summary>
        internal static RawMediaFrame Rent(List<ReadOnlyMemory<byte>> data, List<IMemoryOwner<byte>> owners, uint rtpTimestamp, DateTime clockTimestamp)
        {
            var frame = Take();
            frame._dataList!.AddRange(data);
            frame._ownerList!.AddRange(owners);
            frame._rtpTimestamp = rtpTimestamp;
            frame._clockTimestamp = clockTimestamp;
            return frame;
        }

        /// <summary>A frame of one buffer, as <see cref="Rent(List{ReadOnlyMemory{byte}}, List{IMemoryOwner{byte}}, uint, DateTime)"/>.</summary>
        internal static RawMediaFrame Rent(ReadOnlyMemory<byte> data, IMemoryOwner<byte> owner, uint rtpTimestamp, DateTime clockTimestamp)
        {
            var frame = Take();
            frame._dataList!.Add(data);
            frame._ownerList!.Add(owner);
            frame._rtpTimestamp = rtpTimestamp;
            frame._clockTimestamp = clockTimestamp;
            return frame;
        }

        private static RawMediaFrame Take()
        {
            RawMediaFrame? frame = null;
            lock (Idle)
            {
                if (Idle.Count > 0) frame = Idle.Pop();
            }
            if (frame == null)
            {
                var data = new List<ReadOnlyMemory<byte>>();
                var owners = new List<IMemoryOwner<byte>>();
                frame = new RawMediaFrame(data, owners) { RtpTimestamp = 0, ClockTimestamp = DateTime.MinValue };
                frame._dataList = data;
                frame._ownerList = owners;
            }
            frame.disposedValue = false;
            return frame;
        }

        public bool Any() => Data is ICollection<ReadOnlyMemory<byte>> collection ? collection.Count > 0 : Data.Any();

        protected virtual void Dispose(bool disposing)
        {
            // the one empty frame is shared: never disposed
            if (!disposedValue && !ReferenceEquals(this, Empty))
            {
                if (_ownerList != null)
                {
                    // of the parsers' own: its owners by index, its lists emptied, the frame kept to be filled again
                    for (int i = 0; i < _ownerList.Count; i++)
                    {
                        _ownerList[i].Dispose();
                    }
                    _ownerList.Clear();
                    _dataList!.Clear();
                    disposedValue = true;
                    lock (Idle)
                    {
                        if (Idle.Count < MaxIdle) Idle.Push(this);
                    }
                    return;
                }
                if (disposing)
                {
                    foreach (var owner in _owners)
                    {
                        owner.Dispose();
                    }
                }
                disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        public static RawMediaFrame Empty { get; } = new([], []) { RtpTimestamp = 0, ClockTimestamp = DateTime.MinValue };
    }
}
