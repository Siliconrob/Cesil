﻿using System;
using System.Buffers;
using System.Runtime.InteropServices;

using static Cesil.DisposableHelper;

namespace Cesil
{
    internal struct UnmanagedLookupArray<T> : ITestableDisposable
        where T : unmanaged
    {
        private static readonly unsafe int BYTES_PER_T = sizeof(T);
        private const int BYTES_PER_CHAR = sizeof(char);

        private int _Count;
        internal int Count
        {
            get
            {
                AssertNotDisposedInternal(this);

                return _Count;
            }
        }

        private readonly int NumElements;

        private IMemoryOwner<char>? Owner;

        public bool IsDisposed => Owner == null;

        // internal for testing purposes
        internal Span<T> Data
        {
            get
            {
                if (Owner != null)
                {
                    return MemoryMarshal.Cast<char, T>(Owner.Memory.Span)[0..NumElements];
                }

                return default;
            }
        }

        internal UnmanagedLookupArray(MemoryPool<char> pool, int elemCount)
        {
            _Count = 0;
            NumElements = elemCount;

            var totalBytesNeeded = (NumElements * BYTES_PER_T);
            var totalCharsNeeded = totalBytesNeeded / BYTES_PER_CHAR;
            if ((totalBytesNeeded % BYTES_PER_CHAR) != 0)
            {
                totalCharsNeeded++;
            }

            Owner = pool.Rent(totalCharsNeeded);

            Owner.Memory.Slice(0, totalCharsNeeded).Span.Clear();
        }

        internal void Clear()
        {
            AssertNotDisposedInternal(this);

            Data.Clear();
            _Count = 0;
        }

        internal void Add(T item)
        {
            AssertNotDisposedInternal(this);

            Set(_Count, item);
        }

        internal void Set(int ix, T item)
        {
            AssertNotDisposedInternal(this);

            var data = Data;

            data[ix] = item;

            _Count = Math.Max(_Count, ix + 1);
        }

        internal void Get(int ix, T defaultValue, out T value)
        {
            AssertNotDisposedInternal(this);

            if (ix >= Data.Length)
            {
                value = defaultValue;
                return;
            }

            value = Data[ix];
        }

        public void Dispose()
        {
            if (!IsDisposed)
            {
                Owner?.Dispose();
                Owner = null;
            }
        }
    }
}
