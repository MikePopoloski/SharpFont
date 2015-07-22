using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace SharpFont {
    public class Glyph {
        Renderer renderer;
        PointF[] points;
        int[] contours;
        
        public readonly float Width;
        public readonly float Height;
        public readonly int RenderWidth;
        public readonly int RenderHeight;
        public readonly GlyphMetrics HorizontalMetrics;
        public readonly GlyphMetrics VerticalMetrics;

        internal Glyph (Renderer renderer, PointF[] points, int[] contours, float linearHorizontalAdvance) {
            this.renderer = renderer;
            this.points = points;
            this.contours = contours;

            // find the bounding box
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            var pointCount = points.Length - 4;
            for (int i = 0; i < pointCount; i++) {
                min = Vector2.Min(min, points[i].P);
                max = Vector2.Max(max, points[i].P);
            }

            // save the "pure" size of the glyph, in fractional pixels
            var size = max - min;
            Width = size.X;
            Height = size.Y;

            // find the "render" size of the glyph, in whole pixels
            var shiftX = (int)Math.Floor(min.X);
            var shiftY = (int)Math.Floor(min.Y);
            RenderWidth = (int)Math.Ceiling(max.X) - shiftX;
            RenderHeight = (int)Math.Ceiling(max.Y) - shiftY;

            // translate the points so that 0,0 is at the bottom left corner
            var offset = new Vector2(-shiftX, -shiftY);
            for (int i = 0; i < pointCount; i++)
                points[i] = points[i].Offset(offset);

            // TODO: figure out whether we want rounded bearings
            HorizontalMetrics = new GlyphMetrics {
                //Bearing = new Vector2(shiftX, (int)Math.Ceiling(max.Y)),
                Bearing = new Vector2(min.X, max.Y),
                Advance = points[pointCount + 1].P.X - points[pointCount].P.X,
                LinearAdvance = linearHorizontalAdvance
            };
            
            // TODO: vertical metrics
        }

        public void RenderTo (Surface surface) {
            // check for an empty outline, which obviously results in an empty render
            if (points.Length <= 0 || contours.Length <= 0)
                return;

            // clip against the bounds of the target surface
            var width = Math.Min(RenderWidth, surface.Width);
            var height = Math.Min(RenderHeight, surface.Height);
            if (width <= 0 || height <= 0)
                return;

            // walk each contour of the outline and render it
            var firstIndex = 0;
            renderer.Start(width, height);
            for (int i = 0; i < contours.Length; i++) {
                // decompose the contour into drawing commands
                var lastIndex = contours[i];
                Geometry.DecomposeContour(renderer, firstIndex, lastIndex, points);

                // next contour starts where this one left off
                firstIndex = lastIndex + 1;
            }

            // blit the result to the target surface
            renderer.BlitTo(surface);
        }
    }

    public struct GlyphMetrics {
        public Vector2 Bearing;
        public float Advance;
        public float LinearAdvance;
    }

    public struct Surface {
        public IntPtr Bits;
        public int Width;
        public int Height;
        public int Pitch;
    }
}
