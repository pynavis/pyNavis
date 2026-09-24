using System.Collections.Generic;

namespace PyNavis.Runtime.Output.Html
{
    /// <summary>
    /// Maps element-link indexes to the live objects (ModelItems) they represent.
    /// Per output window: links die with their window, never across documents.
    /// </summary>
    public class ElementRegistry
    {
        private readonly List<object> _items = new List<object>();

        public int Register(object item)
        {
            _items.Add(item);
            return _items.Count - 1;
        }

        public bool TryResolve(int index, out object item)
        {
            if (index >= 0 && index < _items.Count)
            {
                item = _items[index];
                return true;
            }
            item = null;
            return false;
        }
    }
}
