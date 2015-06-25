using SharpBgfx;
using SharpFont;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace GpuExample {
    class TextBuffer {
        MemoryBlock vertexMem;
        IndexBuffer indexBuffer;
        DynamicVertexBuffer vertexBuffers;

        public TextBuffer () {
        }

        public unsafe void Append (TextureAtlas atlas, Typeface typeface, string text) {
            foreach (var c in text) {
                var glyph = typeface.GetGlyph(c, 32.0f);

                var surface = new Surface {
                    Bits = Marshal.AllocHGlobal(glyph.RenderWidth * glyph.RenderHeight),
                    Width = glyph.RenderWidth,
                    Height = glyph.RenderHeight,
                    Pitch = glyph.RenderWidth
                };

                var stuff = (byte*)surface.Bits;
                for (int i = 0; i < surface.Width * surface.Height; i++)
                    *stuff++ = 0;

                glyph.RenderTo(surface);

                atlas.AddRegion(surface.Width, surface.Height, surface.Bits);

                Marshal.FreeHGlobal(surface.Bits);
            }
        }
    }
}
