using System.Collections.Generic;

namespace PyNavis.Runtime.Events
{
    /// <summary>
    /// Consecutive-failure tracker: a hook that fails 3 times in a row is disabled
    /// for the session (Reload calls Reset). Pure so it is unit-testable.
    /// </summary>
    public class HookHealth
    {
        private readonly int _threshold;
        private readonly Dictionary<string, int> _failures = new Dictionary<string, int>();
        private readonly HashSet<string> _disabled = new HashSet<string>();

        public HookHealth(int threshold = 3) { _threshold = threshold; }

        public bool IsDisabled(string id) => _disabled.Contains(id);

        public void RecordSuccess(string id) => _failures.Remove(id);

        /// <summary>True exactly when this failure crossed the threshold (toast once).</summary>
        public bool RecordFailure(string id)
        {
            if (_disabled.Contains(id)) return false;
            _failures.TryGetValue(id, out var count);
            count++;
            _failures[id] = count;
            if (count < _threshold) return false;
            _disabled.Add(id);
            return true;
        }

        public void Reset()
        {
            _failures.Clear();
            _disabled.Clear();
        }
    }
}
