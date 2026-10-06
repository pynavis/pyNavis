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

        [Fact]
        public void RowsFromTop_IsReadFromAProbeAboveTheLineOfSight()
        {
            Assert.True(OverlayMath.RowsFromTop(430, 380));
            // Field case: the probe above the axis came back at row 533 with the
            // centre at 429, so rows ran from the bottom and labels sat mirrored.
            Assert.False(OverlayMath.RowsFromTop(429, 533));
            Assert.Null(OverlayMath.RowsFromTop(430, 430.4));
        }

        [Fact]
        public void Camera_LooksDownMinusZWithYUp_RotatedByItsQuaternion()
        {
            Assert.Equal(new[] { 0.0, 0.0, -1.0 }, OverlayMath.Direction(0, 0, 0, 1));
            Assert.Equal(new[] { 0.0, 1.0, 0.0 }, OverlayMath.Up(0, 0, 0, 1));
            // A quarter turn about X tips the camera up to look along +Y with +Z up.
            var h = System.Math.Sqrt(0.5);
            var direction = OverlayMath.Direction(h, 0, 0, h);
            var up = OverlayMath.Up(h, 0, 0, h);
            Assert.Equal(0.0, direction[0], 9);
            Assert.Equal(1.0, direction[1], 9);
            Assert.Equal(0.0, direction[2], 9);
            Assert.Equal(0.0, up[0], 9);
            Assert.Equal(0.0, up[1], 9);
            Assert.Equal(1.0, up[2], 9);
        }

        [Fact]
        public void ClipToFront_CutsALineAtTheCamera_AndDropsOneWhollyBehind()
        {
            var eye = new[] { 1370000.0, 0.0, 0.0 };
            var ahead = new[] { 1.0, 0.0, 0.0 };
            double[] a, b;

            Assert.True(OverlayMath.ClipToFront(new[] { 1370005.0, 0, 0 }, new[] { 1370009.0, 2, 0 },
                                                eye, ahead, 0.01, out a, out b));
            Assert.Equal(1370005.0, a[0]);
            Assert.Equal(1370009.0, b[0]);

            Assert.False(OverlayMath.ClipToFront(new[] { 1369990.0, 0, 0 }, new[] { 1369999.0, 2, 0 },
                                                 eye, ahead, 0.01, out a, out b));

            Assert.True(OverlayMath.ClipToFront(new[] { 1369996.0, 0, 0 }, new[] { 1370004.0, 8, 0 },
                                                eye, ahead, 0.01, out a, out b));
            Assert.Equal(1370000.01, a[0], 6);
            Assert.Equal(4.01, a[1], 6);
            Assert.Equal(1370004.0, b[0]);
        }
    }
}
