using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpFont {
    struct FUnit {
        int value;
    }

    struct GlyphOutline {
        public Point[] Points;
        public PointType[] PointTypes;
        public int[] ContourEndpoints;
    }

    struct Point {
        public F26Dot6 X;
        public F26Dot6 Y;

        public Point (F26Dot6 x, F26Dot6 y) {
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
