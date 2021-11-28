﻿using System;
using System.Buffers;
using System.Diagnostics;

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Mewdeko.Voice
{
    internal sealed class ArrayBufferWriter<T> : IBufferWriter<T>
    {
        // Copy of Array.MaxLength.
        private const int ArrayMaxLength = 0x7FFFFFC7;

        private const int DefaultInitialBufferSize = 256;

        private T[] _buffer;


        /// <summary>
        ///     Creates an instance of an <see cref="ArrayBufferWriter{T}" />, in which data can be written to,
        ///     with the default initial capacity.
        /// </summary>
        public ArrayBufferWriter()
        {
            _buffer = Array.Empty<T>();
            WrittenCount = 0;
        }

        /// <summary>
        ///     Creates an instance of an <see cref="ArrayBufferWriter{T}" />, in which data can be written to,
        ///     with an initial capacity specified.
        /// </summary>
        /// <param name="initialCapacity">The minimum capacity with which to initialize the underlying buffer.</param>
        /// <exception cref="ArgumentException">
        ///     Thrown when <paramref name="initialCapacity" /> is not positive (i.e. less than or equal to 0).
        /// </exception>
        public ArrayBufferWriter(int initialCapacity)
        {
            if (initialCapacity <= 0)
                throw new ArgumentException(null, nameof(initialCapacity));

            _buffer = new T[initialCapacity];
            WrittenCount = 0;
        }

        /// <summary>
        ///     Returns the data written to the underlying buffer so far, as a <see cref="ReadOnlyMemory{T}" />.
        /// </summary>
        public ReadOnlyMemory<T> WrittenMemory => _buffer.AsMemory(0, WrittenCount);

        /// <summary>
        ///     Returns the data written to the underlying buffer so far, as a <see cref="ReadOnlySpan{T}" />.
        /// </summary>
        public ReadOnlySpan<T> WrittenSpan => _buffer.AsSpan(0, WrittenCount);

        /// <summary>
        ///     Returns the amount of data written to the underlying buffer so far.
        /// </summary>
        public int WrittenCount { get; private set; }

        /// <summary>
        ///     Returns the total amount of space within the underlying buffer.
        /// </summary>
        public int Capacity => _buffer.Length;

        /// <summary>
        ///     Returns the amount of space available that can still be written into without forcing the underlying buffer to grow.
        /// </summary>
        public int FreeCapacity => _buffer.Length - WrittenCount;

        /// <summary>
        ///     Notifies <see cref="IBufferWriter{T}" /> that <paramref name="count" /> amount of data was written to the output
        ///     <see cref="Span{T}" />/<see cref="Memory{T}" />
        /// </summary>
        /// <exception cref="ArgumentException">
        ///     Thrown when <paramref name="count" /> is negative.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        ///     Thrown when attempting to advance past the end of the underlying buffer.
        /// </exception>
        /// <remarks>
        ///     You must request a new buffer after calling Advance to continue writing more data and cannot write to a previously
        ///     acquired buffer.
        /// </remarks>
        public void Advance(int count)
        {
            if (count < 0)
                throw new ArgumentException(null, nameof(count));

            if (WrittenCount > _buffer.Length - count)
                ThrowInvalidOperationException_AdvancedTooFar();

            WrittenCount += count;
        }

        /// <summary>
        ///     Returns a <see cref="Memory{T}" /> to write to that is at least the requested length (specified by
        ///     <paramref name="sizeHint" />).
        ///     If no <paramref name="sizeHint" /> is provided (or it's equal to <code>0</code>), some non-empty buffer is
        ///     returned.
        /// </summary>
        /// <exception cref="ArgumentException">
        ///     Thrown when <paramref name="sizeHint" /> is negative.
        /// </exception>
        /// <remarks>
        ///     This will never return an empty <see cref="Memory{T}" />.
        /// </remarks>
        /// <remarks>
        ///     There is no guarantee that successive calls will return the same buffer or the same-sized buffer.
        /// </remarks>
        /// <remarks>
        ///     You must request a new buffer after calling Advance to continue writing more data and cannot write to a previously
        ///     acquired buffer.
        /// </remarks>
        public Memory<T> GetMemory(int sizeHint = 0)
        {
            CheckAndResizeBuffer(sizeHint);
            Debug.Assert(_buffer.Length > WrittenCount);
            return _buffer.AsMemory(WrittenCount);
        }

        /// <summary>
        ///     Returns a <see cref="Span{T}" /> to write to that is at least the requested length (specified by
        ///     <paramref name="sizeHint" />).
        ///     If no <paramref name="sizeHint" /> is provided (or it's equal to <code>0</code>), some non-empty buffer is
        ///     returned.
        /// </summary>
        /// <exception cref="ArgumentException">
        ///     Thrown when <paramref name="sizeHint" /> is negative.
        /// </exception>
        /// <remarks>
        ///     This will never return an empty <see cref="Span{T}" />.
        /// </remarks>
        /// <remarks>
        ///     There is no guarantee that successive calls will return the same buffer or the same-sized buffer.
        /// </remarks>
        /// <remarks>
        ///     You must request a new buffer after calling Advance to continue writing more data and cannot write to a previously
        ///     acquired buffer.
        /// </remarks>
        public Span<T> GetSpan(int sizeHint = 0)
        {
            CheckAndResizeBuffer(sizeHint);
            Debug.Assert(_buffer.Length > WrittenCount);
            return _buffer.AsSpan(WrittenCount);
        }

        /// <summary>
        ///     Clears the data written to the underlying buffer.
        /// </summary>
        /// <remarks>
        ///     You must clear the <see cref="ArrayBufferWriter{T}" /> before trying to re-use it.
        /// </remarks>
        public void Clear()
        {
            Debug.Assert(_buffer.Length >= WrittenCount);
            _buffer.AsSpan(0, WrittenCount).Clear();
            WrittenCount = 0;
        }

        private void CheckAndResizeBuffer(int sizeHint)
        {
            if (sizeHint < 0)
                throw new ArgumentException(nameof(sizeHint));

            if (sizeHint == 0) sizeHint = 1;

            if (sizeHint > FreeCapacity)
            {
                var currentLength = _buffer.Length;

                // Attempt to grow by the larger of the sizeHint and double the current size.
                var growBy = Math.Max(sizeHint, currentLength);

                if (currentLength == 0) growBy = Math.Max(growBy, DefaultInitialBufferSize);

                var newSize = currentLength + growBy;

                if ((uint)newSize > int.MaxValue)
                {
                    // Attempt to grow to ArrayMaxLength.
                    var needed = (uint)(currentLength - FreeCapacity + sizeHint);
                    Debug.Assert(needed > currentLength);

                    if (needed > ArrayMaxLength) ThrowOutOfMemoryException();

                    newSize = ArrayMaxLength;
                }

                Array.Resize(ref _buffer, newSize);
            }

            Debug.Assert(FreeCapacity > 0 && FreeCapacity >= sizeHint);
        }

        private static void ThrowInvalidOperationException_AdvancedTooFar()
        {
            throw new InvalidOperationException();
        }

        private static void ThrowOutOfMemoryException()
        {
            throw new OutOfMemoryException();
        }
    }
}