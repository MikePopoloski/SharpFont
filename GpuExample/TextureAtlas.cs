using SharpBgfx;
using SharpFont;
using System;

namespace GpuExample {
    class TextureAtlas : IGlyphAtlas, IDisposable {
        Texture texture;
        int size;

        public int Width => size;
        public int Height => size;
        public Texture Texture => texture;

        public TextureAtlas (int size) {
            this.size = size;
            texture = Texture.Create2D(size, size, 1, TextureFormat.R8);
        }

        public void Dispose () => texture.Dispose();

        public void Insert (int page, int x, int y, int width, int height, IntPtr data) {
            if (page > 0)
                throw new NotImplementedException();

            texture.Update2D(0, x, y, width, height, new MemoryBlock(data, width * height), width);
        }
    }
}
