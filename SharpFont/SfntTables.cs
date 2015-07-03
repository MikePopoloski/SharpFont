using System;
using System.Collections.Generic;
using System.Numerics;

namespace SharpFont {
    // raw SFNT container table reading routines
    unsafe static class SfntTables {
        public static uint[] ReadTTCHeader (DataReader reader) {
            // read the file header; if we have a collection, we want to
            // figure out where all the different faces are in the file
            // if we don't have a collection, there's just one font in the file
            var tag = reader.ReadUInt32();
            if (tag != FourCC.Ttcf)
                return new[] { 0u };

            // font file is a TrueType collection; read the TTC header
            reader.Skip(4);     // version number
            var count = reader.ReadUInt32BE();
            if (count == 0 || count > MaxFontsInCollection)
                throw new InvalidFontException("Invalid TTC header");

            var offsets = new uint[count];
            for (int i = 0; i < count; i++)
                offsets[i] = reader.ReadUInt32BE();

            return offsets;
        }

        public static TableRecord[] ReadFaceHeader (DataReader reader) {
            var tag = reader.ReadUInt32BE();
            if (tag != TTFv1 && tag != TTFv2 && tag != FourCC.True)
                throw new InvalidFontException("Unknown or unsupported sfnt version.");

            var tableCount = reader.ReadUInt16BE();
            reader.Skip(6); // skip the rest of the header

            // read each font table descriptor
            var tables = new TableRecord[tableCount];
            for (int i = 0; i < tableCount; i++) {
                tables[i] = new TableRecord {
                    Tag = reader.ReadUInt32(),
                    CheckSum = reader.ReadUInt32BE(),
                    Offset = reader.ReadUInt32BE(),
                    Length = reader.ReadUInt32BE(),
                };
            }

            return tables;
        }

        public static FaceHeader ReadHead (DataReader reader, TableRecord[] tables) {
            SeekToTable(reader, tables, FourCC.Head, required: true);

            // 'head' table contains global information for the font face
            // we only care about a few fields in it
            reader.Skip(sizeof(int) * 4);   // version, revision, checksum, magic number

            var result = new FaceHeader {
                Flags = (HeadFlags)reader.ReadUInt16BE(),
                UnitsPerEm = reader.ReadUInt16BE()
            };
            if (result.UnitsPerEm == 0)
                throw new InvalidFontException("Invalid 'head' table.");

            // skip over created and modified times, bounding box,
            // deprecated style bits, direction hints, and size hints
            reader.Skip(sizeof(long) * 2 + sizeof(short) * 7);

            result.IndexFormat = (IndexFormat)reader.ReadInt16BE();

            return result;
        }

        public static void ReadMaxp (DataReader reader, TableRecord[] tables, ref FaceHeader header) {
            SeekToTable(reader, tables, FourCC.Maxp, required: true);

            // we just want the number of glyphs
            reader.Skip(sizeof(int));
            header.GlyphCount = reader.ReadUInt16BE();
            if (header.GlyphCount > MaxGlyphs)
                throw new InvalidFontException("Font contains too many glyphs.");
        }

        public static MetricsHeader ReadMetricsHeader (DataReader reader) {
            // skip over version
            reader.Skip(sizeof(int));

            var header = new MetricsHeader {
                Ascender = reader.ReadInt16BE(),
                Descender = reader.ReadInt16BE(),
                LineGap = reader.ReadInt16BE()
            };

            // skip over advanceWidthMax, minLsb, minRsb, xMaxExtent, caretSlopeRise,
            // caretSlopeRun, caretOffset, 4 reserved entries, and metricDataFormat
            reader.Skip(sizeof(short) * 12);

            header.MetricCount = reader.ReadUInt16BE();
            return header;
        }

        public static MetricsEntry[] ReadMetricsTable (DataReader reader, int glyphCount, int metricCount) {
            var results = new MetricsEntry[glyphCount];
            for (int i = 0; i < metricCount; i++) {
                results[i] = new MetricsEntry {
                    Advance = reader.ReadUInt16BE(),
                    FrontSideBearing = reader.ReadInt16BE()
                };
            }

            // there might be an additional array of fsb-only entries
            var extraCount = glyphCount - metricCount;
            var lastAdvance = results[metricCount - 1].Advance;
            for (int i = 0; i < extraCount; i++) {
                results[i + metricCount] = new MetricsEntry {
                    Advance = lastAdvance,
                    FrontSideBearing = reader.ReadInt16BE()
                };
            }

            return results;
        }

        public static OS2Data ReadOS2 (DataReader reader, TableRecord[] tables) {
            SeekToTable(reader, tables, FourCC.OS_2, required: true);

            // skip over version, xAvgCharWidth
            reader.Skip(sizeof(short) * 2);

            var result = new OS2Data {
                Weight = (FontWeight)reader.ReadUInt16BE(),
                Stretch = (FontStretch)reader.ReadUInt16BE()
            };

            // skip over fsType, ySubscriptXSize, ySubscriptYSize, ySubscriptXOffset, ySubscriptYOffset,
            // ySuperscriptXSize, ySuperscriptYSize, ySuperscriptXOffset, ySuperscriptXOffset
            reader.Skip(sizeof(short) * 9);

            result.StrikeoutSize = reader.ReadInt16BE();
            result.StrikeoutPosition = reader.ReadInt16BE();

            // skip over sFamilyClass, panose[10], ulUnicodeRange1-4, achVendID[4]
            reader.Skip(sizeof(short) + sizeof(int) * 4 + 14);

            // check various style flags
            var fsSelection = (FsSelectionFlags)reader.ReadUInt16BE();
            result.Style = (fsSelection & FsSelectionFlags.Italic) != 0 ? FontStyle.Italic :
                            (fsSelection & FsSelectionFlags.Bold) != 0 ? FontStyle.Bold :
                            (fsSelection & FsSelectionFlags.Oblique) != 0 ? FontStyle.Oblique :
                            FontStyle.Regular;
            result.IsWWSFont = (fsSelection & FsSelectionFlags.WWS) != 0;
            result.UseTypographicMetrics = (fsSelection & FsSelectionFlags.UseTypoMetrics) != 0;

            // skip over usFirstCharIndex, usLastCharIndex
            reader.Skip(sizeof(short) * 2);

            result.TypographicAscender = reader.ReadInt16BE();
            result.TypographicDescender = reader.ReadInt16BE();
            result.TypographicLineGap = reader.ReadInt16BE();
            result.WinAscent = reader.ReadUInt16BE();
            result.WinDescent = reader.ReadUInt16BE();

            // skip over ulCodePageRange1-2
            reader.Skip(sizeof(int) * 2);

            result.XHeight = reader.ReadInt16BE();
            result.CapHeight = reader.ReadInt16BE();

            return result;
        }

        public static void ReadPost (DataReader reader, TableRecord[] tables, ref FaceHeader header) {
            if (!SeekToTable(reader, tables, FourCC.Post))
                return;

            // skip over version and italicAngle
            reader.Skip(sizeof(int) * 2);

            header.UnderlinePosition = reader.ReadInt16BE();
            header.UnderlineThickness = reader.ReadInt16BE();
            header.IsFixedPitch = reader.ReadUInt32BE() != 0;
        }

        public static void ReadLoca (DataReader reader, TableRecord[] tables, IndexFormat format, uint* table, int count) {
            SeekToTable(reader, tables, FourCC.Loca, required: true);

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

        public static int FindTable (TableRecord[] records, FourCC tag) {
            var index = -1;
            for (int i = 0; i < records.Length; i++) {
                if (records[i].Tag == tag) {
                    index = i;
                    break;
                }
            }

            return index;
        }

        public static bool SeekToTable (DataReader reader, TableRecord[] records, FourCC tag, bool required = false) {
            // check if we have the desired table and that it's not empty
            var index = FindTable(records, tag);
            if (index == -1 || records[index].Length == 0) {
                if (required)
                    throw new InvalidFontException($"Missing or empty '{tag}' table.");
                return false;
            }

            // seek to the appropriate offset
            reader.Seek(records[index].Offset);
            return true;
        }

        public static void ReadGlyph (
            DataReader reader, int glyphIndex, int recursionDepth,
            BaseGlyph[] glyphTable, uint glyfOffset, uint glyfLength, uint* loca
        ) {
            // check if this glyph has already been loaded; this can happen
            // if we're recursively loading subglyphs as part of a composite
            if (glyphTable[glyphIndex] != null)
                return;

            // prevent bad font data from causing infinite recursion
            if (recursionDepth > MaxRecursion)
                throw new InvalidFontException("Bad font data; infinite composite recursion.");

            // check if this glyph doesn't have any actual data
            GlyphHeader header;
            var offset = loca[glyphIndex];
            if ((glyphIndex < glyphTable.Length - 1 && offset == loca[glyphIndex + 1]) || offset >= glyfLength) {
                // this is an empty glyph, so synthesize a header
                header = default(GlyphHeader);
            }
            else {
                // seek to the right spot and load the header
                reader.Seek(glyfOffset + loca[glyphIndex]);
                header = new GlyphHeader {
                    ContourCount = reader.ReadInt16BE(),
                    MinX = reader.ReadInt16BE(),
                    MinY = reader.ReadInt16BE(),
                    MaxX = reader.ReadInt16BE(),
                    MaxY = reader.ReadInt16BE()
                };
                
                if (header.ContourCount < -1 || header.ContourCount > MaxContours)
                    throw new InvalidFontException("Invalid number of contours for glyph.");
            }

            if (header.ContourCount > 0) {
                // positive contours means a simple glyph
                glyphTable[glyphIndex] = ReadSimpleGlyph(reader, header.ContourCount);
            }
            else if (header.ContourCount == -1) {
                // -1 means composite glyph
                var composite = ReadCompositeGlyph(reader);
                var subglyphs = composite.Subglyphs;

                // read each subglyph recrusively
                for (int i = 0; i < subglyphs.Length; i++)
                    ReadGlyph(reader, subglyphs[i].Index, recursionDepth + 1, glyphTable, glyfOffset, glyfLength, loca);

                glyphTable[glyphIndex] = composite;
            }
            else {
                // no data, so synthesize an empty glyph
                glyphTable[glyphIndex] = new SimpleGlyph {
                    Points = new Point[0],
                    ContourEndpoints = new int[0]
                };
            }

            // save bounding box
            var glyph = glyphTable[glyphIndex];
            glyph.MinX = header.MinX;
            glyph.MinY = header.MinY;
            glyph.MaxX = header.MaxX;
            glyph.MaxY = header.MaxY;
        }

        static SimpleGlyph ReadSimpleGlyph (DataReader reader, int contourCount) {
            // read contour endpoints
            var contours = new int[contourCount];
            var lastEndpoint = reader.ReadUInt16BE();
            contours[0] = lastEndpoint;
            for (int i = 1; i < contours.Length; i++) {
                var endpoint = reader.ReadUInt16BE();
                contours[i] = endpoint;
                if (contours[i] <= lastEndpoint)
                    throw new InvalidFontException("Glyph contour endpoints are unordered.");

                lastEndpoint = endpoint;
            }

            // the last contour's endpoint is the number of points in the glyph
            var pointCount = lastEndpoint + 1;
            var points = new Point[pointCount];

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
                points[i].X = (FUnit)x;
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
                points[i].Y = (FUnit)y;
                points[i].Type = (f & SimpleGlyphFlags.OnCurve) != 0 ? PointType.OnCurve : PointType.Quadratic;
            }

            return new SimpleGlyph {
                Points = points,
                ContourEndpoints = contours,
                Instructions = instructions
            };
        }

        static CompositeGlyph ReadCompositeGlyph (DataReader reader) {
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
                // transform components are in 2.14 fixed point format
                var transform = Matrix3x2.Identity;
                if ((flags & CompositeGlyphFlags.HaveScale) != 0) {
                    var scale = (float)(F2Dot14)reader.ReadInt16BE();
                    transform.M11 = scale;
                    transform.M22 = scale;
                }
                else if ((flags & CompositeGlyphFlags.HaveXYScale) != 0) {
                    transform.M11 = (float)(F2Dot14)reader.ReadInt16BE();
                    transform.M22 = (float)(F2Dot14)reader.ReadInt16BE();
                }
                else if ((flags & CompositeGlyphFlags.HaveTransform) != 0) {
                    transform.M11 = (float)(F2Dot14)reader.ReadInt16BE();
                    transform.M12 = (float)(F2Dot14)reader.ReadInt16BE();
                    transform.M21 = (float)(F2Dot14)reader.ReadInt16BE();
                    transform.M22 = (float)(F2Dot14)reader.ReadInt16BE();
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

        const uint TTFv1 = 0x10000;
        const uint TTFv2 = 0x20000;
        const int MaxGlyphs = short.MaxValue;
        const int MaxContours = 256;
        const int MaxRecursion = 128;
        const int MaxFontsInCollection = 64;
    }

    struct TableRecord {
        public FourCC Tag;
        public uint CheckSum;
        public uint Offset;
        public uint Length;

        public override string ToString () => Tag.ToString();
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

    struct MetricsHeader {
        public int Ascender;
        public int Descender;
        public int LineGap;
        public int MetricCount;
    }

    struct MetricsEntry {
        public int Advance;
        public int FrontSideBearing;
    }

    struct OS2Data {
        public FontWeight Weight;
        public FontStretch Stretch;
        public FontStyle Style;
        public int StrikeoutSize;
        public int StrikeoutPosition;
        public int TypographicAscender;
        public int TypographicDescender;
        public int TypographicLineGap;
        public int WinAscent;
        public int WinDescent;
        public bool UseTypographicMetrics;
        public bool IsWWSFont;
        public int XHeight;
        public int CapHeight;
    }

    struct GlyphHeader {
        public short ContourCount;
        public short MinX;
        public short MinY;
        public short MaxX;
        public short MaxY;
    }

    abstract class BaseGlyph {
        public byte[] Instructions;
        public int MinX;
        public int MinY;
        public int MaxX;
        public int MaxY;
    }

    class SimpleGlyph : BaseGlyph {
        public Point[] Points;
        public int[] ContourEndpoints;
    }

    struct Subglyph {
        public Matrix3x2 Transform;
        public CompositeGlyphFlags Flags;
        public int Index;
        public int Arg1;
        public int Arg2;
    }

    class CompositeGlyph : BaseGlyph {
        public Subglyph[] Subglyphs;
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
        ArgsAreXYValues = 0x2,
        RoundXYToGrid = 0x4,
        HaveScale = 0x8,
        MoreComponents = 0x20,
        HaveXYScale = 0x40,
        HaveTransform = 0x80,
        HaveInstructions = 0x100,
        UseMetrics = 0x200,
        ScaledComponentOffset = 0x800
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

    [Flags]
    enum FsSelectionFlags {
        Italic = 0x1,
        Bold = 0x20,
        Regular = 0x40,
        UseTypoMetrics = 0x80,
        WWS = 0x100,
        Oblique = 0x200
    }

    enum IndexFormat {
        Short,
        Long
    }

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
        public static readonly FourCC Maxp = "maxp";
        public static readonly FourCC Post = "post";
        public static readonly FourCC OS_2 = "OS/2";
        public static readonly FourCC Hhea = "hhea";
        public static readonly FourCC Hmtx = "hmtx";
        public static readonly FourCC Vhea = "vhea";
        public static readonly FourCC Vmtx = "vmtx";
        public static readonly FourCC Loca = "loca";
        public static readonly FourCC Glyf = "glyf";
        public static readonly FourCC Cmap = "cmap";
    }
}
