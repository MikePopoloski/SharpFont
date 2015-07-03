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

        public static explicit operator float (F2Dot14 v) => v.value / 16384.0f;
    }

    struct FUnit {
        int value;

        public static explicit operator int (FUnit v) => v.value;
        public static explicit operator FUnit (int v) => new FUnit { value = v };

        public static FUnit operator -(FUnit lhs, FUnit rhs) => (FUnit)(lhs.value - rhs.value);
        public static FUnit operator +(FUnit lhs, FUnit rhs) => (FUnit)(lhs.value + rhs.value);
        public static float operator *(FUnit lhs, float rhs) => lhs.value * rhs;

        public static FUnit Max (FUnit a, FUnit b) => (FUnit)Math.Max(a.value, b.value);
        public static FUnit Min (FUnit a, FUnit b) => (FUnit)Math.Min(a.value, b.value);
    }
}
