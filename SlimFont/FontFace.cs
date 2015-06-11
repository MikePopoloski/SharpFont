using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SlimFont {
    public class FontFace {
        Renderer renderer = new Renderer();
        GlyphData[] glyphs;
        MetricsEntry[] hmetrics;
        MetricsEntry[] vmetrics;
        int cellAscent;
        int cellDescent;
        int lineHeight;
        int xHeight;
        int capHeight;
        int underlineSize;
        int underlinePosition;
        int strikeoutSize;
        int strikeoutPosition;
        bool isFixedWidth;
        FontWeight weight;
        FontStretch stretch;
        FontStyle style;

        public int CellAscent => cellAscent;
        public int CellDescent => cellDescent;
        public int LineHeight => lineHeight;
        public int XHeight => xHeight;
        public int CapHeight => capHeight;
        public int UnderlineSize => underlineSize;
        public int UnderlinePosition => underlinePosition;
        public int StrikeoutSize => strikeoutSize;
        public int StrikeoutPosition => strikeoutPosition;
        public bool IsFixedWidth => isFixedWidth;
        public FontWeight Weight => weight;
        public FontStretch Stretch => stretch;
        public FontStyle Style => style;

        internal FontFace (
            int cellAscent, int cellDescent, int lineHeight, int xHeight, int capHeight, int underlineSize,
            int underlinePosition, int strikeoutSize, int strikeoutPosition, bool isFixedWidth,
            FontWeight weight, FontStretch stretch, FontStyle style, GlyphData[] glyphs,
            MetricsEntry[] hmetrics, MetricsEntry[] vmetrics
        ) {
            this.cellAscent = cellAscent;
            this.cellDescent = cellDescent;
            this.lineHeight = lineHeight;
            this.xHeight = xHeight;
            this.capHeight = capHeight;
            this.underlineSize = underlineSize;
            this.underlinePosition = underlinePosition;
            this.strikeoutSize = strikeoutSize;
            this.strikeoutPosition = strikeoutPosition;
            this.isFixedWidth = isFixedWidth;
            this.weight = weight;
            this.stretch = stretch;
            this.style = style;
            this.glyphs = glyphs;
            this.hmetrics = hmetrics;
            this.vmetrics = vmetrics;
        }

        public GlyphMetrics GetGlyphMetrics (int glyphIndex) {
            if (glyphIndex < 0 || glyphIndex >= glyphs.Length)
                throw new ArgumentOutOfRangeException(nameof(glyphIndex));

            // get horizontal metrics
            var horizontal = hmetrics[glyphIndex];

            //  get vertical metrics if we have them; otherwise synthesize them
            // TODO:

            return default(GlyphMetrics);
        }

        public void RenderGlyph (int glyphIndex, Surface surface) {
            if (glyphIndex < 0 || glyphIndex >= glyphs.Length)
                throw new ArgumentOutOfRangeException(nameof(glyphIndex));

            // get out all the junk we care about
            var glyphData = glyphs[glyphIndex];
            var outline = glyphData.Outline;
            var points = outline.Points;
            var contours = outline.ContourEndpoints;
            var types = outline.PointTypes;

            // check for an empty outline, which obviously results in an empty render
            if (points.Length <= 0 || contours.Length <= 0)
                return;

            // compute control box; we'll shift the glyph so that it's rendered
            // with its bottom corner at the bottom left of the target surface
            var cbox = FixedMath.ComputeControlBox(points);
            var shiftX = -cbox.MinX;
            var shiftY = -cbox.MinY;

            // shift down into integer pixel coordinates and clip
            // against the bounds of the passed in target surface
            cbox = FixedMath.Translate(cbox, shiftX, shiftY);
            var minX = Math.Max(cbox.MinX.IntPart, 0);
            var minY = Math.Max(cbox.MinY.IntPart, 0);
            var maxX = Math.Min(cbox.MaxX.IntPart, surface.Width);
            var maxY = Math.Min(cbox.MaxY.IntPart, surface.Height);

            // check if the entire thing was clipped
            if (maxX - minX <= 0 || maxY - minY <= 0)
                return;

            // prep the renderer
            renderer.Clear();
            renderer.SetBounds(minX, minY, maxX, maxY);
            renderer.SetOffset(shiftX, shiftY);

            // walk each contour of the outline and render it
            var firstIndex = 0;
            for (int i = 0; i < contours.Length; i++) {
                // decompose the contour into drawing commands
                var lastIndex = contours[i];
                DecomposeContour(renderer, firstIndex, lastIndex, points, types);

                // next contour starts where this one left off
                firstIndex = lastIndex + 1;
            }

            // blit the result to the target surface
            renderer.BlitTo(surface);
        }

        static void DecomposeContour (Renderer renderer, int firstIndex, int lastIndex, Point[] points, PointType[] types) {
            var pointIndex = firstIndex;
            var type = types[pointIndex];
            var start = points[pointIndex];
            var end = points[lastIndex];
            var control = start;

            // contours can't start with a cubic control point.
            if (type == PointType.Cubic)
                return;

            if (type == PointType.Quadratic) {
                // if first point is a control point, try using the last point
                if (types[lastIndex] == PointType.OnCurve) {
                    start = end;
                    lastIndex--;
                }
                else {
                    // if they're both control points, start at the middle
                    start.X = (start.X + end.X) / 2;
                    start.Y = (start.Y + end.Y) / 2;
                }
                pointIndex--;
            }

            // let's draw this contour
            renderer.MoveTo(start);

            var needClose = true;
            while (pointIndex < lastIndex) {
                var point = points[++pointIndex];
                switch (types[pointIndex]) {
                    case PointType.OnCurve:
                        renderer.LineTo(point);
                        break;

                    case PointType.Quadratic:
                        control = point;
                        var done = false;
                        while (pointIndex < lastIndex) {
                            var v = points[++pointIndex];
                            var t = types[pointIndex];
                            if (t == PointType.OnCurve) {
                                renderer.QuadraticCurveTo(control, v);
                                done = true;
                                break;
                            }

                            // this condition checks for garbage outlines
                            if (t != PointType.Quadratic)
                                return;

                            var middle = new Point((control.X + v.X) / 2, (control.Y + v.Y) / 2);
                            renderer.QuadraticCurveTo(control, middle);
                            control = v;
                        }

                        // if we hit this point, we're ready to close out the contour
                        if (!done) {
                            renderer.QuadraticCurveTo(control, start);
                            needClose = false;
                        }
                        break;

                    case PointType.Cubic:
                        throw new NotSupportedException();
                }
            }

            if (needClose)
                renderer.LineTo(start);
        }
    }

    public struct GlyphMetrics {

    }

    public struct Surface {
        public IntPtr Bits;
        public int Width;
        public int Height;
        public int Pitch;
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
