using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SlimFont {
    // handles loading font data from TrueType (ttf), OpenType (otf), and TrueTypeCollection (ttc) files
    public unsafe class TtfReader : IDisposable {
        Stream stream;
        DataReader reader;
        readonly uint[] faceOffsets;
        readonly bool leaveOpen;

        public int FontCount => faceOffsets.Length;

        public TtfReader (string filePath)
            : this(File.OpenRead(filePath)) {
        }

        public TtfReader (Stream stream, bool leaveOpen = false) {
            this.stream = stream;
            this.leaveOpen = leaveOpen;

            reader = new DataReader(stream);

            // read the file header; if we have a collection, we want to
            // figure out where all the different faces are in the file
            // if we don't have a collection, there's just one font in the file
            faceOffsets = ReadTTCHeader(reader) ?? new[] { 0u };
        }

        public FontFace LoadFace (int faceIndex = 0) {
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
            var faceHeader = new FaceHeader();
            SfntTables.ReadHead(reader, ref faceHeader);
            if (faceHeader.UnitsPerEm == 0)
                Error("Invalid 'head' table.");

            // random metrics are stuffed into the PostScript table
            if (SeekToTable(reader, records, FourCC.Post))
                SfntTables.ReadPost(reader, ref faceHeader);

            // 'OS/2' has a bunch of metrics in it

            // TODO: metrics tables

            // TODO: kerning and gasp

            // TODO: friendly names

            // TODO: style flags

            // TODO: embedded bitmaps

            // TODO: cmap

            // load glyphs if we have them
            if (SeekToTable(reader, records, FourCC.Glyf)) {
                // first load the max position table; it will tell us how many glyphs we have
                SeekToTable(reader, records, FourCC.Maxp, required: true);
                SfntTables.ReadMaxp(reader, ref faceHeader);
                if (faceHeader.GlyphCount > MaxGlyphs)
                    Error("Font contains too many glyphs.");

                // now read in the loca table, which tells us the byte offset of each glyph
                var loca = stackalloc uint[faceHeader.GlyphCount];
                SeekToTable(reader, records, FourCC.Loca, required: true);
                SfntTables.ReadLoca(reader, faceHeader.IndexFormat, loca, faceHeader.GlyphCount);

                // read in all glyphs
                SeekToTable(reader, records, FourCC.Glyf);
                var glyfOffset = reader.Position;
                var glyphTable = new GlyphData[faceHeader.GlyphCount];
                for (int i = 0; i < glyphTable.Length; i++)
                    ReadGlyph(reader, i, 0, glyphTable, glyfOffset, loca);
            }

            // build the final font face; all data has been copied
            // out of the font file so we can close it after this
            var face = new FontFace();
            return face;
        }

        public void Dispose () {
            if (stream != null) {
                if (!leaveOpen)
                    stream.Close();
                reader.Dispose();

                stream = null;
                reader = null;
            }
        }

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
