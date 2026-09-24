using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NavApp = Autodesk.Navisworks.Api.Application;

namespace PyNavis.Runtime.Ribbon
{
    /// <summary>
    /// Evaluates the seven context conditions against the live document, once per hub
    /// event (a snapshot), so N gated buttons cost one API sweep instead of N.
    /// Clash data is reached by reflection because Autodesk.Navisworks.Clash.dll is not
    /// a compile-time reference of this project; inside a Manage session it is loaded.
    /// </summary>
    public static class ContextConditions
    {
        private static MethodInfo _getClash;      // DocumentClash.GetClash(Document)
        private static PropertyInfo _testsData;   // DocumentClash.TestsData
        private static PropertyInfo _tests;       // ClashTestsData.Tests

        public static HashSet<string> Snapshot()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var doc = NavApp.ActiveDocument;
                if (doc == null || doc.Models.Count == 0) return set;
                set.Add("doc");
                if (doc.Models.Count > 1) set.Add("multi-model");
                if (!doc.CurrentSelection.IsEmpty) set.Add("selection");
                if (doc.SelectionSets.RootItem != null && doc.SelectionSets.RootItem.Children.Count > 0)
                    set.Add("selection-sets");
                if (doc.SavedViewpoints.RootItem != null && doc.SavedViewpoints.RootItem.Children.Count > 0)
                    set.Add("viewpoints");
                AddClashConditions(doc, set);
            }
            catch (Exception ex)
            {
                Log.Error("Context snapshot failed", ex);
            }
            return set;
        }

        private static void AddClashConditions(Autodesk.Navisworks.Api.Document doc, HashSet<string> set)
        {
            try
            {
                if (_getClash == null)
                {
                    var asm = AppDomain.CurrentDomain.Load("Autodesk.Navisworks.Clash");
                    var type = asm.GetType("Autodesk.Navisworks.Api.Clash.DocumentClash");
                    _getClash = type?.GetMethod("GetClash",
                        new[] { typeof(Autodesk.Navisworks.Api.Document) });
                }
                var clash = _getClash?.Invoke(null, new object[] { doc });
                if (clash == null) return;
                if (_testsData == null) _testsData = clash.GetType().GetProperty("TestsData");
                var testsData = _testsData?.GetValue(clash);
                if (testsData == null) return;
                if (_tests == null) _tests = testsData.GetType().GetProperty("Tests");
                if (!(_tests?.GetValue(testsData) is IEnumerable tests)) return;

                foreach (var test in tests)
                {
                    set.Add("clash-tests");
                    // ClashTest derives from GroupItem; its Children are the results.
                    var children = test.GetType().GetProperty("Children")?.GetValue(test) as IEnumerable;
                    if (children != null && children.GetEnumerator().MoveNext())
                    {
                        set.Add("clash-results");
                        break;
                    }
                }
            }
            catch
            {
                // Clash module absent (e.g. Simulate) or API shape drift: conditions stay false.
            }
        }
    }
}
