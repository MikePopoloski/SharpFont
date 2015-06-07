using System;

namespace SlimFont {
    // helper wrapper around 4CC codes for debugging purposes
    struct FourCC {
        uint value;

        public FourCC (uint value) {
            this.value = value;
        }

        public FourCC (string str) {
            if (str.Length != 4)
                throw new InvalidOperationException("Invalid FourCC code");
            value = str[0] | ((uint)str[1] << 8) | ((uint)str[2] << 16) | ((uint)str[3] << 24);
        }

        public override string ToString () {
            return new string(new[] {
                    (char)(value & 0xff),
                    (char)((value >> 8) & 0xff),
                    (char)((value >> 16) & 0xff),
                    (char)(value >> 24)
                });
        }

        public static implicit operator FourCC (string value) => new FourCC(value);
        public static implicit operator FourCC (uint value) => new FourCC(value);
        public static implicit operator uint (FourCC fourCC) => fourCC.value;

        public static readonly FourCC Otto = "OTTO";
        public static readonly FourCC True = "true";
        public static readonly FourCC Ttcf = "ttcf";
        public static readonly FourCC Typ1 = "typ1";
        public static readonly FourCC Head = "head";
        public static readonly FourCC Glyf = "glyf";
        public static readonly FourCC Post = "post";
        public static readonly FourCC Hhea = "hhea";
        public static readonly FourCC Hmtx = "hmtx";
        public static readonly FourCC Maxp = "maxp";
        public static readonly FourCC Loca = "loca";
    }
}
