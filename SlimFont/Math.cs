using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SlimFont {
    // Fixed point: 2.14
    struct F2Dot14 {
        short value;

        public F2Dot14 (short value) {
            this.value = value;
        }

        public F2Dot14 (int integer, int fraction) {
            value = (short)((integer << 14) | fraction);
        }

        public static explicit operator F2Dot14 (short v) => new F2Dot14(v);
        public static explicit operator short (F2Dot14 v) => v.value;

        public override string ToString () => $"{value / 16384.0}";
    }

    // Fixed point: 16.16
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
}