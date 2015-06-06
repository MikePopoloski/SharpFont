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

        public void Dispose () {
            if (stream != null) {
                stream.Close();
                reader.Dispose();

                stream = null;
                reader = null;
            }
        }

        uint[] ReadTTCHeader () {
            var tag = reader.ReadUInt32BE();
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

        void Error (string message) {
            throw new Exception(message);
        }

        const int MaxFontsInCollection = 64;
    }
}
