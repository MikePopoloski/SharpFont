using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SlimFont {
    struct GlyphOutline {
        public Point[] Points;
        public PointType[] PointTypes;
        public int[] ContourEndpoints;
    }

    struct Point {
        public F26Dot6 X;
        public F26Dot6 Y;

        public Point (int x, int y) {
            X = x;
            Y = y;
        }

        public override string ToString () {
            return $"{X}, {Y}";
        }
    }

    enum PointType {
        OnCurve,
        Quadratic,
        Cubic
    }
}
