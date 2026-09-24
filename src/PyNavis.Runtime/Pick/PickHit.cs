namespace PyNavis.Runtime.Pick
{
    /// <summary>What the pick landed on, in the order the snap was chosen.</summary>
    public enum PickSnap
    {
        None,
        Vertex,
        Edge,
        LineVertex,
        LineMiddle,
        ArcCenter,
    }

    /// <summary>Why a session ended without a point.</summary>
    public enum PickCancelReason
    {
        None,
        Escape,
        RightClick,
        ToolChanged,
        Superseded,
        Error,
    }

    /// <summary>Where a session is in its one-way trip.</summary>
    public enum PickState
    {
        Idle,
        Active,
        Completed,
        Cancelled,
    }

    /// <summary>
    /// One finished pick. Point and Normal are plain doubles rather than API
    /// Point3D/UnitVector3D so the whole session half stays testable without
    /// Navisworks; Item is the ModelItem, kept as object for the same reason.
    /// </summary>
    public sealed class PickHit
    {
        public double[] Point { get; set; }
        public double[] Normal { get; set; }
        public PickSnap Snap { get; set; }
        public object Item { get; set; }
    }

    /// <summary>
    /// Translation of the raw PickResults bits into a snap kind, and the two
    /// questions the cursor asks of one. Pure integers in, pure enum out: the
    /// API enum never reaches here, so the mapping is unit-tested directly.
    /// </summary>
    public static class PickSnaps
    {
        // Autodesk.Navisworks.Api.PickResults is [Flags]; these are its values,
        // read by reflection from the 2026 assembly. Repeated as literals rather
        // than referenced so this file keeps no Navisworks dependency.
        public const int VertexBits = 112;      // Vertex0 | Vertex1 | Vertex2
        public const int EdgeBits = 7;          // Edge0To1 | Edge1To2 | Edge2To0
        public const int LineVertexBits = 196608;
        public const int LineMiddleBit = 262144;
        public const int ArcCenterBit = 1048576;

        /// <summary>
        /// The snap kind those result bits describe. A pick can report several at
        /// once (a vertex is also on two edges), so the order matters: the
        /// tightest, most deliberate point wins, and a plain face is the floor.
        /// </summary>
        public static PickSnap From(int resultBits)
        {
            if ((resultBits & VertexBits) != 0) return PickSnap.Vertex;
            if ((resultBits & LineVertexBits) != 0) return PickSnap.LineVertex;
            if ((resultBits & ArcCenterBit) != 0) return PickSnap.ArcCenter;
            if ((resultBits & LineMiddleBit) != 0) return PickSnap.LineMiddle;
            if ((resultBits & EdgeBits) != 0) return PickSnap.Edge;
            return PickSnap.None;
        }

        /// <summary>True for the snaps that land on one exact point, which is
        /// what the native measure tool shows its vertex cursor for.</summary>
        public static bool IsPointSnap(PickSnap snap)
        {
            return snap == PickSnap.Vertex || snap == PickSnap.LineVertex
                || snap == PickSnap.LineMiddle || snap == PickSnap.ArcCenter;
        }

        /// <summary>The name scripts see on hit.snap, or null for a plain face.</summary>
        public static string Name(PickSnap snap)
        {
            switch (snap)
            {
                case PickSnap.Vertex: return "vertex";
                case PickSnap.Edge: return "edge";
                case PickSnap.LineVertex: return "line-vertex";
                case PickSnap.LineMiddle: return "line-middle";
                case PickSnap.ArcCenter: return "arc-center";
                default: return null;
            }
        }
    }
}
