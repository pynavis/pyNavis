using System;

namespace PyNavis.Runtime.Overlay
{
    /// <summary>
    /// The pure geometry behind the overlay's drawing: which way up the view counts
    /// pixel rows, where the camera looks, and what part of a line lies in front of
    /// it. No Navisworks types, so it is unit tested.
    /// </summary>
    public static class OverlayMath
    {
        /// <summary>
        /// Which way View.ProjectPoint counts rows, from two projected probes: one on
        /// the line of sight and one a little above it. True when the upper probe got
        /// the smaller row (rows from the top), false when the larger (rows from the
        /// bottom), null when they landed on the same row and nothing can be told.
        /// Field-measured both ways, so it is asked of the view rather than assumed.
        /// </summary>
        public static bool? RowsFromTop(double centreY, double aboveY)
        {
            if (Math.Abs(centreY - aboveY) < 1.0) return null;
            return aboveY < centreY;
        }

        /// <summary>Rotates v by the quaternion (a, b, c, d) = (x, y, z, w).</summary>
        public static double[] Rotate(double a, double b, double c, double d, double[] v)
        {
            // v' = v + 2w(q x v) + 2 q x (q x v)
            var cx = b * v[2] - c * v[1];
            var cy = c * v[0] - a * v[2];
            var cz = a * v[1] - b * v[0];
            var dx = b * cz - c * cy;
            var dy = c * cx - a * cz;
            var dz = a * cy - b * cx;
            return new[]
            {
                v[0] + 2.0 * (d * cx + dx),
                v[1] + 2.0 * (d * cy + dy),
                v[2] + 2.0 * (d * cz + dz),
            };
        }

        /// <summary>A Navisworks camera looks down its own -Z with +Y up.</summary>
        public static double[] Direction(double a, double b, double c, double d)
            => Rotate(a, b, c, d, new[] { 0.0, 0.0, -1.0 });

        public static double[] Up(double a, double b, double c, double d)
            => Rotate(a, b, c, d, new[] { 0.0, 1.0, 0.0 });

        /// <summary>
        /// The part of the line from-to that lies at least `near` in front of the eye
        /// along `direction`. False when none of it does. A point behind a
        /// perspective camera projects to a mirrored pixel, so a line is cut at the
        /// camera before it is projected, never after.
        /// </summary>
        public static bool ClipToFront(double[] from, double[] to, double[] eye, double[] direction,
                                       double near, out double[] clippedFrom, out double[] clippedTo)
        {
            clippedFrom = from;
            clippedTo = to;
            var df = Ahead(from, eye, direction);
            var dt = Ahead(to, eye, direction);
            if (df < near && dt < near) return false;
            if (df >= near && dt >= near) return true;
            var t = (near - df) / (dt - df);
            var cut = new[]
            {
                from[0] + (to[0] - from[0]) * t,
                from[1] + (to[1] - from[1]) * t,
                from[2] + (to[2] - from[2]) * t,
            };
            if (df < near) clippedFrom = cut;
            else clippedTo = cut;
            return true;
        }

        private static double Ahead(double[] p, double[] eye, double[] direction)
            => (p[0] - eye[0]) * direction[0] + (p[1] - eye[1]) * direction[1] + (p[2] - eye[2]) * direction[2];
    }
}
