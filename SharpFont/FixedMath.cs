using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpFont {
    // Fixed point: 2.14
    // used for unit vectors
    struct F2Dot14 {
        short value;

        public F2Dot14 (short v) {
            value = v;
        }

        public F2Dot14 (int integer, int fraction) {
            value = (short)((integer << 14) | fraction);
        }

        public override string ToString () => $"{value / 16384.0}";

        public static explicit operator F2Dot14 (short v) => new F2Dot14(v);
        public static explicit operator short (F2Dot14 v) => v.value;
    }

    // Fixed point: 16.16
    // format used for glyph transform math
    struct F16Dot16 {
        int value;

        public F16Dot16 (F2Dot14 v) {
            value = (short)v << 2;
        }

        public F16Dot16 (int integer, int fraction) {
            value = (integer << 16) | fraction;
        }

        public override string ToString () => $"{value / 65536.0}";

        public static readonly F16Dot16 Zero = new F16Dot16();
        public static readonly F16Dot16 One = new F16Dot16(1, 0);
    }

    // Fixed point: 26.6
    // points in TTF files are usually in this format
    struct F26Dot6 {
        int value;

        public int IntPart => value >> 6;

        public F26Dot6 (int v) {
            value = v;
        }

        public override string ToString () => $"{value / 64.0}";

        public static explicit operator F26Dot6 (int v) => new F26Dot6(v);
        public static explicit operator int (F26Dot6 v) => v.value;

        public static F26Dot6 operator +(F26Dot6 lhs, F26Dot6 rhs) => (F26Dot6)(lhs.value + rhs.value);
        public static F26Dot6 operator -(F26Dot6 lhs, F26Dot6 rhs) => (F26Dot6)(lhs.value - rhs.value);
        public static F26Dot6 operator *(F26Dot6 lhs, int rhs) => (F26Dot6)(lhs.value * rhs);
        public static F26Dot6 operator /(F26Dot6 lhs, int rhs) => (F26Dot6)(lhs.value / rhs);
        public static F26Dot6 operator -(F26Dot6 v) => (F26Dot6)(-v.value);
    }

    struct Matrix2x2 {
        public F16Dot16 m11, m12;
        public F16Dot16 m21, m22;

        public Matrix2x2 (F16Dot16 m11, F16Dot16 m12, F16Dot16 m21, F16Dot16 m22) {
            this.m11 = m11;
            this.m12 = m12;
            this.m21 = m21;
            this.m22 = m22;
        }

        public static readonly Matrix2x2 Identity = new Matrix2x2(
            F16Dot16.One,
            F16Dot16.Zero,
            F16Dot16.Zero,
            F16Dot16.One
        );
    }

    struct BoundingBox {
        public static readonly BoundingBox Empty = new BoundingBox();

        public F26Dot6 MinX;
        public F26Dot6 MinY;
        public F26Dot6 MaxX;
        public F26Dot6 MaxY;

        public F26Dot6 Width => MaxX - MinX;
        public F26Dot6 Height => MaxY - MinY;

        public void UnionWith (Point point) {
            MinX = FixedMath.Min(MinX, point.X);
            MinY = FixedMath.Min(MinY, point.Y);
            MaxX = FixedMath.Max(MaxX, point.X);
            MaxY = FixedMath.Max(MaxY, point.Y);
        }
    }

    static class FixedMath {
        public static F26Dot6 Floor (F26Dot6 v) => (F26Dot6)((int)v & ~0x3f);
        public static F26Dot6 Ceiling (F26Dot6 v) => Floor((F26Dot6)((int)v + 0x3f));
        public static F26Dot6 Min (F26Dot6 a, F26Dot6 b) => (int)a < (int)b ? a : b;
        public static F26Dot6 Max (F26Dot6 a, F26Dot6 b) => (int)a > (int)b ? a : b;

        //public static void DivMod (F24Dot8 dividend, F24Dot8 divisor, out F24Dot8 quotient, out F24Dot8 remainder) {
        //    var q = (int)dividend / (int)divisor;
        //    var r = (int)dividend % (int)divisor;
        //    if (r < 0) {
        //        q--;
        //        r += (int)divisor;
        //    }

        //    quotient = new F24Dot8(q);
        //    remainder = new F24Dot8(r);
        //}

        public static void DivMod (float dividend, float divisor, out float quotient, out float remainder) {
            var q = dividend / divisor;
            var r = dividend % divisor;
            quotient = q;
            remainder = r;
        }

        public static BoundingBox Translate (BoundingBox bbox, F26Dot6 shiftX, F26Dot6 shiftY) {
            return new BoundingBox {
                MinX = bbox.MinX + shiftX,
                MinY = bbox.MinY + shiftY,
                MaxX = bbox.MaxX + shiftX,
                MaxY = bbox.MaxY + shiftY
            };
        }

        public static BoundingBox ComputeControlBox (Point[] points) {
            if (points.Length < 1)
                return BoundingBox.Empty;

            var first = points[0];
            var minX = first.X;
            var maxX = first.X;
            var minY = first.Y;
            var maxY = first.Y;

            for (int i = 1; i < points.Length; i++) {
                var point = points[i];
                minX = Min(minX, point.X);
                minY = Min(minY, point.Y);
                maxX = Max(maxX, point.X);
                maxY = Max(maxY, point.Y);
            }

            return new BoundingBox {
                MinX = Floor(minX),
                MinY = Floor(minY),
                MaxX = Ceiling(maxX),
                MaxY = Ceiling(maxY)
            };
        }
    }
}