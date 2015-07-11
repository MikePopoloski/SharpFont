using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;

namespace SharpFont {
    public class FontFace {
        readonly Renderer renderer = new Renderer();
        readonly Interpreter interpreter;
        readonly BaseGlyph[] glyphs;
        readonly MetricsEntry[] hmetrics;
        readonly MetricsEntry[] vmetrics;
        readonly CharacterMap charMap;
        readonly KerningTable kernTable;
        readonly MetricsEntry verticalSynthesized;
        readonly FUnit[] controlValueTable;
        readonly byte[] prepProgram;
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
        readonly bool integerPpems;

        public readonly bool IsFixedWidth;
        public readonly FontWeight Weight;
        public readonly FontStretch Stretch;
        public readonly FontStyle Style;
        public readonly string Family;
        public readonly string Subfamily;
        public readonly string FullName;
        public readonly string UniqueID;
        public readonly string Version;
        public readonly string Description;

        unsafe public FontFace (Stream stream) {
            // read the face header and table records
            using (var reader = new DataReader(stream)) {
                var tables = SfntTables.ReadFaceHeader(reader);

                // read head and maxp tables for font metadata and limits
                FaceHeader head;
                SfntTables.ReadHead(reader, tables, out head);
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
                Weight = os2Data.Weight;
                Stretch = os2Data.Stretch;
                Style = os2Data.Style;

                // optional PostScript table has random junk in it
                SfntTables.ReadPost(reader, tables, ref head);
                IsFixedWidth = head.IsFixedPitch;

                // read character-to-glyph mapping tables and kerning table
                charMap = CharacterMap.ReadCmap(reader, tables);
                kernTable = KerningTable.ReadKern(reader, tables);

                // name data
                var names = SfntTables.ReadNames(reader, tables);
                Family = names.TypographicFamilyName ?? names.FamilyName;
                Subfamily = names.TypographicSubfamilyName ?? names.SubfamilyName;
                FullName = names.FullName;
                UniqueID = names.UniqueID;
                Version = names.Version;
                Description = names.Description;

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

                // create some vertical metrics in case we haven't loaded any
                verticalSynthesized = new MetricsEntry {
                    FrontSideBearing = os2Data.TypographicAscender,
                    Advance = os2Data.TypographicAscender - os2Data.TypographicDescender
                };

                // read in global font program data
                controlValueTable = SfntTables.ReadCvt(reader, tables);
                prepProgram = SfntTables.ReadProgram(reader, tables, FourCC.Prep);
                interpreter = new Interpreter(head.MaxStackSize, head.MaxStorageLocations, head.MaxFunctionDefs);

                // the fpgm table optionally contains a program to run at initialization time
                var fpgm = SfntTables.ReadProgram(reader, tables, FourCC.Fpgm);
                if (fpgm != null)
                    interpreter.Execute(fpgm);
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

            // set up the control value table
            var scale = ComputeScale(pixelSize);
            interpreter.SetControlValueTable(controlValueTable, scale, pixelSize, prepProgram);

            // get metrics
            var glyph = glyphs[glyphIndex];
            var horizontal = hmetrics[glyphIndex];
            var vtemp = vmetrics?[glyphIndex];
            if (vtemp == null) {
                var synth = verticalSynthesized;
                synth.FrontSideBearing -= glyph.MaxY;
                vtemp = synth;
            }
            var vertical = vtemp.GetValueOrDefault();

            // build and transform the glyph
            var points = new List<PointF>(32);
            var contours = new List<int>(32);
            var transform = Matrix3x2.CreateScale(scale);
            Geometry.ComposeGlyphs(glyphIndex, 0, ref transform, points, contours, glyphs);

            // add phantom points; these are used to define the extents of the glyph,
            // and can be modified by hinting instructions
            var pp1 = new Point((FUnit)(glyph.MinX - horizontal.FrontSideBearing), (FUnit)0);
            var pp2 = new Point(pp1.X + (FUnit)horizontal.Advance, (FUnit)0);
            var pp3 = new Point((FUnit)0, (FUnit)(glyph.MaxY + vertical.FrontSideBearing));
            var pp4 = new Point((FUnit)0, pp3.Y - (FUnit)vertical.Advance);
            points.Add(pp1 * scale);
            points.Add(pp2 * scale);
            points.Add(pp3 * scale);
            points.Add(pp4 * scale);

            return new Glyph(renderer, points.ToArray(), contours.ToArray());
        }

        public float GetKerning (CodePoint left, CodePoint right, float pixelSize) {
            if (kernTable == null)
                return 0.0f;

            var leftIndex = charMap.Lookup(left);
            var rightIndex = charMap.Lookup(right);
            if (leftIndex < 0 || rightIndex < 0)
                return 0.0f;

            var kern = kernTable.Lookup(leftIndex, rightIndex);
            return kern * ComputeScale(pixelSize);
        }

        float ComputeScale (float pixelSize) {
            if (integerPpems)
                pixelSize = (float)Math.Round(pixelSize);
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
