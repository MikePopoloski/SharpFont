using SharpFont;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
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


            faceOffsets = SfntTables.ReadTTCHeader(reader);
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
            SfntTables.SeekToTable(reader, records, FourCC.Head, required: true);
            FaceHeader faceHeader;
            SfntTables.ReadHead(reader, out faceHeader);
            if (faceHeader.UnitsPerEm == 0)
                Error("Invalid 'head' table.");

            // max position table has a bunch of limits defined in it
            SfntTables.SeekToTable(reader, records, FourCC.Maxp, required: true);
            SfntTables.ReadMaxp(reader, ref faceHeader);

            // random junk is stuffed into the PostScript table
            if (SfntTables.SeekToTable(reader, records, FourCC.Post))
                SfntTables.ReadPost(reader, ref faceHeader);

            // horizontal metrics header
            SfntTables.SeekToTable(reader, records, FourCC.Hhea, required: true);
            var hMetrics = SfntTables.ReadMetricsHeader(reader);

            // horizontal metrics table
            SfntTables.SeekToTable(reader, records, FourCC.Hmtx, required: true);
            var horizontal = SfntTables.ReadMetricsTable(reader, faceHeader.GlyphCount, hMetrics.MetricCount);

            // font might optionally have vertical metrics
            MetricsEntry[] vertical = null;
            if (SfntTables.SeekToTable(reader, records, FourCC.Vhea)) {
                var vMetrics = SfntTables.ReadMetricsHeader(reader);

                SfntTables.SeekToTable(reader, records, FourCC.Vmtx, required: true);
                vertical = SfntTables.ReadMetricsTable(reader, faceHeader.GlyphCount, vMetrics.MetricCount);
            }

            // OS/2 table has even more metrics
            SfntTables.SeekToTable(reader, records, FourCC.OS_2, required: true);
            OS2Data os2Data;
            SfntTables.ReadOS2(reader, out os2Data);

            // read character-to-glyph mapping tables
            SfntTables.SeekToTable(reader, records, FourCC.Cmap, required: true);
            var cmap = CharacterMap.ReadCmap(reader);




            // TODO: HasOutline based on existence of glyf or CFF tables

            // TODO: kerning and gasp

            // TODO: friendly names

            // TODO: embedded bitmaps

            // load glyphs if we have them
            BaseGlyph[] glyphTable = null;
            if (SfntTables.SeekToTable(reader, records, FourCC.Glyf)) {
                // read in the loca table, which tells us the byte offset of each glyph
                var loca = stackalloc uint[faceHeader.GlyphCount];
                SfntTables.SeekToTable(reader, records, FourCC.Loca, required: true);
                SfntTables.ReadLoca(reader, faceHeader.IndexFormat, loca, faceHeader.GlyphCount);

                // we need to know the length of the glyf table because of some weirdness in the loca table:
                // if a glyph is "missing" (like a space character), then its loca[n] entry is equal to loca[n+1]
                // if the last glyph in the set is missing, then loca[n] == glyf table length
                SfntTables.SeekToTable(reader, records, FourCC.Glyf);
                var glyfOffset = reader.Position;
                var glyfLength = records[SfntTables.FindTable(records, FourCC.Glyf)].Length;

                // read in all glyphs
                glyphTable = new BaseGlyph[faceHeader.GlyphCount];
                for (int i = 0; i < glyphTable.Length; i++)
                    SfntTables.ReadGlyph(reader, i, 0, glyphTable, glyfOffset, glyfLength, loca);
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
                faceHeader.UnitsPerEm, cellAscent, cellDescent, lineHeight, os2Data.XHeight,
                os2Data.CapHeight, underlineSize, underlinePosition, strikeoutSize,
                strikeoutPosition, os2Data.Weight, os2Data.Stretch, os2Data.Style,
                glyphTable, horizontal, vertical, cmap, faceHeader.IsFixedPitch,
                (faceHeader.Flags & HeadFlags.IntegerPpem) != 0
            );
        }

        public void Dispose () => reader.Dispose();

        

        

        static void Error (string message) {
            throw new Exception(message);
        }

        

        

        
        
        const uint TTFv1 = 0x10000;
        const uint TTFv2 = 0x20000;
    }
}
