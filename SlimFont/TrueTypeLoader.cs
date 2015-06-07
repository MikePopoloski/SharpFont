using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SlimFont {
    // handles loading font data from TrueType (ttf), OpenType (otf), and TrueTypeCollection (ttc) files
    public class TrueTypeLoader : IDisposable {
        Stream stream;
        DataReader reader;
        readonly uint[] faceOffsets;

        public int FontCount => faceOffsets.Length;

        public TrueTypeLoader (string filePath)
            : this(File.OpenRead(filePath)) {
        }

        public TrueTypeLoader (Stream stream) {
            this.stream = stream;
            reader = new DataReader(stream);

            // read the file header; if we have a collection, we want to
            // figure out where all the different faces are in the file
            // if we don't have a collection, there's just one font in the file
            faceOffsets = ReadTTCHeader() ?? new[] { 0u };
        }

        public void LoadFace (Surface surface, int faceIndex = 0) {
            if (faceIndex >= faceOffsets.Length)
                throw new ArgumentOutOfRangeException(nameof(faceIndex));

            // jump to the face offset table
            reader.Jump(faceOffsets[faceIndex]);
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

            // load glyphs if we have them
            var glyfIndex = FindTable(records, FourCC.Glyf);
            if (glyfIndex >= 0) {
                reader.Jump(records[glyfIndex].Offset);

                for (int i = 0; i < 270; i++)
                    LoadGlyph();

                var glyph = LoadGlyph();
                var renderer = new Renderer();
                renderer.Render(glyph, surface);
            }
        }

        public void Dispose () {
            if (stream != null) {
                stream.Close();
                reader.Dispose();

                stream = null;
                reader = null;
            }
        }

        uint[] ReadTTCHeader () {
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

        GlyphOutline LoadGlyph () {
            // load the header
            var header = new GlyphHeader {
                ContourCount = reader.ReadInt16BE(),
                MinX = reader.ReadInt16BE(),
                MinY = reader.ReadInt16BE(),
                MaxX = reader.ReadInt16BE(),
                MaxY = reader.ReadInt16BE()
            };

            // if contours is positive, we have a simple glyph
            if (header.ContourCount > 0) {
                // read contour endpoints
                var contours = new int[header.ContourCount];
                var lastEndpoint = reader.ReadUInt16BE();
                contours[0] = lastEndpoint;
                for (int i = 1; i < contours.Length; i++) {
                    var endpoint = reader.ReadUInt16BE();
                    contours[i] = endpoint;
                    if (contours[i] <= lastEndpoint)
                        Error("Glyph contour endpoints are unordered.");

                    lastEndpoint = endpoint;
                }

                // the last contour's endpoint is the number of points in the glyph
                var pointCount = lastEndpoint + 1;
                var points = new Point[pointCount];
                var types = new PointType[pointCount];

                // read instruction data
                var instructionLength = reader.ReadUInt16BE();
                reader.Skip(instructionLength);

                // read flags
                var flags = new GlyphFlags[pointCount];
                int flagIndex = 0;
                while (flagIndex < flags.Length) {
                    var f = (GlyphFlags)reader.ReadByte();
                    flags[flagIndex++] = f;

                    // if Repeat is set, this flag data is repeated n more times
                    if ((f & GlyphFlags.Repeat) != 0) {
                        var count = reader.ReadByte();
                        for (int i = 0; i < count; i++)
                            flags[flagIndex++] = f;
                    }
                }

                // Read points, first doing all X coordinates and then all Y coordinates.
                // The point packing is insane; coords are either 1 byte or 2; they're
                // deltas from previous point, and flags let you repeat identical points.
                var x = 0;
                for (int i = 0; i < points.Length; i++) {
                    var f = flags[i];
                    var delta = 0;

                    if ((f & GlyphFlags.ShortX) != 0) {
                        delta = reader.ReadByte();
                        if ((f & GlyphFlags.SameX) == 0)
                            delta = -delta;
                    }
                    else if ((f & GlyphFlags.SameX) == 0)
                        delta = reader.ReadInt16BE();

                    x += delta;
                    points[i].X = MulFix(x, 131072); // TODO
                }

                var y = 0;
                for (int i = 0; i < points.Length; i++) {
                    var f = flags[i];
                    var delta = 0;

                    if ((f & GlyphFlags.ShortY) != 0) {
                        delta = reader.ReadByte();
                        if ((f & GlyphFlags.SameY) == 0)
                            delta = -delta;
                    }
                    else if ((f & GlyphFlags.SameY) == 0)
                        delta = reader.ReadInt16BE();

                    y += delta;
                    points[i].Y = MulFix(y, 131072); //TODO
                    types[i] = (f & GlyphFlags.OnCurve) != 0 ? PointType.OnCurve : PointType.Quadratic;
                }

                return new GlyphOutline {
                    Points = points,
                    PointTypes = types,
                    ContourEndpoints = contours
                };
            }
            else {
                throw new Exception();
            }


            return default(GlyphOutline);
        }

        void Error (string message) {
            throw new Exception(message);
        }

        static int MulFix (int a, int b) {
            var c = (long)a * b;
            c += 0x8000 + (c >> 63);
            return (int)(c >> 16);
        }

        static int FindTable (TableRecord[] records, FourCC tag) {
            for (int i = 0; i < records.Length; i++) {
                if (records[i].Tag == tag) {
                    // zero-length table might as well not exist
                    if (records[i].Length == 0)
                        return -1;
                    return i;
                }
            }
            return -1;
        }

        struct TableRecord {
            public FourCC Tag;
            public uint CheckSum;
            public uint Offset;
            public uint Length;

            public override string ToString () => Tag.ToString();
        }

        struct GlyphHeader {
            public short ContourCount;
            public short MinX;
            public short MinY;
            public short MaxX;
            public short MaxY;
        }

        [Flags]
        enum GlyphFlags : byte {
            None = 0,
            OnCurve = 0x1,
            ShortX = 0x2,
            ShortY = 0x4,
            Repeat = 0x8,
            SameX = 0x10,
            SameY = 0x20
        }

        const int MaxFontsInCollection = 64;
        const uint TTFv1 = 0x10000;
        const uint TTFv2 = 0x20000;
    }
}
