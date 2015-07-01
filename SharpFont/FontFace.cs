using SharpFont;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace SharpFont {
    public class FontFace {
        readonly Renderer renderer = new Renderer();
        readonly BaseGlyph[] glyphs;
        readonly MetricsEntry[] hmetrics;
        readonly MetricsEntry[] vmetrics;
        readonly CharacterMap charMap;
        readonly FontWeight weight;
        readonly FontStretch stretch;
        readonly FontStyle style;
        readonly int cellAscent;
        readonly int cellDescent;
        readonly int lineHeight;
        readonly int xHeight;
        readonly int capHeight;
        readonly int underlineSize;
        readonly int underlinePosition;
        readonly int strikeoutSize;
        readonly int strikeoutPosition;
        readonly int unitsPerEm;
        readonly bool isFixedWidth;
        readonly bool integerPpems;

        public bool IsFixedWidth => isFixedWidth;
        public FontWeight Weight => weight;
        public FontStretch Stretch => stretch;
        public FontStyle Style => style;

        unsafe public FontFace (Stream stream) {
            // read the face header and table records
            using (var reader = new DataReader(stream)) {
                var tables = SfntTables.ReadFaceHeader(reader);

                // read head and maxp tables for font metadata and limits
                var head = SfntTables.ReadHead(reader, tables);
                SfntTables.ReadMaxp(reader, tables, ref head);
                unitsPerEm = head.UnitsPerEm;
                integerPpems = (head.Flags & HeadFlags.IntegerPpem) != 0;

                // horizontal metrics header and data
                SfntTables.SeekToTable(reader, tables, FourCC.Hhea, required: true);
                var hMetricsHeader = SfntTables.ReadMetricsHeader(reader);
                SfntTables.SeekToTable(reader, tables, FourCC.Hmtx, required: true);
                hmetrics = SfntTables.ReadMetricsTable(reader, head.GlyphCount, hMetricsHeader.MetricCount);

                // font might optionally have vertical metrics
                if (SfntTables.SeekToTable(reader, tables, FourCC.Vhea)) {
                    var vMetricsHeader = SfntTables.ReadMetricsHeader(reader);

                    SfntTables.SeekToTable(reader, tables, FourCC.Vmtx, required: true);
                    vmetrics = SfntTables.ReadMetricsTable(reader, head.GlyphCount, vMetricsHeader.MetricCount);
                }

                // OS/2 table has even more metrics
                var os2Data = SfntTables.ReadOS2(reader, tables);
                xHeight = os2Data.XHeight;
                capHeight = os2Data.CapHeight;
                weight = os2Data.Weight;
                stretch = os2Data.Stretch;
                style = os2Data.Style;

                // optional PostScript table has random junk in it
                SfntTables.ReadPost(reader, tables, ref head);
                isFixedWidth = head.IsFixedPitch;

                // read character-to-glyph mapping tables
                charMap = CharacterMap.ReadCmap(reader, tables);

                // load glyphs if we have them
                if (SfntTables.SeekToTable(reader, tables, FourCC.Glyf)) {
                    // read in the loca table, which tells us the byte offset of each glyph
                    var loca = stackalloc uint[head.GlyphCount];
                    SfntTables.ReadLoca(reader, tables, head.IndexFormat, loca, head.GlyphCount);

                    // we need to know the length of the glyf table because of some weirdness in the loca table:
                    // if a glyph is "missing" (like a space character), then its loca[n] entry is equal to loca[n+1]
                    // if the last glyph in the set is missing, then loca[n] == glyf table length
                    SfntTables.SeekToTable(reader, tables, FourCC.Glyf);
                    var glyfOffset = reader.Position;
                    var glyfLength = tables[SfntTables.FindTable(tables, FourCC.Glyf)].Length;

                    // read in all glyphs
                    glyphs = new BaseGlyph[head.GlyphCount];
                    for (int i = 0; i < glyphs.Length; i++)
                        SfntTables.ReadGlyph(reader, i, 0, glyphs, glyfOffset, glyfLength, loca);
                }

                // metrics calculations: if the UseTypographicMetrics flag is set, then
                // we should use the sTypo*** data for line height calculation
                if (os2Data.UseTypographicMetrics) {
                    // include the line gap in the ascent so that
                    // white space is distributed above the line
                    cellAscent = os2Data.TypographicAscender + os2Data.TypographicLineGap;
                    cellDescent = -os2Data.TypographicDescender;
                    lineHeight = os2Data.TypographicAscender + os2Data.TypographicLineGap - os2Data.TypographicDescender;
                }
                else {
                    // otherwise, we need to guess at whether hhea data or os/2 data has better line spacing
                    // this is the recommended procedure based on the OS/2 spec extra notes
                    cellAscent = os2Data.WinAscent;
                    cellDescent = Math.Abs(os2Data.WinDescent);
                    lineHeight = Math.Max(
                        Math.Max(0, hMetricsHeader.LineGap) + hMetricsHeader.Ascender + Math.Abs(hMetricsHeader.Descender),
                        cellAscent + cellDescent
                    );
                }

                // give sane defaults for underline and strikeout data if missing
                underlineSize = head.UnderlineThickness != 0 ?
                    head.UnderlineThickness : (head.UnitsPerEm + 7) / 14;
                underlinePosition = head.UnderlinePosition != 0 ?
                    head.UnderlinePosition : -((head.UnitsPerEm + 5) / 10);
                strikeoutSize = os2Data.StrikeoutSize != 0 ?
                    os2Data.StrikeoutSize : underlineSize;
                strikeoutPosition = os2Data.StrikeoutPosition != 0 ?
                    os2Data.StrikeoutPosition : head.UnitsPerEm / 3;
            }
        }

        public static float ComputePixelSize (float pointSize, int dpi) => pointSize * dpi / 72;

        public FaceMetrics GetFaceMetrics (float pixelSize) {
            var scale = ComputeScale(pixelSize);
            return new FaceMetrics(
                cellAscent * scale,
                cellDescent * scale,
                lineHeight * scale,
                xHeight * scale,
                capHeight * scale,
                underlineSize * scale,
                underlinePosition * scale,
                strikeoutSize * scale,
                strikeoutPosition * scale
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

            // build and transform the glyph
            var points = new List<PointF>(32);
            var contours = new List<int>(32);
            var transform = Matrix3x2.CreateScale(ComputeScale(pixelSize));
            ComposeGlyphs(glyphs[glyphIndex], 0, ref transform, points, contours);

            return new Glyph(renderer, points.ToArray(), contours.ToArray());

            //var glyphData = glyphs[glyphIndex];
            //var outline = glyphData.Outline;
            //var points = outline.Points;
            //// TODO: don't round the control box
            //var cbox = FixedMath.ComputeControlBox(points);

            //return new Glyph(
            //    glyphData,
            //    renderer,
            //    (int)cbox.MinX * scale,
            //    (int)cbox.MaxY * scale,
            //    (int)(cbox.MaxX - cbox.MinX) * scale,
            //    (int)(cbox.MaxY - cbox.MinY) * scale,
            //    horizontal.Advance * scale
            //);
        }

        void ComposeGlyphs (BaseGlyph glyph, int startPoint, ref Matrix3x2 transform, List<PointF> basePoints, List<int> baseContours) {
            var simple = glyph as SimpleGlyph;
            if (simple != null) {
                baseContours.AddRange(simple.Outline.ContourEndpoints);
                foreach (var point in simple.Outline.Points)
                    basePoints.Add(new PointF(Vector2.TransformNormal((Vector2)point, transform), point.Type));
            }
            else {
                // otherwise, we have a composite glyph
                var composite = (CompositeGlyph)glyph;
                foreach (var subglyph in composite.Subglyphs) {
                    // if we have a scale, update the local transform
                    var local = transform;
                    bool haveScale = (subglyph.Flags & (CompositeGlyphFlags.HaveScale | CompositeGlyphFlags.HaveXYScale | CompositeGlyphFlags.HaveTransform)) != 0;
                    if (haveScale)
                        local = transform * subglyph.Transform;

                    // recursively compose the subglyph into our lists
                    int currentPoints = basePoints.Count;
                    ComposeGlyphs(glyphs[subglyph.Index], currentPoints, ref local, basePoints, baseContours);

                    // calculate the offset for the subglyph. we have to do offsetting after composing all subglyphs,
                    // because we might need to find the offset based on previously composed points by index
                    Vector2 offset;
                    if ((subglyph.Flags & CompositeGlyphFlags.ArgsAreXYValues) != 0) {
                        offset = (Vector2)new Point((FUnit)subglyph.Arg1, (FUnit)subglyph.Arg2);
                        if (haveScale && (subglyph.Flags & CompositeGlyphFlags.ScaledComponentOffset) != 0)
                            offset = Vector2.TransformNormal(offset, local);
                        else
                            offset = Vector2.TransformNormal(offset, transform);

                        // if the RoundXYToGrid flag is set, round the offset components
                        if ((subglyph.Flags & CompositeGlyphFlags.RoundXYToGrid) != 0)
                            offset = new Vector2((float)Math.Round(offset.X), (float)Math.Round(offset.Y));
                    }
                    else {
                        // if the offsets are not given in FUnits, then they are point indices
                        // in the currently composed base glyph that we should match up
                        var p1 = basePoints[subglyph.Arg1 + startPoint];
                        var p2 = basePoints[subglyph.Arg2 + currentPoints];
                        offset = p1.P - p2.P;
                    }

                    // translate all child points
                    if (offset != Vector2.Zero) {
                        //for (int i = currentPoints; i < basePoints.Count; i++)
                        //  basePoints[i].Offset(offset);
                    }
                }
            }
        }

        float ComputeScale (float pixelSize) {
            if (integerPpems)
                pixelSize = (float)Math.Round(pixelSize, MidpointRounding.AwayFromZero);
            return pixelSize / unitsPerEm;
        }
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

        public override string ToString () => $"{value} ({(char)value})";

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
