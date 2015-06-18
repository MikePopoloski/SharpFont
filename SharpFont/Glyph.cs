using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpFont {
    public class Glyph {
        Typeface typeface;
        int glyphIndex;

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

            var bbox = new BoundingBox {
                MinX = new F26Dot6(int.MaxValue),
                MinY = new F26Dot6(int.MaxValue),
                MaxX = new F26Dot6(int.MinValue),
                MaxY = new F26Dot6(int.MinValue)
            };

            FindBoundingBox(typeface.Glyphs[glyphIndex], scale, ref bbox);

            Width = (int)(bbox.MaxX - bbox.MinX) * scale;
            Height = (int)(bbox.MaxY - bbox.MinY) * scale;

            RenderWidth = (int)((int)(FixedMath.Ceiling(bbox.MaxX) - FixedMath.Floor(bbox.MinX)) * scale);
            RenderHeight = (int)((int)(FixedMath.Ceiling(bbox.MaxY) - FixedMath.Floor(bbox.MinY)) * scale);

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
            var renderer = typeface.Renderer;
            var simple = (SimpleGlyph)glyph;
            var outline = simple.Outline;
            var points = outline.Points;
            var contours = outline.ContourEndpoints;
            var types = outline.PointTypes;

            // check for an empty outline, which obviously results in an empty render
            if (points.Length <= 0 || contours.Length <= 0)
                return;

            // compute control box; we'll shift the glyph so that it's rendered
            // with its bottom corner at the bottom left of the target surface
            var cbox = FixedMath.ComputeControlBox(points);
            var shiftX = -cbox.MinX;
            var shiftY = -cbox.MinY;

            // shift down into integer pixel coordinates and clip
            // against the bounds of the passed in target surface
            cbox = FixedMath.Translate(cbox, shiftX, shiftY);
            var minX = Math.Max(cbox.MinX.IntPart, 0);
            var minY = Math.Max(cbox.MinY.IntPart, 0);
            var maxX = Math.Min(cbox.MaxX.IntPart, surface.Width);
            var maxY = Math.Min(cbox.MaxY.IntPart, surface.Height);

            // check if the entire thing was clipped
            if (maxX - minX <= 0 || maxY - minY <= 0)
                return;

            // prep the renderer
            renderer.Clear();
            renderer.SetBounds(minX, minY, maxX, maxY);
            renderer.SetOffset(shiftX, shiftY);

            // walk each contour of the outline and render it
            var firstIndex = 0;
            for (int i = 0; i < contours.Length; i++) {
                // decompose the contour into drawing commands
                var lastIndex = contours[i];
                DecomposeContour(renderer, firstIndex, lastIndex, points, types);

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

        static void DecomposeContour (Renderer renderer, int firstIndex, int lastIndex, Point[] points, PointType[] types) {
            var pointIndex = firstIndex;
            var type = types[pointIndex];
            var start = points[pointIndex];
            var end = points[lastIndex];
            var control = start;

            // contours can't start with a cubic control point.
            if (type == PointType.Cubic)
                return;

            if (type == PointType.Quadratic) {
                // if first point is a control point, try using the last point
                if (types[lastIndex] == PointType.OnCurve) {
                    start = end;
                    lastIndex--;
                }
                else {
                    // if they're both control points, start at the middle
                    start.X = (start.X + end.X) / 2;
                    start.Y = (start.Y + end.Y) / 2;
                }
                pointIndex--;
            }

            // let's draw this contour
            renderer.MoveTo(start);

            var needClose = true;
            while (pointIndex < lastIndex) {
                var point = points[++pointIndex];
                switch (types[pointIndex]) {
                    case PointType.OnCurve:
                        renderer.LineTo(point);
                        break;

                    case PointType.Quadratic:
                        control = point;
                        var done = false;
                        while (pointIndex < lastIndex) {
                            var v = points[++pointIndex];
                            var t = types[pointIndex];
                            if (t == PointType.OnCurve) {
                                renderer.QuadraticCurveTo(control, v);
                                done = true;
                                break;
                            }

                            // this condition checks for garbage outlines
                            if (t != PointType.Quadratic)
                                return;

                            var middle = new Point((control.X + v.X) / 2, (control.Y + v.Y) / 2);
                            renderer.QuadraticCurveTo(control, middle);
                            control = v;
                        }

                        // if we hit this point, we're ready to close out the contour
                        if (!done) {
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
