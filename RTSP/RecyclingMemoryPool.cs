using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading;

namespace Rtsp
{
    /// <summary>
    /// The library's default pool: owners kept and handed out again rather than one made of each rent, of arrays of a pool
    /// of its own.
    /// </summary>
    /// <remarks>
    /// Not of <see cref="ArrayPool{T}.Shared"/>, whose arrays are cached of each thread: the receiving loops go from one
    /// thread of the pool to another, and an array returned on one thread waited there while the next frame, on another,
    /// had a new one made. A pool of its own has no such caches.
    /// </remarks>
    internal sealed class RecyclingMemoryPool : MemoryPool<byte>
    {
        public static RecyclingMemoryPool Instance { get; } = new();

        // a key frame's NAL units of a few MB at most
        private static readonly ArrayPool<byte> Arrays = ArrayPool<byte>.Create(maxArrayLength: 16 * 1024 * 1024, maxArraysPerBucket: 64);

        private const int MaxIdle = 256;
        private readonly Stack<Owner> _idle = new();

        public override int MaxBufferSize => int.MaxValue;

        public override IMemoryOwner<byte> Rent(int minBufferSize = -1)
        {
            Owner? owner = null;
            lock (_idle)
            {
                if (_idle.Count > 0) owner = _idle.Pop();
            }
            owner ??= new Owner(this);
            owner.Take(Arrays.Rent(minBufferSize < 0 ? 4096 : minBufferSize));
            return owner;
        }

        protected override void Dispose(bool disposing)
        {
        }

        private void Return(Owner owner)
        {
            lock (_idle)
            {
                if (_idle.Count < MaxIdle) _idle.Push(owner);
            }
        }

        private sealed class Owner(RecyclingMemoryPool pool) : IMemoryOwner<byte>
        {
            private byte[]? _array;

            public void Take(byte[] array) => _array = array;

            public Memory<byte> Memory => _array ?? throw new ObjectDisposedException(nameof(IMemoryOwner<byte>));

            // the array back to its pool and the owner to this one, once: disposed again, nothing
            public void Dispose()
            {
                byte[]? array = Interlocked.Exchange(ref _array, null);
                if (array == null) return;
                Arrays.Return(array);
                pool.Return(this);
            }
        }
    }
}
