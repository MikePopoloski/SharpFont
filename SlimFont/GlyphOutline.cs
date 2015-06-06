using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SlimFont {
    struct GlyphOutline {
        public Point[] Points;
        public GlyphFlags[] PointFlags;
        public int[] ContourEndpoints;
    }

    struct Point {
        public int X;
        public int Y;
    }

    [Flags]
    enum GlyphFlags : byte {
        None = 0,
        OnCurve = 0x1,
        ShortX = 0x2,
        ShortY = 0x4,
        Repeat = 0x8,
        SameX = 0x10,
        SameY = 0x20
    }
}
