using SharpFont;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpFont {
    public class Typeface {
        Renderer renderer = new Renderer();
        GlyphData[] glyphs;
        MetricsEntry[] hmetrics;
        MetricsEntry[] vmetrics;
        CharacterMap charMap;
        FontWeight weight;
        FontStretch stretch;
        FontStyle style;
        int cellAscent;
        int cellDescent;
        int lineHeight;
        int xHeight;
        int capHeight;
        int underlineSize;
        int underlinePosition;
        int strikeoutSize;
        int strikeoutPosition;
        int unitsPerEm;
        bool isFixedWidth;
        bool integerPpems;

        public bool IsFixedWidth => isFixedWidth;
        public FontWeight Weight => weight;
        public FontStretch Stretch => stretch;
        public FontStyle Style => style;

        internal Typeface (
            int unitsPerEm, int cellAscent, int cellDescent, int lineHeight, int xHeight,
            int capHeight, int underlineSize, int underlinePosition, int strikeoutSize,
            int strikeoutPosition, FontWeight weight, FontStretch stretch, FontStyle style,
            GlyphData[] glyphs, MetricsEntry[] hmetrics, MetricsEntry[] vmetrics,
            CharacterMap charMap, bool isFixedWidth, bool integerPpems
        ) {
            this.unitsPerEm = unitsPerEm;
            this.cellAscent = cellAscent;
            this.cellDescent = cellDescent;
            this.lineHeight = lineHeight;
            this.xHeight = xHeight;
            this.capHeight = capHeight;
            this.underlineSize = underlineSize;
            this.underlinePosition = underlinePosition;
            this.strikeoutSize = strikeoutSize;
            this.strikeoutPosition = strikeoutPosition;
            this.weight = weight;
            this.stretch = stretch;
            this.style = style;
            this.glyphs = glyphs;
            this.hmetrics = hmetrics;
            this.vmetrics = vmetrics;
            this.charMap = charMap;
            this.isFixedWidth = isFixedWidth;
            this.integerPpems = integerPpems;
        }

        public static float ComputePixelSize (float pointSize, int dpi) => pointSize * dpi / 72;

        public FaceMetrics GetFaceMetrics (float pixelSize) {
            var scale = ComputeScale(pixelSize);
            return new FaceMetrics(
                Round(cellAscent * scale),
                Round(cellDescent * scale),
                Round(lineHeight * scale),
                Round(xHeight * scale),
                Round(capHeight * scale),
                Round(underlineSize * scale),
                Round(underlinePosition * scale),
                Round(strikeoutSize * scale),
                Round(strikeoutPosition * scale)
            );
        }

        public Glyph GetGlyph (CodePoint codePoint, float pixelSize) {
            var glyphIndex = charMap.Lookup(codePoint);
            if (glyphIndex < 0)
                return null;

            // get horizontal metrics
            var horizontal = hmetrics[glyphIndex];

            //  get vertical metrics if we have them; otherwise synthesize them
            // TODO:

            var scale = ComputeScale(pixelSize);

            var glyphData = glyphs[glyphIndex];
            var outline = glyphData.Outline;
            var points = outline.Points;
            // TODO: don't round the control box
            var cbox = FixedMath.ComputeControlBox(points);

            return new Glyph(
                glyphData,
                renderer,
                (int)cbox.MinX * scale,
                (int)cbox.MaxY * scale,
                (int)(cbox.MaxX - cbox.MinX) * scale,
                (int)(cbox.MaxY - cbox.MinY) * scale,
                horizontal.Advance * scale
            );
        }

        float ComputeScale (float pixelSize) {
            if (integerPpems)
                pixelSize = Round(pixelSize);
            return pixelSize / unitsPerEm;
        }

        static float Round (float value) => (float)Math.Round(value, MidpointRounding.AwayFromZero);
    }

    public struct CodePoint {
        int value;

        public CodePoint (int codePoint) {
            value = codePoint;
        }

        public CodePoint (char character) {
            value = character;
        }

        public CodePoint (char highSurrogate, char lowSurrogate) {
            value = char.ConvertToUtf32(highSurrogate, lowSurrogate);
        }

        public static explicit operator CodePoint (int codePoint) => new CodePoint(codePoint);
        public static implicit operator CodePoint (char character) => new CodePoint(character);
    }

    public class FaceMetrics {
        public readonly float CellAscent;
        public readonly float CellDescent;
        public readonly float LineHeight;
        public readonly float XHeight;
        public readonly float CapHeight;
        public readonly float UnderlineSize;
        public readonly float UnderlinePosition;
        public readonly float StrikeoutSize;
        public readonly float StrikeoutPosition;

        public FaceMetrics (
            float cellAscent, float cellDescent, float lineHeight, float xHeight,
            float capHeight, float underlineSize, float underlinePosition,
            float strikeoutSize, float strikeoutPosition
        ) {
            CellAscent = cellAscent;
            CellDescent = cellDescent;
            LineHeight = lineHeight;
            XHeight = xHeight;
            CapHeight = capHeight;
            UnderlineSize = underlineSize;
            UnderlinePosition = underlinePosition;
            StrikeoutSize = strikeoutSize;
            StrikeoutPosition = strikeoutPosition;
        }
    }

    public enum FontWeight {
        Unknown = 0,
        Thin = 100,
        ExtraLight = 200,
        Light = 300,
        Normal = 400,
        Medium = 500,
        SemiBold = 600,
        Bold = 700,
        ExtraBold = 800,
        Black = 900
    }

    public enum FontStretch {
        Unknown,
        UltraCondensed,
        ExtraCondensed,
        Condensed,
        SemiCondensed,
        Normal,
        SemiExpanded,
        Expanded,
        ExtraExpanded,
        UltraExpanded
    }

    public enum FontStyle {
        Regular,
        Bold,
        Italic,
        Oblique
    }
}
