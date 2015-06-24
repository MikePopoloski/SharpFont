using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace SharpFont {
    struct FUnit {
        int value;

        public static explicit operator int (FUnit v) => v.value;
        public static explicit operator FUnit (int v) => new FUnit { value = v };

        public static FUnit operator -(FUnit lhs, FUnit rhs) => (FUnit)(lhs.value - rhs.value);
        public static float operator *(FUnit lhs, float rhs) => lhs.value * rhs;

        public static FUnit Max (FUnit a, FUnit b) => (FUnit)Math.Max(a.value, b.value);
        public static FUnit Min (FUnit a, FUnit b) => (FUnit)Math.Min(a.value, b.value);
    }

    struct GlyphOutline {
        public Point[] Points;
        public int[] ContourEndpoints;
    }

    struct BoundingBox {
        public static readonly BoundingBox Infinite = new BoundingBox {
            MinX = (FUnit)int.MaxValue,
            MinY = (FUnit)int.MaxValue,
            MaxX = (FUnit)int.MinValue,
            MaxY = (FUnit)int.MinValue
        };

        public FUnit MinX;
        public FUnit MinY;
        public FUnit MaxX;
        public FUnit MaxY;

        public FUnit Width => MaxX - MinX;
        public FUnit Height => MaxY - MinY;

        public void UnionWith (Point point) {
            MinX = FUnit.Min(MinX, point.X);
            MinY = FUnit.Min(MinY, point.Y);
            MaxX = FUnit.Max(MaxX, point.X);
            MaxY = FUnit.Max(MaxY, point.Y);
        }
    }

    struct Point {
        public FUnit X;
        public FUnit Y;
        public PointType Type;

        public Point (FUnit x, FUnit y) {
            X = x;
            Y = y;
            Type = PointType.OnCurve;
        }

        public static explicit operator Vector2 (Point p) => new Vector2((int)p.X, (int)p.Y);
    }

    struct PointF {
        public Vector2 P;
        public PointType Type;

        public PointF (Vector2 position, PointType type) {
            P = position;
            Type = type;
        }

        public void Offset (Vector2 offset) => P += offset;

        public static implicit operator Vector2 (PointF p) => p.P;
    }

    enum PointType {
        OnCurve,
        Quadratic,
        Cubic
    }
}
