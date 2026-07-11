namespace Rtsp.Rtp
{
    using System;
    using System.Buffers;

    /// <summary>
    /// Reorders and de-duplicates RTP packets.
    /// </summary>
    public class RtpReorderer : IDisposable
    {
        private readonly int _maxGap;
        private readonly int _bufferSize;
        private readonly int _mask;
        private readonly IMemoryOwner<byte>?[] _buffer;
        private readonly int[] _lengths;
        private readonly ushort[] _seqs;

        private ushort _expectedSeq;
        private bool _started;
        private uint? _ssrc;
        private int _bufferCount;
        private IMemoryOwner<byte>? _lastReturnedOwner;

        /// <summary>
        /// Initializes a new instance of the <see cref="RtpReorderer"/> class.
        /// </summary>
        /// <param name="maxGap">The maximum number of lost packets to wait for before skipping.</param>
        public RtpReorderer(int maxGap)
        {
            if (maxGap < 1) throw new ArgumentOutOfRangeException(nameof(maxGap));

            _maxGap = maxGap;
            // Use a power of two size for the circular buffer to allow bitwise masking
            _bufferSize = 1;
            while (_bufferSize <= maxGap) _bufferSize <<= 1;
            _bufferSize <<= 1; // Double it to ensure no collisions within the window

            _mask = _bufferSize - 1;
            _buffer = new IMemoryOwner<byte>[_bufferSize];
            _lengths = new int[_bufferSize];
            _seqs = new ushort[_bufferSize];
        }

        /// <summary>
        /// Processes an RTP packet.
        /// </summary>
        /// <param name="packet">The RTP packet to process, or a default/empty RtpPacket to depile buffered packets.</param>
        /// <returns>The next RTP packet in order, or an empty RtpPacket if no packet is ready.</returns>
        public RtpPacket Process(RtpPacket packet = default)
        {
            // Release the memory owner of the PREVIOUSLY returned packet
            // This ensures the caller has had time to use the RtpPacket
            _lastReturnedOwner?.Dispose();
            _lastReturnedOwner = null;

            if (packet.IsWellFormed)
            {
                uint ssrc = packet.Ssrc;
                ushort seq = (ushort)packet.SequenceNumber;

                if (_ssrc != null && _ssrc != ssrc)
                {
                    // SSRC change: reset reorderer
                    ClearBuffer();
                    _started = false;
                }

                _ssrc = ssrc;

                if (!_started)
                {
                    _started = true;
                    _expectedSeq = seq;
                }

                ushort delta = (ushort)(seq - _expectedSeq);

                if (delta > 32768)
                {
                    // Past packet (duplicate or too old)
                    return default;
                }

                if (delta > _maxGap)
                {
                    // Gap too large, jump to the current packet
                    ClearBuffer();
                    _expectedSeq = seq;
                    delta = 0;
                }

                if (delta == 0)
                {
                    // Nominal case: it's the packet we expect. 
                    // Optimization: return directly without copying to pool
                    _expectedSeq++;
                    return packet;
                }

                // Buffer the packet (handles de-duplication)
                BufferPacket(packet, seq);
            }

            // Check if the expected packet is in the buffer
            int index = _expectedSeq & _mask;
            var owner = _buffer[index];
            if (owner != null && _seqs[index] == _expectedSeq)
            {
                _buffer[index] = null;
                _bufferCount--;
                _expectedSeq++;

                // Store owner to dispose it on next call
                _lastReturnedOwner = owner;
                return new RtpPacket(owner.Memory.Span[.._lengths[index]]);
            }

            return default;
        }

        private void BufferPacket(RtpPacket packet, ushort seq)
        {
            int index = seq & _mask;
            // If already occupied by this sequence number, it's a duplicate
            if (_buffer[index] != null && _seqs[index] == seq) return;

            // If occupied by an old packet (should not happen with correct window size but safety first)
            _buffer[index]?.Dispose();

            if (_buffer[index] == null) _bufferCount++;

            _seqs[index] = seq;
            var owner = MemoryPool<byte>.Shared.Rent(packet.RawData.Length);
            _buffer[index] = owner;
            _lengths[index] = packet.RawData.Length;
            packet.RawData.CopyTo(owner.Memory.Span);
        }

        private void ClearBuffer()
        {
            for (int i = 0; i < _buffer.Length; i++)
            {
                _buffer[i]?.Dispose();
                _buffer[i] = null;
            }

            _lastReturnedOwner?.Dispose();
            _lastReturnedOwner = null;
        }

        public void Dispose()
        {
            ClearBuffer();
            GC.SuppressFinalize(this);
        }
    }
}