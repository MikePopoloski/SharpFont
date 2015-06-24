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

        public readonly float Left;
        public readonly float Top;
        public readonly float Width;
        public readonly float Height;
        public readonly float Advance;

        public readonly int RenderWidth;
        public readonly int RenderHeight;

        internal Glyph (Renderer renderer, PointF[] points, int[] contours) {
            this.renderer = renderer;
            this.points = points;
            this.contours = contours;

            // find the bounding box
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            for (int i = 0; i < points.Length; i++) {
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
            for (int i = 0; i < points.Length; i++)
                points[i].Offset(offset);

            //Left = left;
            //Top = top;
            //Width = width;
            //Height = height;
            //Advance = advance;
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
                DecomposeContour(renderer, firstIndex, lastIndex, points);

                // next contour starts where this one left off
                firstIndex = lastIndex + 1;
            }

            // blit the result to the target surface
            renderer.BlitTo(surface);
        }
        
        static void DecomposeContour (Renderer renderer, int firstIndex, int lastIndex, PointF[] points) {
            var pointIndex = firstIndex;
            var start = points[pointIndex];
            var end = points[lastIndex];
            var control = start;

            if (start.Type == PointType.Cubic)
                throw new InvalidFontException("Contours can't start with a cubic control point.");

            if (start.Type == PointType.Quadratic) {
                // if first point is a control point, try using the last point
                if (end.Type == PointType.OnCurve) {
                    start = end;
                    lastIndex--;
                }
                else {
                    // if they're both control points, start at the middle
                    start.P = (start.P + end.P) / 2;
                }
                pointIndex--;
            }

            // let's draw this contour
            renderer.MoveTo(start);

            var needClose = true;
            while (pointIndex < lastIndex) {
                var point = points[++pointIndex];
                switch (point.Type) {
                    case PointType.OnCurve:
                        renderer.LineTo(point);
                        break;

                    case PointType.Quadratic:
                        control = point;
                        var done = false;
                        while (pointIndex < lastIndex) {
                            var next = points[++pointIndex];
                            if (next.Type == PointType.OnCurve) {
                                renderer.QuadraticCurveTo(control, next);
                                done = true;
                                break;
                            }

                            if (next.Type != PointType.Quadratic)
                                throw new InvalidFontException("Bad outline data.");

                            renderer.QuadraticCurveTo(control, (control.P + next.P) / 2);
                            control = next;
                        }

                        if (!done) {
                            // if we hit this point, we're ready to close out the contour
                            renderer.QuadraticCurveTo(control, start);
                            needClose = false;
                        }
                        break;

                    case PointType.Cubic:
                        throw new NotSupportedException();
                }
            }

            if (needClose)
                renderer.LineTo(start);
        }
    }

    public struct Surface {
        public IntPtr Bits;
        public int Width;
        public int Height;
        public int Pitch;
    }
}
