using PyNavis.Runtime.Overlay;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// The view overlay store's pure half: tagged add/replace/clear, and the rule
    /// that drops a dimension anchored to a measurement the user has since
    /// replaced. Drawing itself needs a live view and is field-checked.
    /// </summary>
    public class OverlayRegistryTests
    {
        private static OverlayItem Item(string tag, double[] first = null, double[] end = null)
        {
            var item = new OverlayItem
            {
                Tag = tag, Label = "0ft 4in", LabelAt = new[] { 0.0, 0.0, 0.0 },
                AnchorFirst = first, AnchorEnd = end,
            };
            item.Segments.Add(new OverlaySegment { From = new[] { 0.0, 0.0, 0.0 }, To = new[] { 1.0, 0.0, 0.0 } });
            return item;
        }

        [Fact]
        public void Add_ReplacesAnItemWithTheSameTag_AndClearRemovesByTagOrAll()
        {
            OverlayRegistry.Clear(null);
            OverlayRegistry.Add(Item("a"));
            OverlayRegistry.Add(Item("a"));
            OverlayRegistry.Add(Item("b"));
            Assert.Equal(2, OverlayRegistry.Count);

            OverlayRegistry.Clear("a");
            var left = Assert.Single(OverlayRegistry.Items);
            Assert.Equal("b", left.Tag);

            OverlayRegistry.Clear(null);
            Assert.Equal(0, OverlayRegistry.Count);
        }

        [Fact]
        public void IsStale_OnlyWhenAnchoredAndTheMeasurementMoved()
        {
            var f = new[] { 1.0, 2.0, 3.0 };
            var e = new[] { 4.0, 5.0, 6.0 };

            // A free-standing item never goes stale, even with no measurement at all.
            Assert.False(OverlayRegistry.IsStale(Item("free"), false, null, false, null));

            var anchored = Item("m", f, e);
            Assert.False(OverlayRegistry.IsStale(anchored, true, f, true, e));
            // The API hands back the same doubles each frame; a billionth is noise.
            Assert.False(OverlayRegistry.IsStale(anchored, true, new[] { 1.0 + 1e-9, 2.0, 3.0 }, true, e));
            // Measurement cleared, or its end moved: gone.
            Assert.True(OverlayRegistry.IsStale(anchored, false, null, true, e));
            Assert.True(OverlayRegistry.IsStale(anchored, true, f, true, new[] { 4.0, 5.0, 6.5 }));
        }
    }
}
