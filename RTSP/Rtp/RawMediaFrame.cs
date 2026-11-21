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
        // of a parser of no memory pool given: its buffers' arrays of ArrayPool<byte>.Shared, of no owner object of each
        private List<byte[]>? _arrayList;
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
        internal static RawMediaFrame Rent(List<ReadOnlyMemory<byte>> data, List<IMemoryOwner<byte>> owners, List<byte[]> arrays, uint rtpTimestamp, DateTime clockTimestamp)
        {
            var frame = Take();
            frame._dataList!.AddRange(data);
            frame._ownerList!.AddRange(owners);
            frame._arrayList!.AddRange(arrays);
            frame._rtpTimestamp = rtpTimestamp;
            frame._clockTimestamp = clockTimestamp;
            return frame;
        }

        /// <summary>A frame of one buffer, of its owner or its array (<see cref="RentBuffer(MemoryPool{byte}?, int, out IMemoryOwner{byte}?, out byte[]?)"/>).</summary>
        internal static RawMediaFrame Rent(ReadOnlyMemory<byte> data, IMemoryOwner<byte>? owner, byte[]? array, uint rtpTimestamp, DateTime clockTimestamp)
        {
            var frame = Take();
            frame._dataList!.Add(data);
            if (owner != null)
                frame._ownerList!.Add(owner);
            if (array != null)
                frame._arrayList!.Add(array);
            frame._rtpTimestamp = rtpTimestamp;
            frame._clockTimestamp = clockTimestamp;
            return frame;
        }

        /// <summary>
        /// A buffer of a frame of a parser's, of the size: of the memory pool the parser was given, of an owner in the owners; of
        /// none, of an array of <see cref="ArrayPool{T}.Shared"/> in the arrays - of <see cref="MemoryPool{T}.Shared"/>, an owner
        /// object was allocated of each buffer.
        /// </summary>
        internal static Memory<byte> RentBuffer(MemoryPool<byte>? pool, int size, List<IMemoryOwner<byte>> owners, List<byte[]> arrays)
        {
            var memory = RentBuffer(pool, size, out IMemoryOwner<byte>? owner, out byte[]? array);
            if (owner != null)
                owners.Add(owner);
            else
                arrays.Add(array!);
            return memory;
        }

        /// <summary>A buffer as <see cref="RentBuffer(MemoryPool{byte}?, int, List{IMemoryOwner{byte}}, List{byte[]})"/>, of its owner or its array.</summary>
        internal static Memory<byte> RentBuffer(MemoryPool<byte>? pool, int size, out IMemoryOwner<byte>? owner, out byte[]? array)
        {
            if (pool != null)
            {
                owner = pool.Rent(size);
                array = null;
                return owner.Memory[..size];
            }
            owner = null;
            array = ArrayPool<byte>.Shared.Rent(size);
            return array.AsMemory(0, size);
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
                frame._arrayList = new List<byte[]>();
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
                    for (int i = 0; i < _arrayList!.Count; i++)
                    {
                        ArrayPool<byte>.Shared.Return(_arrayList[i]);
                    }
                    _arrayList.Clear();
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
