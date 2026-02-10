using System;
using System.Collections.Generic;

namespace EconSim.Core.Diagnostics
{
    public sealed class RingBufferLogSink : IDomainLogSink
    {
        private readonly object _gate = new object();
        private readonly DomainLogEvent[] _items;
        private int _next;
        private int _count;

        public RingBufferLogSink(int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be > 0.");
            }

            _items = new DomainLogEvent[capacity];
        }

        public int Capacity => _items.Length;

        public int Count
        {
            get
            {
                lock (_gate)
                {
                    return _count;
                }
            }
        }

        public void Write(DomainLogEvent entry)
        {
            lock (_gate)
            {
                _items[_next] = entry;
                _next = (_next + 1) % _items.Length;
                if (_count < _items.Length)
                {
                    _count++;
                }
            }
        }

        public void Clear()
        {
            lock (_gate)
            {
                Array.Clear(_items, 0, _items.Length);
                _next = 0;
                _count = 0;
            }
        }

        public List<DomainLogEvent> Snapshot(int maxItems)
        {
            if (maxItems <= 0)
            {
                return new List<DomainLogEvent>();
            }

            lock (_gate)
            {
                int take = Math.Min(Math.Min(maxItems, _count), _items.Length);
                var result = new List<DomainLogEvent>(take);

                int start = _count == _items.Length ? _next : 0;
                int first = _count - take;
                for (int i = 0; i < take; i++)
                {
                    int idx = (start + first + i) % _items.Length;
                    result.Add(_items[idx]);
                }

                return result;
            }
        }
    }
}
