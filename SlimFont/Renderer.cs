using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace SlimFont {
    // handles rasterizing curves to a bitmap
    unsafe class Renderer {
        int bandSize = 512 / 8;

        public Renderer () {
        }

        // we assume the caller has done all of the necessary error checking
        public void Render (GlyphOutline outline, Surface surface) {
            // compute bounding boxes and perform clipping
            var clipBox = new BoundingBox { MaxX = surface.Width, MaxY = surface.Height };
            var bbox = ComputeBoundingBox(outline, clipBox);
            if (bbox.Area <= 0)
                return;
            
            // set up vertical bands
            var bandCount = Math.Min(MaxBands, Math.Max(1, bbox.Height / bandSize));
            var bands = stackalloc Band[bandCount];
        }

        static BoundingBox ComputeBoundingBox (GlyphOutline outline, BoundingBox clip) {
            var points = outline.Points;
            if (points.Length < 1)
                return BoundingBox.Empty;

            var first = points[0];
            var box = new BoundingBox {
                MinX = first.X,
                MaxX = first.X,
                MinY = first.Y,
                MaxY = first.Y
            };

            for (int i = 1; i < points.Length; i++) {
                var point = points[i];
                box.MinX = Math.Min(box.MinX, point.X);
                box.MinY = Math.Min(box.MinY, point.Y);
                box.MaxX = Math.Max(box.MaxX, point.X);
                box.MaxY = Math.Max(box.MaxY, point.Y);
            }

            // perform the intersection between the outline box and the clipping region
            box.MinX = Math.Max(box.MinX, clip.MinX);
            box.MinY = Math.Max(box.MinY, clip.MinY);
            box.MaxX = Math.Min(box.MaxX, clip.MaxX);
            box.MaxY = Math.Min(box.MaxY, clip.MaxY);

            return box;
        }

        struct Band {
            public int Min;
            public int Max;
        }

        const int MaxBands = 39;
    }

    public struct Surface {
        public IntPtr Bits;
        public int Width;
        public int Height;
        public int Pitch;
    }

    struct BoundingBox {
        public static readonly BoundingBox Empty = new BoundingBox();

        public int MinX;
        public int MinY;
        public int MaxX;
        public int MaxY;

        public int Width => MaxX - MinX;
        public int Height => MaxY - MinY;
        public int Area => Width * Height;
    }
}
