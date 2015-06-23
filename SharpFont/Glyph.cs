using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace SharpFont {
    public class Glyph {
        Typeface typeface;
        int glyphIndex;
        int shiftX, shiftY;
        float scale;

        public readonly float Left;
        public readonly float Top;
        public readonly float Width;
        public readonly float Height;
        public readonly float Advance;

        public readonly int RenderWidth;
        public readonly int RenderHeight;

        internal Glyph (Typeface typeface, int glyphIndex, float scale) {
            this.typeface = typeface;
            this.glyphIndex = glyphIndex;
            this.scale = scale;

            var bbox = BoundingBox.Infinite;
            FindBoundingBox(typeface.Glyphs[glyphIndex], scale, ref bbox);

            Width = (bbox.MaxX - bbox.MinX) * scale;
            Height = (bbox.MaxY - bbox.MinY) * scale;

            shiftX = (int)Math.Floor(bbox.MinX * scale);
            shiftY = (int)Math.Floor(bbox.MinY * scale);
            RenderWidth = (int)Math.Ceiling(bbox.MaxX * scale) - shiftX;
            RenderHeight = (int)Math.Ceiling(bbox.MaxY * scale) - shiftY;

            //Left = left;
            //Top = top;
            //Width = width;
            //Height = height;
            //Advance = advance;
        }

        public void Render (Surface surface) {
            RenderGlyph(typeface.Glyphs[glyphIndex], surface);
        }

        void RenderGlyph (BaseGlyph glyph, Surface surface) {
            // if we have a composite, recursively render each subglyph
            var composite = glyph as CompositeGlyph;
            if (composite != null) {
                foreach (var subglyph in composite.Subglyphs)
                    RenderGlyph(typeface.Glyphs[subglyph.Index], surface);
                return;
            }

            // otherwise, we have a simple glyph, so render it
            var simple = (SimpleGlyph)glyph;
            var points = simple.Outline.Points;
            var contours = simple.Outline.ContourEndpoints;

            // check for an empty outline, which obviously results in an empty render
            if (points.Length <= 0 || contours.Length <= 0)
                return;

            // clip against the bounds of the target surface
            var width = Math.Min(RenderWidth, surface.Width);
            var height = Math.Min(RenderHeight, surface.Height);
            if (width <= 0 || height <= 0)
                return;

            // prep the renderer
            var renderer = typeface.Renderer;
            renderer.Start(width, height);

            // get the total transform for the glyph
            var transform = Matrix3x2.CreateScale(scale);
            transform.Translation = new Vector2(-shiftX, -shiftY);

            // walk each contour of the outline and render it
            var firstIndex = 0;
            for (int i = 0; i < contours.Length; i++) {
                // decompose the contour into drawing commands
                var lastIndex = contours[i];
                DecomposeContour(renderer, firstIndex, lastIndex, points, transform);

                // next contour starts where this one left off
                firstIndex = lastIndex + 1;
            }

            // blit the result to the target surface
            renderer.BlitTo(surface);
        }

        void FindBoundingBox (BaseGlyph glyph, float scale, ref BoundingBox bbox) {
            var simple = glyph as SimpleGlyph;
            if (simple != null) {
                foreach (var point in simple.Outline.Points)
                    bbox.UnionWith(point);
                return;
            }

            // otherwise, we have a composite
            var composite = (CompositeGlyph)glyph;
            foreach (var subglyph in composite.Subglyphs) {
                // calculate the offset for the subglyph


                FindBoundingBox(typeface.Glyphs[subglyph.Index], scale, ref bbox);
            }
        }

        static void DecomposeContour (Renderer renderer, int firstIndex, int lastIndex, Point[] points, Matrix3x2 transform) {
            var pointIndex = firstIndex;
            var start = points[pointIndex];
            var end = points[lastIndex];
            var startV = Vector2.Transform((Vector2)start, transform);
            var control = startV;
            
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
                    startV = (startV + Vector2.Transform((Vector2)end, transform)) / 2;
                }
                pointIndex--;
            }

            // let's draw this contour
            renderer.MoveTo(startV);

            var needClose = true;
            while (pointIndex < lastIndex) {
                var point = points[++pointIndex];
                var pointV = Vector2.Transform((Vector2)point, transform);
                switch (point.Type) {
                    case PointType.OnCurve:
                        renderer.LineTo(pointV);
                        break;

                    case PointType.Quadratic:
                        control = pointV;
                        var done = false;
                        while (pointIndex < lastIndex) {
                            var next = points[++pointIndex];
                            var nextV = Vector2.Transform((Vector2)next, transform);
                            if (next.Type == PointType.OnCurve) {
                                renderer.QuadraticCurveTo(control, nextV);
                                done = true;
                                break;
                            }
                            
                            if (next.Type != PointType.Quadratic)
                                throw new InvalidFontException("Bad outline data.");
                            
                            renderer.QuadraticCurveTo(control, (control + nextV) / 2);
                            control = nextV;
                        }

                        if (!done) {
                            // if we hit this point, we're ready to close out the contour
                            renderer.QuadraticCurveTo(control, startV);
                            needClose = false;
                        }
                        break;

                    case PointType.Cubic:
                        throw new NotSupportedException();
                }
            }

            if (needClose)
                renderer.LineTo(startV);
        }
    }

    public struct Surface {
        public IntPtr Bits;
        public int Width;
        public int Height;
        public int Pitch;
    }
}
