using System.Collections.Generic;
using Autodesk.Navisworks.Api;

namespace PyNavis.Runtime.Ai
{
    /// <summary>
    /// The API half of the attach-selection payload: reads the current selection's
    /// category and property NAMES and hands them to SelectionSummary. Values are
    /// never read, so nothing from the project can end up in the request. Needs a
    /// live Navisworks; the pure half is what the tests cover.
    /// </summary>
    public static class SelectionReader
    {
        /// <summary>Items beyond this many are counted but not read, so a 50,000-item
        /// selection does not stall the UI before the request even starts.</summary>
        public const int MaxItemsRead = 200;

        public static string Summarise()
        {
            var doc = Application.ActiveDocument;
            if (doc == null) return SelectionSummary.Build(0, new PropertyRow[0]);
            var items = doc.CurrentSelection.SelectedItems;
            var count = items.Count;
            var rows = new List<PropertyRow>();
            var read = 0;
            foreach (ModelItem item in items)
            {
                if (read++ >= MaxItemsRead) break;
                foreach (PropertyCategory category in item.PropertyCategories)
                    foreach (DataProperty property in category.Properties)
                        rows.Add(new PropertyRow { Category = category.DisplayName, Property = property.DisplayName });
            }
            return SelectionSummary.Build(count, rows);
        }
    }
}
