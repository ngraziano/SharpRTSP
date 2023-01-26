using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Rtsp.Utils;

/// <summary>
/// A pooled memory buffer backed by a <see cref="MemoryPool{T}.Shared"/> of <see langword="byte"/> for efficient writing of byte data and
/// allows reading the written content through a <see cref="ReadOnlySequence{Byte}"/> using the <see cref="GetReadOnlySequence"/> method.
/// </summary>
internal sealed class PooledSequence : IDisposable
{
    private readonly List<(ReadOnlyMemory<byte> memory, IDisposable memoryOwner)> _buffers = new();
    private readonly MemoryPool<byte> _pool;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="PooledSequence"/> class with an optional initial buffer size and array pool.
    /// </summary>
    /// <param name="initialBufferSize">The initial size of the buffer to rent from the pool. Defaults to 4096 bytes.</param>
    /// <param name="pool">The array pool to use. If null, <see cref="MemoryPool{T}.Shared"/> of <see langword="byte"/> is used.</param>
    public PooledSequence(MemoryPool<byte>? pool = null)
    {
        _pool = pool ?? MemoryPool<byte>.Shared;
    }

    /// <summary>
    /// The total length of all rented buffers in bytes.
    /// </summary>
    public int Length { get; private set; }

    /// <summary>
    /// Rents a writable memory buffer of the specified size.
    /// </summary>
    /// <param name="size">The size of the memory buffer to rent, in bytes.</param>
    /// <returns>A writable <see cref="Memory{byte}"/> buffer.</returns>
    /// <exception cref="ObjectDisposedException">
    /// Thrown if the buffer has already been disposed.
    /// </exception>
    public Memory<byte> GetMemory(int size)
    {
        EnsureNotDisposed();

        var memoryOwner = _pool.Rent(size);
        var memory = memoryOwner.Memory.Slice(0, size);
        _buffers.Add((memory, memoryOwner));
        Length += size;

        return memory;
    }

    /// <summary>
    /// Returns a <see cref="ReadOnlySequence{Byte}"/> representing the written data across all buffers.
    /// </summary>
    /// <returns>A read-only sequence of bytes.</returns>
    public ReadOnlySequence<byte> GetReadOnlySequence()
    {
        EnsureNotDisposed();

        SequenceSegment? first = null;
        SequenceSegment? last = null;

        for (var i = 0; i < _buffers.Count; i++)
        {
            var (buffer, _) = _buffers[i];

            var segment = new SequenceSegment(buffer);

            if (first == null)
            {
                first = segment;
            }

            if (last != null)
            {
                last.SetNext(segment);
            }

            last = segment;
        }

        if (first == null || last == null)
        {
            return ReadOnlySequence<byte>.Empty;
        }

        return new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
    }

    /// <summary>
    /// Clears the buffer and resets the internal state of the object.
    /// </summary>
    public void Clear()
    {
        EnsureNotDisposed();

        foreach (var (_, memoryOwner) in _buffers)
        {
            memoryOwner.Dispose();
        }

        _buffers.Clear();
        Length = 0;
    }


    /// <summary>
    /// Creates a deep copy of the current <see cref="PooledSequence"/> instance,
    /// including all written data up to the current write position.
    /// </summary>
    /// <returns>
    /// A new <see cref="PooledSequence"/> instance containing a copy of the data
    /// from the original buffer. The cloned instance has its own rented buffers and
    /// maintains the same write position as the original.
    /// </returns>

    public PooledSequence Clone()
    {
        EnsureNotDisposed();

        var clone = new PooledSequence();

        foreach (var (buffer, _) in _buffers)
        {
            buffer.CopyTo(clone.GetMemory(buffer.Length));
        }

        return clone;
    }

    /// <summary>
    /// Copies the contents of the buffer into the provided destination span.
    /// </summary>
    /// <param name="destination">The span to copy the data into.</param>
    /// <exception cref="ArgumentException">
    /// Thrown if the destination span is smaller than the total buffer length.
    /// </exception>
    /// <exception cref="ObjectDisposedException">
    /// Thrown if the buffer has been disposed.
    /// </exception>
    public void CopyTo(Span<byte> destination)
    {
        EnsureNotDisposed();

        if (destination.Length < Length)
            throw new ArgumentException("Destination span is too small.", nameof(destination));

        foreach (var (memory, _) in _buffers)
        {
            memory.Span.CopyTo(destination);
            destination = destination.Slice(memory.Length);
        }
    }

    /// <summary>
    /// Releases all buffers back to the pool and clears internal state.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Clear();
        _disposed = true;
    }


#if NET8_0_OR_GREATER
    [StackTraceHidden]
#endif
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureNotDisposed()
    {
#if NET8_0_OR_GREATER
        ObjectDisposedException.ThrowIf(_disposed, this);
#else
        if (_disposed)
        {
            ThrowObjectDisposedException();

            [MethodImpl(MethodImplOptions.NoInlining)]
            static void ThrowObjectDisposedException() => throw new ObjectDisposedException(typeof(PooledSequence).FullName);
        }
#endif
    }

    private class SequenceSegment : ReadOnlySequenceSegment<byte>
    {
        public SequenceSegment(ReadOnlyMemory<byte> memory)
        {
            Memory = memory;
        }

        public void SetNext(SequenceSegment next)
        {
            Next = next;
            next.RunningIndex = RunningIndex + Memory.Length;
        }
    }
}
