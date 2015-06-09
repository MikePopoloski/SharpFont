using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SlimFont {
    // raw SFNT container table reading routines
    unsafe static class SfntTables {
        public static void ReadHead (DataReader reader, ref FaceHeader header) {
            // 'head' table contains global information for the font face
            // we only care about a few fields in it
            reader.Skip(sizeof(int) * 4);   // version, revision, checksum, magic number

            header.Flags = (HeadFlags)reader.ReadUInt16BE();
            header.UnitsPerEm = reader.ReadUInt16BE();

            // skip over created and modified times, bounding box,
            // deprecated style bits, direction hints, and size hints
            reader.Skip(sizeof(long) * 2 + sizeof(short) * 7);

            header.IndexFormat = (IndexFormat)reader.ReadInt16BE();
        }

        public static void ReadPost (DataReader reader, ref FaceHeader header) {
            // skip over version and italicAngle
            reader.Skip(sizeof(int) * 2);

            header.UnderlinePosition = reader.ReadInt16BE();
            header.UnderlineThickness = reader.ReadInt16BE();
            header.IsFixedPitch = reader.ReadUInt32BE() != 0;
        }

        public static void ReadMaxp (DataReader reader, ref FaceHeader header) {
            // we just want the number of glyphs
            reader.Skip(sizeof(int));
            header.GlyphCount = reader.ReadUInt16BE();
        }

        public static void ReadLoca (DataReader reader, IndexFormat format, uint* table, int count) {
            if (format == IndexFormat.Short) {
                // values are ushort, divided by 2, so we need to shift back
                for (int i = 0; i < count; i++)
                    *table++ = (uint)(reader.ReadUInt16BE() << 1);
            }
            else {
                for (int i = 0; i < count; i++)
                    *table++ = reader.ReadUInt32BE();
            }
        }

        public static GlyphHeader ReadGlyphHeader (DataReader reader) {
            return new GlyphHeader {
                ContourCount = reader.ReadInt16BE(),
                MinX = reader.ReadInt16BE(),
                MinY = reader.ReadInt16BE(),
                MaxX = reader.ReadInt16BE(),
                MaxY = reader.ReadInt16BE()
            };
        }

        public static SimpleGlyph ReadSimpleGlyph (DataReader reader, int contourCount) {
            // read contour endpoints
            var contours = new int[contourCount];
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
            var instructions = reader.ReadBytes(instructionLength);

            // read flags
            var flags = new SimpleGlyphFlags[pointCount];
            int flagIndex = 0;
            while (flagIndex < flags.Length) {
                var f = (SimpleGlyphFlags)reader.ReadByte();
                flags[flagIndex++] = f;

                // if Repeat is set, this flag data is repeated n more times
                if ((f & SimpleGlyphFlags.Repeat) != 0) {
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

                if ((f & SimpleGlyphFlags.ShortX) != 0) {
                    delta = reader.ReadByte();
                    if ((f & SimpleGlyphFlags.SameX) == 0)
                        delta = -delta;
                }
                else if ((f & SimpleGlyphFlags.SameX) == 0)
                    delta = reader.ReadInt16BE();

                x += delta;
                points[i].X = (F26Dot6)x;
            }

            var y = 0;
            for (int i = 0; i < points.Length; i++) {
                var f = flags[i];
                var delta = 0;

                if ((f & SimpleGlyphFlags.ShortY) != 0) {
                    delta = reader.ReadByte();
                    if ((f & SimpleGlyphFlags.SameY) == 0)
                        delta = -delta;
                }
                else if ((f & SimpleGlyphFlags.SameY) == 0)
                    delta = reader.ReadInt16BE();

                y += delta;
                points[i].Y = (F26Dot6)y;
                types[i] = (f & SimpleGlyphFlags.OnCurve) != 0 ? PointType.OnCurve : PointType.Quadratic;
            }

            return new SimpleGlyph {
                Outline = new GlyphOutline {
                    Points = points,
                    PointTypes = types,
                    ContourEndpoints = contours
                },
                Instructions = instructions
            };
        }

        public static CompositeGlyph ReadCompositeGlyph (DataReader reader) {
            // we need to keep reading glyphs for as long as
            // our flags tell us that there are more to read
            var subglyphs = new List<Subglyph>();

            CompositeGlyphFlags flags;
            do {
                flags = (CompositeGlyphFlags)reader.ReadUInt16BE();

                var subglyph = new Subglyph { Flags = flags };
                subglyph.Index = reader.ReadUInt16BE();

                // read in args; they vary in size based on flags
                if ((flags & CompositeGlyphFlags.ArgsAreWords) != 0) {
                    subglyph.Arg1 = reader.ReadUInt16BE();
                    subglyph.Arg2 = reader.ReadUInt16BE();
                }
                else {
                    subglyph.Arg1 = reader.ReadByte();
                    subglyph.Arg2 = reader.ReadByte();
                }

                // figure out the transform; we can either have no scale, a uniform
                // scale, two independent scales, or a full 2x2 transform matrix
                var transform = Matrix2x2.Identity;
                if ((flags & CompositeGlyphFlags.HaveScale) != 0) {
                    var scale = new F16Dot16((F2Dot14)reader.ReadInt16BE());
                    transform.m11 = scale;
                    transform.m22 = scale;
                }
                else if ((flags & CompositeGlyphFlags.HaveXYScale) != 0) {
                    transform.m11 = new F16Dot16((F2Dot14)reader.ReadInt16BE());
                    transform.m22 = new F16Dot16((F2Dot14)reader.ReadInt16BE());
                }
                else if ((flags & CompositeGlyphFlags.HaveTransform) != 0) {
                    transform.m11 = new F16Dot16((F2Dot14)reader.ReadInt16BE());
                    transform.m12 = new F16Dot16((F2Dot14)reader.ReadInt16BE());
                    transform.m21 = new F16Dot16((F2Dot14)reader.ReadInt16BE());
                    transform.m22 = new F16Dot16((F2Dot14)reader.ReadInt16BE());
                }

                subglyph.Transform = transform;
                subglyphs.Add(subglyph);

            } while ((flags & CompositeGlyphFlags.MoreComponents) != 0);

            var result = new CompositeGlyph { Subglyphs = subglyphs.ToArray() };

            // if we have instructions, read them now
            if ((flags & CompositeGlyphFlags.HaveInstructions) != 0) {
                var instructionLength = reader.ReadUInt16BE();
                result.Instructions = reader.ReadBytes(instructionLength);
            }

            return result;
        }

        static void Error (string message) {
            throw new Exception(message);
        }
    }

    struct FaceHeader {
        public HeadFlags Flags;
        public int UnitsPerEm;
        public IndexFormat IndexFormat;
        public int UnderlinePosition;
        public int UnderlineThickness;
        public bool IsFixedPitch;
        public int GlyphCount;
    }

    struct GlyphHeader {
        public short ContourCount;
        public short MinX;
        public short MinY;
        public short MaxX;
        public short MaxY;
    }

    struct SimpleGlyph {
        public GlyphOutline Outline;
        public byte[] Instructions;
    }

    struct Subglyph {
        public Matrix2x2 Transform;
        public CompositeGlyphFlags Flags;
        public int Index;
        public int Arg1;
        public int Arg2;
    }

    struct CompositeGlyph {
        public Subglyph[] Subglyphs;
        public byte[] Instructions;
    }

    [Flags]
    enum SimpleGlyphFlags {
        None = 0,
        OnCurve = 0x1,
        ShortX = 0x2,
        ShortY = 0x4,
        Repeat = 0x8,
        SameX = 0x10,
        SameY = 0x20
    }

    [Flags]
    enum CompositeGlyphFlags {
        None = 0,
        ArgsAreWords = 0x1,
        AresAreXYValues = 0x2,
        RoundXYToGrid = 0x4,
        HaveScale = 0x8,
        MoreComponents = 0x20,
        HaveXYScale = 0x40,
        HaveTransform = 0x80,
        HaveInstructions = 0x100,
        UseMetrics = 0x200
    }

    [Flags]
    enum HeadFlags {
        None = 0,
        SimpleBaseline = 0x1,
        SimpleLsb = 0x2,
        SizeDependentInstructions = 0x4,
        IntegerPpem = 0x8,
        InstructionsAlterAdvance = 0x10
    }

    enum IndexFormat {
        Short,
        Long
    }
}
