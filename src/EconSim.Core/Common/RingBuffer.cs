using System;
using System.Collections;
using System.Collections.Generic;

namespace EconSim.Core.Common
{
    /// <summary>
    /// Fixed-capacity ring buffer. Oldest entries are silently overwritten when full.
    /// Enumeration yields items oldest-first.
    /// </summary>
    public sealed class RingBuffer<T> : IEnumerable<T>
    {
        private readonly T[] _buf;
        private int _head; // next write position
        private int _count;

        public RingBuffer(int capacity)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _buf = new T[capacity];
        }

        public int Count => _count;
        public int Capacity => _buf.Length;

        public void Add(T item)
        {
            _buf[_head] = item;
            _head = (_head + 1) % _buf.Length;
            if (_count < _buf.Length)
                _count++;
        }

        /// <summary>
        /// Get item by logical index (0 = oldest).
        /// </summary>
        public T this[int index]
        {
            get
            {
                if ((uint)index >= (uint)_count)
                    throw new ArgumentOutOfRangeException(nameof(index));
                int start = _count < _buf.Length ? 0 : _head;
                return _buf[(start + index) % _buf.Length];
            }
        }

        public IEnumerator<T> GetEnumerator()
        {
            int start = _count < _buf.Length ? 0 : _head;
            for (int i = 0; i < _count; i++)
                yield return _buf[(start + i) % _buf.Length];
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
