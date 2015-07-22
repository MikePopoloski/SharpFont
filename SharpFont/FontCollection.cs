using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpFont {
    public class FontCollection {
        static FontCollection systemFonts;
        readonly Dictionary<string, List<Metadata>> fontTable = new Dictionary<string, List<Metadata>>();

        public static FontCollection SystemFonts {
            get {
                if (systemFonts == null)
                    systemFonts = LoadSystemFonts();
                return systemFonts;
            }
        }

        public FontCollection () {
        }

        public void AddFontFile (Stream stream) {
            var metadata = LoadMetadata(stream);
            metadata.Stream = stream;
            AddFile(metadata, throwOnError: true);
        }

        public void AddFontFile (string fileName) => AddFontFile(fileName, throwOnError: true);

        public FontFace Load (string family, FontWeight weight = FontWeight.Normal, FontStretch stretch = FontStretch.Normal, FontStyle style = FontStyle.Regular) {
            List<Metadata> sublist;
            if (!fontTable.TryGetValue(family.ToLowerInvariant(), out sublist))
                return null;

            foreach (var file in sublist) {
                if (file.Weight == weight && file.Stretch == stretch && file.Style == style) {
                    if (file.Stream != null)
                        return new FontFace(file.Stream);
                    else {
                        using (var stream = File.OpenRead(file.FileName))
                            return new FontFace(stream);
                    }
                }
            }

            return null;
        }

        void AddFontFile (string fileName, bool throwOnError) {
            using (var stream = File.OpenRead(fileName)) {
                var metadata = LoadMetadata(stream);
                metadata.FileName = fileName;
                AddFile(metadata, throwOnError);
            }
        }

        void AddFile (Metadata metadata, bool throwOnError) {
            // specifically ignore fonts with no family name
            if (string.IsNullOrEmpty(metadata.Family)) {
                if (throwOnError)
                    throw new InvalidFontException("Font does not contain any name metadata.");
                else
                    return;
            }

            List<Metadata> sublist;
            var key = metadata.Family.ToLowerInvariant();
            if (fontTable.TryGetValue(key, out sublist))
                sublist.Add(metadata);
            else
                fontTable.Add(key, new List<Metadata> { metadata });
        }

        static FontCollection LoadSystemFonts () {
            // TODO: currently only supports Windows
            var collection = new FontCollection();
            foreach (var file in Directory.EnumerateFiles(Environment.GetFolderPath(Environment.SpecialFolder.Fonts))) {
                if (SupportedExtensions.Contains(Path.GetExtension(file).ToLowerInvariant()))
                    collection.AddFontFile(file, throwOnError: false);
            }

            return collection;
        }

        static Metadata LoadMetadata (Stream stream) {
            using (var reader = new DataReader(stream)) {
                var tables = SfntTables.ReadFaceHeader(reader);
                var names = SfntTables.ReadNames(reader, tables);
                var os2Data = SfntTables.ReadOS2(reader, tables);

                return new Metadata {
                    Family = names.TypographicFamilyName ?? names.FamilyName,
                    Weight = os2Data.Weight,
                    Stretch = os2Data.Stretch,
                    Style = os2Data.Style
                };
            }
        }

        class Metadata {
            public string Family;
            public FontWeight Weight;
            public FontStretch Stretch;
            public FontStyle Style;
            public Stream Stream;
            public string FileName;
        }

        static readonly HashSet<string> SupportedExtensions = new HashSet<string> {
            ".ttf", ".otf"
        };
    }
}
