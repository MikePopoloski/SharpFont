using SharpFont;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpFont {
    // handles loading font data from TrueType (ttf), OpenType (otf), and TrueTypeCollection (ttc) files
    public unsafe class FontReader : IDisposable {
        readonly DataReader reader;
        readonly uint[] faceOffsets;

        public int FontCount => faceOffsets.Length;

        public FontReader (Stream stream) {
            reader = new DataReader(stream);

            // read the file header; if we have a collection, we want to
            // figure out where all the different faces are in the file
            // if we don't have a collection, there's just one font in the file
            faceOffsets = ReadTTCHeader(reader) ?? new[] { 0u };
        }

        public Typeface ReadFace (int faceIndex = 0) {
            if (faceIndex >= faceOffsets.Length)
                throw new ArgumentOutOfRangeException(nameof(faceIndex));

            // jump to the face offset table
            reader.Seek(faceOffsets[faceIndex]);
            var tag = reader.ReadUInt32BE();
            if (tag != TTFv1 && tag != TTFv2 && tag != FourCC.True)
                Error("Unknown or unsupported sfnt version.");

            var tableCount = reader.ReadUInt16BE();
            reader.Skip(6); // skip the rest of the header

            // read each font table descriptor
            var records = new TableRecord[tableCount];
            for (int i = 0; i < tableCount; i++) {
                records[i] = new TableRecord {
                    Tag = reader.ReadUInt32(),
                    CheckSum = reader.ReadUInt32BE(),
                    Offset = reader.ReadUInt32BE(),
                    Length = reader.ReadUInt32BE(),
                };
            }

            // read the face header
            SeekToTable(reader, records, FourCC.Head, required: true);
            FaceHeader faceHeader;
            SfntTables.ReadHead(reader, out faceHeader);
            if (faceHeader.UnitsPerEm == 0)
                Error("Invalid 'head' table.");

            // max position table has a bunch of limits defined in it
            SeekToTable(reader, records, FourCC.Maxp, required: true);
            SfntTables.ReadMaxp(reader, ref faceHeader);
            if (faceHeader.GlyphCount > MaxGlyphs)
                Error("Font contains too many glyphs.");

            // random junk is stuffed into the PostScript table
            if (SeekToTable(reader, records, FourCC.Post))
                SfntTables.ReadPost(reader, ref faceHeader);

            // horizontal metrics header
            SeekToTable(reader, records, FourCC.Hhea, required: true);
            var hMetrics = SfntTables.ReadMetricsHeader(reader);

            // horizontal metrics table
            SeekToTable(reader, records, FourCC.Hmtx, required: true);
            var horizontal = SfntTables.ReadMetricsTable(reader, faceHeader.GlyphCount, hMetrics.MetricCount);

            // font might optionally have vertical metrics
            MetricsEntry[] vertical = null;
            if (SeekToTable(reader, records, FourCC.Vhea)) {
                var vMetrics = SfntTables.ReadMetricsHeader(reader);

                SeekToTable(reader, records, FourCC.Vmtx, required: true);
                vertical = SfntTables.ReadMetricsTable(reader, faceHeader.GlyphCount, vMetrics.MetricCount);
            }

            // OS/2 table has even more metrics
            SeekToTable(reader, records, FourCC.OS_2, required: true);
            OS2Data os2Data;
            SfntTables.ReadOS2(reader, out os2Data);

            // read character-to-glyph mapping tables
            SeekToTable(reader, records, FourCC.Cmap, required: true);
            var cmap = CharacterMap.ReadCmap(reader);




            // TODO: HasOutline based on existence of glyf or CFF tables
            
            // TODO: kerning and gasp

            // TODO: friendly names

            // TODO: embedded bitmaps

            // load glyphs if we have them
            GlyphData[] glyphTable = null;
            if (SeekToTable(reader, records, FourCC.Glyf)) {
                // read in the loca table, which tells us the byte offset of each glyph
                var loca = stackalloc uint[faceHeader.GlyphCount];
                SeekToTable(reader, records, FourCC.Loca, required: true);
                SfntTables.ReadLoca(reader, faceHeader.IndexFormat, loca, faceHeader.GlyphCount);

                // read in all glyphs
                SeekToTable(reader, records, FourCC.Glyf);
                var glyfOffset = reader.Position;
                glyphTable = new GlyphData[faceHeader.GlyphCount];
                for (int i = 0; i < glyphTable.Length; i++)
                    ReadGlyph(reader, i, 0, glyphTable, glyfOffset, loca);
            }

            // metrics calculations: if the UseTypographicMetrics flag is set, then
            // we should use the sTypo*** data for line height calculation
            int cellAscent, cellDescent, lineHeight;
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
                    Math.Max(0, hMetrics.LineGap) + hMetrics.Ascender + Math.Abs(hMetrics.Descender),
                    cellAscent + cellDescent
                );
            }

            // give sane defaults for underline and strikeout data if missing
            var underlineSize = faceHeader.UnderlineThickness != 0 ?
                faceHeader.UnderlineThickness : (faceHeader.UnitsPerEm + 7) / 14;
            var underlinePosition = faceHeader.UnderlinePosition != 0 ?
                faceHeader.UnderlinePosition : -((faceHeader.UnitsPerEm + 5) / 10);
            var strikeoutSize = os2Data.StrikeoutSize != 0 ?
                os2Data.StrikeoutSize : underlineSize;
            var strikeoutPosition = os2Data.StrikeoutPosition != 0 ?
                os2Data.StrikeoutPosition : faceHeader.UnitsPerEm / 3;

            // build the final font face; all data has been copied
            // out of the font file so we can close it after this
            return new Typeface(
                cellAscent, cellDescent, lineHeight, os2Data.XHeight, os2Data.CapHeight,
                underlineSize, underlinePosition, strikeoutSize, strikeoutPosition,
                faceHeader.IsFixedPitch, os2Data.Weight, os2Data.Stretch, os2Data.Style,
                glyphTable, horizontal, vertical, cmap
            );
        }

        public void Dispose () => reader.Dispose();

        static uint[] ReadTTCHeader (DataReader reader) {
            var tag = reader.ReadUInt32();
            if (tag != FourCC.Ttcf)
                return null;

            // font file is a TrueType collection; read the TTC header
            reader.Skip(4);     // version number
            var count = reader.ReadUInt32BE();
            if (count == 0 || count > MaxFontsInCollection)
                Error("Invalid TTC header");

            var offsets = new uint[count];
            for (int i = 0; i < count; i++)
                offsets[i] = reader.ReadUInt32BE();

            return offsets;
        }

        static void ReadGlyph (DataReader reader, int glyphIndex, int recursionDepth, GlyphData[] glyphTable, uint glyfOffset, uint* loca) {
            // check if this glyph has already been loaded; this can happen
            // if we're recursively loading subglyphs as part of a composite
            if (glyphTable[glyphIndex] != null)
                return;

            // prevent bad font data from causing infinite recursion
            if (recursionDepth > MaxRecursion)
                Error("Bad font data; infinite composite recursion.");

            // seek to the right spot and load the header
            reader.Seek(glyfOffset + loca[glyphIndex]);
            var header = SfntTables.ReadGlyphHeader(reader);
            var contours = header.ContourCount;
            if (contours < -1 || contours > MaxContours)
                Error("Invalid number of contours for glyph.");

            // load metrics for this glyph

            if (contours == 0) {
            }
            else if (contours > 0) {
                // positive contours means a simple glyph
                var simple = SfntTables.ReadSimpleGlyph(reader, contours);
                glyphTable[glyphIndex] = new GlyphData {
                    Outline = simple.Outline,
                    Instructions = simple.Instructions
                };
            }
            else if (contours == -1) {
                // -1 means composite glyph
                var composite = SfntTables.ReadCompositeGlyph(reader);
                var subglyphs = composite.Subglyphs;

                // read each subglyph recrusively
                for (int i = 0; i < subglyphs.Length; i++) {
                    ReadGlyph(reader, subglyphs[i].Index, recursionDepth + 1, glyphTable, glyfOffset, loca);

                    // TODO
                }
            }
        }

        static void Error (string message) {
            throw new Exception(message);
        }

        static int MulFix (int a, int b) {
            var c = (long)a * b;
            c += 0x8000 + (c >> 63);
            return (int)(c >> 16);
        }

        static bool SeekToTable (DataReader reader, TableRecord[] records, FourCC tag, bool required = false) {
            var index = -1;
            for (int i = 0; i < records.Length; i++) {
                if (records[i].Tag == tag) {
                    index = i;
                    break;
                }
            }

            // check if we found the desired table and that it's not empty
            if (index == -1 || records[index].Length == 0) {
                if (required)
                    Error($"Missing or empty '{tag}' table.");
                return false;
            }

            // seek to the appropriate offset
            reader.Seek(records[index].Offset);
            return true;
        }

        struct TableRecord {
            public FourCC Tag;
            public uint CheckSum;
            public uint Offset;
            public uint Length;

            public override string ToString () => Tag.ToString();
        }

        const int MaxGlyphs = short.MaxValue;
        const int MaxContours = 256;
        const int MaxRecursion = 128;
        const int MaxFontsInCollection = 64;
        const uint TTFv1 = 0x10000;
        const uint TTFv2 = 0x20000;
    }

    class GlyphData {
        public GlyphOutline Outline;
        public byte[] Instructions;
    }
}
