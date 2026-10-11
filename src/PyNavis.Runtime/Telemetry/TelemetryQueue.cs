using System.Collections.Generic;
using System.Threading;

namespace PyNavis.Runtime.Telemetry
{
    /// <summary>
    /// Records waiting for the next flush, in memory. Bounded: when it is full the
    /// oldest record goes, so a stalled flush can never grow Navisworks' memory, and
    /// Dropped says how many went. Enqueue is O(1) under a lock held for a few
    /// instructions, so a click never waits on telemetry.
    /// </summary>
    public sealed class TelemetryQueue
    {
        public const int DefaultCapacity = 1000;

        private readonly object _gate = new object();
        private readonly Queue<TelemetryRecord> _records = new Queue<TelemetryRecord>();
        private long _dropped;

        public TelemetryQueue(int capacity = DefaultCapacity)
        {
            Capacity = capacity > 0 ? capacity : DefaultCapacity;
        }

        public int Capacity { get; }

        public int Count
        {
            get { lock (_gate) return _records.Count; }
        }

        /// <summary>Records pushed out by a full queue since it was made.</summary>
        public long Dropped => Interlocked.Read(ref _dropped);

        public void Enqueue(TelemetryRecord record)
        {
            if (record == null) return;
            lock (_gate)
            {
                if (_records.Count >= Capacity)
                {
                    _records.Dequeue();
                    Interlocked.Increment(ref _dropped);
                }
                _records.Enqueue(record);
            }
        }

        /// <summary>Takes up to <paramref name="max"/> records, oldest first.</summary>
        public List<TelemetryRecord> Drain(int max)
        {
            lock (_gate)
            {
                var take = System.Math.Min(System.Math.Max(max, 0), _records.Count);
                var taken = new List<TelemetryRecord>(take);
                for (var i = 0; i < take; i++) taken.Add(_records.Dequeue());
                return taken;
            }
        }
    }
}
