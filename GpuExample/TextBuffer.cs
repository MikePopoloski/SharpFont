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

        public unsafe void Append (TextAnalyzer analyzer, FontFace font, string text) {
            var layout = new TextLayout();
            var format = new TextFormat {
                Font = font,
                Size = 8.0f
            };

            analyzer.AppendText(text, format);
            analyzer.PerformLayout(32, 64, 1000, 1000, layout);

            var memBlock = new MemoryBlock(text.Length * 6 * PosColorTexture.Layout.Stride);
            var mem = (PosColorTexture*)memBlock.Data;
            foreach (var thing in layout.Stuff) {
                var width = thing.Width;
                var height = thing.Height;
                var region = new Vector4(thing.SourceX, thing.SourceY, width, height) / 4096;
                var origin = new Vector2(thing.DestX, thing.DestY);
                *mem++ = new PosColorTexture(origin + new Vector2(0, height), new Vector2(region.X, region.Y + region.W), unchecked((int)0xff000000));
                *mem++ = new PosColorTexture(origin + new Vector2(width, height), new Vector2(region.X + region.Z, region.Y + region.W), unchecked((int)0xff000000));
                *mem++ = new PosColorTexture(origin + new Vector2(width, 0), new Vector2(region.X + region.Z, region.Y), unchecked((int)0xff000000));
                *mem++ = new PosColorTexture(origin, new Vector2(region.X, region.Y), unchecked((int)0xff000000));
                count++;
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
