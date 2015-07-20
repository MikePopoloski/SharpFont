using SharpBgfx;
using SharpFont;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace GpuExample {
    unsafe class TextBuffer {
        IndexBuffer indexBuffer;
        DynamicVertexBuffer vertexBuffer;
        int count;

        public TextBuffer (int capacity) {
            var indexMem = new MemoryBlock(sizeof(ushort) * capacity * 6);
            var indices = (ushort*)indexMem.Data;
            for (int i = 0, v = 0; i < capacity; i++, v += 4) {
                *indices++ = (ushort)(v + 0);
                *indices++ = (ushort)(v + 1);
                *indices++ = (ushort)(v + 2);
                *indices++ = (ushort)(v + 2);
                *indices++ = (ushort)(v + 3);
                *indices++ = (ushort)(v + 0);
            }

            indexBuffer = new IndexBuffer(indexMem);
        }

        public unsafe void Append (TextureAtlas atlas, FontFace typeface, string text) {
            var memBlock = new MemoryBlock(text.Length * 6 * PosColorTexture.Layout.Stride);
            var mem = (PosColorTexture*)memBlock.Data;

            var pen = new Vector2(32, 64);
            char previous = '\0';

            var pixelSize = FontFace.ComputePixelSize(8.0f, 96);

            foreach (var c in text) {
                var glyph = typeface.GetGlyph(c, pixelSize);
                if (glyph.RenderWidth == 0 || glyph.RenderHeight == 0) {
                    pen.X += glyph.HorizontalMetrics.Advance;
                    previous = c;
                    continue;
                }

                var memory = new MemoryBlock(glyph.RenderWidth * glyph.RenderHeight);
                var surface = new Surface {
                    Bits = memory.Data,
                    Width = glyph.RenderWidth,
                    Height = glyph.RenderHeight,
                    Pitch = glyph.RenderWidth
                };

                var stuff = (byte*)surface.Bits;
                for (int i = 0; i < surface.Width * surface.Height; i++)
                    *stuff++ = 0;

                glyph.RenderTo(surface);

                var index = atlas.AddRegion(surface.Width, surface.Height, memory);

                var region = atlas.GetRegion(index);
                var width = region.Z * 4096;
                var height = region.W * -4096;

                var metrics = glyph.HorizontalMetrics;
                var bearing = metrics.Bearing;
                var kerning = typeface.GetKerning(previous, c, pixelSize);
                pen.X += kerning;

                var origin = new Vector2((int)Math.Round(pen.X + bearing.X), (int)Math.Round(pen.Y - bearing.Y));
                //var origin = new Vector2(pen.X + bearing.X, pen.Y - bearing.Y);


                *mem++ = new PosColorTexture(origin + new Vector2(0, glyph.RenderHeight), new Vector2(region.X, region.Y + region.W), unchecked((int)0xff000000));
                *mem++ = new PosColorTexture(origin + new Vector2(glyph.RenderWidth, glyph.RenderHeight), new Vector2(region.X + region.Z, region.Y + region.W), unchecked((int)0xff000000));
                *mem++ = new PosColorTexture(origin + new Vector2(glyph.RenderWidth, 0), new Vector2(region.X + region.Z, region.Y), unchecked((int)0xff000000));
                *mem++ = new PosColorTexture(origin, new Vector2(region.X, region.Y), unchecked((int)0xff000000));

                pen.X += (float)Math.Round(metrics.Advance);
                count++;
                previous = c;
            }

            vertexBuffer = new DynamicVertexBuffer(memBlock, PosColorTexture.Layout);
        }

        public void Submit () {
            Bgfx.SetVertexBuffer(vertexBuffer, count * 4);
            Bgfx.SetIndexBuffer(indexBuffer, 0, count * 6);
            Bgfx.SetRenderState(RenderState.ColorWrite | RenderState.BlendFunction(RenderState.BlendSourceAlpha, RenderState.BlendInverseSourceAlpha));
            Bgfx.Submit(0);
        }
    }
}
