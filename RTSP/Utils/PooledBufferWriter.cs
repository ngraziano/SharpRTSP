using System;
using System.Buffers;
using System.Collections.Generic;

namespace Rtsp.Utils;

/// <summary>
/// A pooled buffer writer that implements <see cref="IBufferWriter{Byte}"/>,
/// backed by segments rented from a <see cref="MemoryPool{Byte}"/>.
/// </summary>
internal sealed class PooledBufferWriter : IBufferWriter<byte>, IDisposable
{
    private readonly MemoryPool<byte> _memoryPool;
    private readonly List<IMemoryOwner<byte>> _owners = new();

    private IMemoryOwner<byte>? _currentOwner;
    private Memory<byte> _currentMemory;
    private int _currentIndex;

    /// <summary>
    /// Gets the total number of bytes written to the buffer.
    /// </summary>
    public int Length { get; private set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="PooledBufferWriter"/> class.
    /// </summary>
    /// <param name="memoryPool">An optional memory pool to use. Defaults to <see cref="MemoryPool{Byte}.Shared"/>.</param>
    public PooledBufferWriter(MemoryPool<byte>? memoryPool = null)
    {
        _memoryPool = memoryPool ?? MemoryPool<byte>.Shared;
    }

    /// <summary>
    /// Clears the buffer, returning all rented memory segments to the pool.
    /// </summary>
    public void Clear()
    {
        foreach (var owner in _owners)
        {
            owner.Dispose();
        }

        _owners.Clear();

        if (_currentOwner != null)
        {
            _currentOwner.Dispose();
            _currentOwner = null;
        }

        _currentMemory = default;
        _currentIndex = 0;
        Length = 0;
    }

    /// <summary>
    /// Copies the written data to the specified destination span.
    /// </summary>
    /// <param name="destination">The span to copy data into.</param>
    /// <exception cref="ArgumentException">Thrown if the destination span is too small.</exception>
    public void CopyTo(Span<byte> destination)
    {
        if (destination.Length < Length)
        {
            throw new ArgumentException("Destination span is too small.", nameof(destination));
        }

        int copied = 0;

        foreach (var owner in _owners)
        {
            var span = owner.Memory.Span;
            span.CopyTo(destination.Slice(copied));
            copied += span.Length;
        }

        if (_currentIndex > 0)
        {
            _currentMemory.Span.Slice(0, _currentIndex).CopyTo(destination.Slice(copied));
        }
    }

    /// <summary>
    /// Writes a single byte to the buffer.
    /// </summary>
    /// <param name="value">The byte to write.</param>
    public void Write(byte value)
    {
        EnsureCapacity(1);
        _currentMemory.Span[_currentIndex] = value;
        Advance(1);
    }

    /// <summary>
    /// Writes a span of bytes to the buffer.
    /// </summary>
    /// <param name="source">The span of bytes to write.</param>
    public void Write(ReadOnlySpan<byte> source)
    {
        var offset = 0;

        while (offset < source.Length)
        {
            EnsureCapacity(1); // Ensure at least one byte is available
            int writable = Math.Min(_currentMemory.Length - _currentIndex, source.Length - offset);
            source.Slice(offset, writable).CopyTo(_currentMemory.Span.Slice(_currentIndex));
            Advance(writable);
            offset += writable;
        }
    }

    /// <inheritdoc />
    public void Advance(int count)
    {
        if (count < 0 || count > _currentMemory.Length - _currentIndex)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        _currentIndex += count;
        Length += count;
    }

    /// <inheritdoc />
    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        EnsureCapacity(sizeHint);
        return _currentMemory.Slice(_currentIndex);
    }

    /// <inheritdoc />
    public Span<byte> GetSpan(int sizeHint = 0)
    {
        return GetMemory(sizeHint).Span;
    }

    /// <summary>
    /// Ensures the current buffer has at least the specified capacity available.
    /// If not, commits the current buffer and rents a new one.
    /// </summary>
    /// <param name="sizeHint">The minimum number of bytes required.</param>
    private void EnsureCapacity(int sizeHint)
    {
        if (_currentMemory.Length - _currentIndex < sizeHint)
        {
            if (_currentOwner != null)
            {
                _owners.Add(_currentOwner);
                _currentOwner = null;
            }

            var bufferSize = Math.Max(sizeHint, 4096);
            _currentOwner = _memoryPool.Rent(bufferSize);
            _currentMemory = _currentOwner.Memory;
            _currentIndex = 0;
        }
    }

    /// <summary>
    /// Disposes the buffer writer and returns all rented memory to the pool.
    /// </summary>
    public void Dispose()
    {
        Clear();
    }
}
