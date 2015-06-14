using SharpFont;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace Test {
    class Program {
        unsafe static void Main (string[] args) {
            var surface = new Surface {
                Bits = Marshal.AllocHGlobal(27 * 46),
                Width = 27,
                Height = 46,
                Pitch = 27
            };

            var stuff = (byte*)surface.Bits;
            for (int i = 0; i < 27 * 46; i++)
                *stuff++ = 0;

            using (var file = File.OpenRead("../../../Fonts/OpenSans-Regular.ttf"))
            using (var loader = new FontReader(file)) {
                var face = loader.ReadFace();
                var blah = face.GetFaceMetrics(64);
                var glyph = face.GetGlyph('a', 64);

                glyph.Render(surface);
            }

            // copy the output to a bitmap for easy debugging
            var bitmap = new Bitmap(surface.Width, surface.Height);
            var bitmapData = bitmap.LockBits(new Rectangle(0, 0, surface.Width, surface.Height), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);

            for (int y = 0; y < surface.Height; y++) {
                var dest = (byte*)bitmapData.Scan0 + y * bitmapData.Stride;
                var src = (byte*)surface.Bits + y * surface.Pitch;

                for (int x = 0; x < surface.Width; x++) {
                    var b = *src++;
                    *dest++ = b;
                    *dest++ = b;
                    *dest++ = b;
                }
            }

            bitmap.UnlockBits(bitmapData);
            bitmap.Save("result.bmp");
        }
    }
}
