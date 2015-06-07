using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace SlimFont {
    // handles rasterizing curves to a bitmap
    unsafe class Renderer {
        Context context = new Context();
        Surface surface;

        public Renderer () {
        }

        // we assume the caller has done all of the necessary error checking
        public void Render (GlyphOutline outline, Surface surface) {
            var bbox = ComputeBoundingBox(outline);
            var shiftX = -bbox.MinX;
            var shiftY = -bbox.MinY;
            TranslateOutline(outline, shiftX, shiftY);

            // compute bounding boxes and perform clipping
            var clipBox = new BoundingBox { MaxX = surface.Width, MaxY = surface.Height };
            bbox = ComputeHighResBoundingBox(outline, clipBox);
            if (bbox.Area <= 0)
                return;

            // set up memory regions
            var cells = stackalloc Cell[CellBufferSize];
            var bands = stackalloc Band[MaxBands];
            var bandCount = Math.Min(MaxBands, Math.Max(1, bbox.Height / BandSize));

            // reset our drawing context
            context.Clear();
            context.Bounds = bbox;
            context.Cells = cells;
            this.surface = surface;

            // we draw the font outline by iterating through horizontal stripes
            // we try to make the stripes as large as possible, but if there is a
            // lot of complexity in a scanline we shrink them down and draw more of them
            var min = bbox.MinY;
            for (int i = 0; i < bandCount; i++) {
                // the last band always extends fully to the bottom edge of the bounding box
                var max = min + BandSize;
                if (i == bandCount - 1 || max > bbox.MaxY)
                    max = bbox.MaxY;

                bands[0].Min = min;
                bands[0].Max = max;

                var currentBand = bands;
                while (currentBand >= bands) {
                    // render the band
                    RenderBand(currentBand, outline);
                    currentBand--;
                }

                // advance the band
                min = max;
            }
        }

        void RenderBand (Band* band, GlyphOutline outline) {
            // set up the context
            context.HasCell = false;
            context.BandHeight = band->Max - band->Min;
            context.CellCount = 0;
            context.Bounds.MinY = band->Min;
            context.Bounds.MaxY = band->Max;

            // memory for our cell pointers; we maintain a table
            // of linked lists of cells, one top-level entry per y level
            var scanlines = stackalloc Cell*[context.BandHeight];
            context.Scanlines = scanlines;

            // draw the glyph into the current band region
            Decompose(outline);
            if (context.HasCell)
                RecordCell();

            // fill in the area bounded by the contour
            Fill();
        }

        void MoveTo (Point point) {
            // record current cell, if any
            if (context.HasCell)
                RecordCell();

            // start at the new position
            point = Upscale(point);

            var x = Math.Max(context.Bounds.MinX - 1, Math.Min(Truncate(point.X), context.Bounds.MaxX));
            var y = Truncate(point.Y);

            context.Area = 0;
            context.Coverage = 0;
            context.Coord = new Point(x - context.Bounds.MinX, y - context.Bounds.MinY);
            context.LastSubpixelY = Subpixels(y);
            context.HasCell = true;

            SetCurrentCell(x, y);

            context.Subpixel = point;
        }

        // render a line as a series of scanlines
        void LineTo (Point point) {
            var target = Upscale(point);

            var subpixelY1 = Truncate(context.LastSubpixelY);
            var subpixelY2 = Truncate(target.Y);
            var y1 = context.Subpixel.Y - context.LastSubpixelY;
            var y2 = target.Y - Subpixels(subpixelY2);
            var dx = target.X - context.Subpixel.X;
            var dy = target.Y - context.Subpixel.Y;

            // vertical clipping
            var min = subpixelY1;
            var max = subpixelY2;
            if (subpixelY1 > subpixelY2) {
                min = subpixelY2;
                max = subpixelY1;
            }

            if (min < context.Bounds.MaxY && max >= context.Bounds.MinY) {
                if (subpixelY1 == subpixelY2) {
                    // this is a horizontal line
                    RenderScanline(subpixelY1, new Point(context.Subpixel.X, y1), new Point(target.X, y2));
                }
                else if (dx == 0) {
                    // this is a vertical line
                    var x = Truncate(context.Subpixel.X);
                    var doubleX = (context.Subpixel.X - Subpixels(x)) << 1;

                    // check if we're going up or down
                    var first = OnePixel;
                    var increment = 1;
                    if (dy < 0) {
                        first = 0;
                        increment = -1;
                    }

                    // first cell
                    var deltaY = first - y1;
                    context.Area += doubleX * deltaY;
                    context.Coverage += deltaY;
                    subpixelY1 += increment;
                    SetCurrentCell(x, subpixelY1);

                    // any other cells covered by the line
                    deltaY = first + first - OnePixel;
                    var area = doubleX * deltaY;
                    while (subpixelY1 != subpixelY2) {
                        context.Area += area;
                        context.Coverage += deltaY;
                        subpixelY1 += increment;
                        SetCurrentCell(x, subpixelY1);
                    }

                    // finish off
                    deltaY = y2 - OnePixel + first;
                    context.Area += doubleX * deltaY;
                    context.Coverage += deltaY;
                }
                else {
                    // slanted line
                    var p = (OnePixel - y1) * dx;
                    var first = OnePixel;
                    var increment = 1;

                    if (dy < 0) {
                        p = y1 * dx;
                        first = 0;
                        increment = -1;
                        dy = -dy;
                    }

                    int delta, mod;
                    DivMod(p, dy, out delta, out mod);

                    var x = context.Subpixel.X + delta;
                    RenderScanline(subpixelY1, new Point(context.Subpixel.X, y1), new Point(x, first));

                    subpixelY1 += increment;
                    SetCurrentCell(Truncate(x), subpixelY1);

                    if (subpixelY1 != subpixelY2) {
                        p = OnePixel * dx;

                        int lift, rem;
                        DivMod(p, dy, out lift, out rem);

                        mod -= dy;
                        while (subpixelY1 != subpixelY2) {
                            delta = lift;
                            mod += rem;
                            if (mod >= 0) {
                                mod -= dy;
                                delta++;
                            }

                            var x2 = x + delta;
                            RenderScanline(subpixelY1, new Point(x, OnePixel - first), new Point(x2, first));
                            x = x2;

                            subpixelY1 += increment;
                            SetCurrentCell(Truncate(x), subpixelY1);
                        }
                    }

                    RenderScanline(subpixelY1, new Point(x, OnePixel - first), new Point(target.X, y2));
                }
            }

            context.Subpixel = target;
            context.LastSubpixelY = Subpixels(subpixelY2);
        }

        void QuadraticCurveTo (Point control, Point point) {
        }

        // set the current cell to a new position
        void SetCurrentCell (int x, int y) {
            // all cells on the left of the clipping region go to the MinX - 1 position
            y -= context.Bounds.MinY;
            x = Math.Min(x, context.Bounds.MaxX);
            x -= context.Bounds.MinX;
            x = Math.Max(x, -1);

            // moving to a new cell?
            if (x != context.Coord.X || y != context.Coord.Y) {
                if (context.HasCell)
                    RecordCell();

                context.Area = 0;
                context.Coverage = 0;
                context.Coord = new Point(x, y);
            }

            context.HasCell = y < context.Bounds.Height && x < context.Bounds.Width;
        }

        void RecordCell () {
            if ((context.Area | context.Coverage) != 0) {
                var cell = FindCell();
                cell->Area += context.Area;
                cell->Coverage = context.Coverage;
            }
        }

        Cell* FindCell () {
            var x = Math.Min(context.Coord.X, context.Bounds.Width);
            var y = context.Coord.Y;

            var cell = context.Scanlines[y];
            if (cell == null || cell->X > x) {
                cell = GetNewCell(x, cell);
                context.Scanlines[y] = cell;
                return cell;
            }

            while (cell->X != x) {
                var next = cell->Next;
                if (next == null || next->X > x) {
                    var newCell = GetNewCell(x, next);
                    cell->Next = newCell;
                    return newCell;
                }

                cell = next;
            }

            return cell;
        }

        Cell* GetNewCell (int x, Cell* next) {
            if (context.CellCount >= CellBufferSize)
                throw new InvalidOperationException();

            var cell = context.Cells + context.CellCount++;
            cell->X = x;
            cell->Area = 0;
            cell->Coverage = 0;
            cell->Next = next;

            return cell;
        }

        void RenderScanline (int subpixelY, Point a, Point b) {
            var cellX1 = Truncate(a.X);
            var cellX2 = Truncate(b.X);
            var coordX1 = a.X - Subpixels(cellX1);
            var coordX2 = b.X - Subpixels(cellX2);

            // trivial case; same Y
            if (a.Y == b.Y) {
                SetCurrentCell(cellX2, subpixelY);
                return;
            }

            // trivial case; same cell
            if (cellX1 == cellX2) {
                var deltaY = b.Y - a.Y;
                context.Area += (coordX1 + coordX2) * deltaY;
                context.Coverage += deltaY;
                return;
            }

            // long case: render a run of adjacent cells on the scanline
            var p = (OnePixel - coordX1) * (b.Y - a.Y);
            var first = OnePixel;
            var increment = 1;
            var dx = b.X - a.X;

            if (dx < 0) {
                p = coordX1 * (b.Y - a.Y);
                first = 0;
                increment = -1;
                dx = -dx;
            }

            int delta, mod;
            DivMod(p, dx, out delta, out mod);
            context.Area += (coordX1 + first) * delta;
            context.Coverage += delta;

            cellX1 += increment;
            SetCurrentCell(cellX1, subpixelY);
            a.Y += delta;

            if (cellX1 != cellX2) {
                p = OnePixel * (b.Y - a.Y + delta);

                int lift, rem;
                DivMod(p, dx, out lift, out rem);

                mod -= dx;
                while (cellX1 != cellX2) {
                    delta = lift;
                    mod += rem;
                    if (mod >= 0) {
                        mod -= dx;
                        delta++;
                    }

                    context.Area += OnePixel * delta;
                    context.Coverage += delta;
                    a.Y += delta;
                    cellX1 += increment;
                    SetCurrentCell(cellX1, subpixelY);
                }
            }

            delta = b.Y - a.Y;
            context.Area += (coordX2 + OnePixel - first) * delta;
            context.Coverage += delta;
        }

        void Fill () {
            if (context.CellCount == 0)
                return;

            for (int y = 0; y < context.BandHeight; y++) {
                var x = 0;
                var coverage = 0;
                for (var cell = context.Scanlines[y]; cell != null; cell = cell->Next) {
                    if (cell->X > x && coverage != 0)
                        FillHLine(x, y, coverage * OnePixel * 2, cell->X - x);

                    coverage += cell->Coverage;

                    var area = coverage * OnePixel * 2 - cell->Area;
                    if (area != 0 && cell->X >= 0)
                        FillHLine(cell->X, y, area, 1);

                    x = cell->X + 1;
                }

                if (coverage != 0)
                    FillHLine(x, y, coverage * OnePixel * 2, context.Bounds.Width - x);
            }
        }

        void FillHLine (int x, int y, int area, int count) {
            // compute coverage, depending on fill rules
            var coverage = area >> (PixelBits * 2 + 1 - 8);
            if (coverage < 0)
                coverage = -coverage;

            // TODO: even / odd fill
            if (coverage >= 256)
                coverage = 255;

            if (coverage == 0)
                return;

            x += context.Bounds.MinX;
            y += context.Bounds.MinY;

            x = Math.Min(x, 32767);

            var span = new Span {
                X = x,
                Length = count,
                Coverage = coverage
            };

            RenderSpan(y, &span, 1);
        }

        void RenderSpan (int y, Span* spans, int count) {
            // find the scanline offset
            var bits = (byte*)surface.Bits - y * surface.Pitch;
            if (surface.Pitch >= 0)
                bits += (surface.Height - 1) * surface.Pitch;

            for (; count > 0; count--, spans++) {
                var coverage = spans->Coverage;
                if (coverage == 0)
                    continue;

                if (spans->X + spans->Length > 27) {
                    throw new Exception();
                }

                if (y >= 46) {
                    throw new Exception();
                }

                // finally fill pixels
                var p = bits + spans->X;
                for (int i = 0; i < spans->Length; i++)
                    *p++ = (byte)coverage;
            }
        }

        void Decompose (GlyphOutline outline) {
            var contours = outline.ContourEndpoints;
            var points = outline.Points;
            var types = outline.PointTypes;
            var firstIndex = 0;

            for (int i = 0; i < contours.Length; i++) {
                var lastIndex = contours[i];
                var limit = lastIndex;
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
                        limit--;
                    }
                    else {
                        // if they're both control points, start at the middle
                        start.X = (start.X + end.X) / 2;
                        start.Y = (start.Y + end.Y) / 2;
                    }
                    pointIndex--;
                }

                // let's draw this contour
                MoveTo(start);

                var needClose = true;
                while (pointIndex < limit) {
                    var point = points[++pointIndex];
                    switch (types[pointIndex]) {
                        case PointType.OnCurve:
                            LineTo(point);
                            break;

                        case PointType.Quadratic:
                            control = point;
                            var done = false;
                            while (pointIndex < limit) {
                                var v = points[++pointIndex];
                                var t = types[pointIndex];
                                if (t == PointType.OnCurve) {
                                    QuadraticCurveTo(control, v);
                                    done = true;
                                    break;
                                }

                                // this condition checks for garbage outlines
                                if (t != PointType.Quadratic)
                                    return;

                                var middle = new Point((control.X + v.X) / 2, (control.Y + v.Y) / 2);
                                QuadraticCurveTo(control, middle);
                                control = v;
                            }

                            // if we hit this point, we're ready to close out the contour
                            if (!done) {
                                QuadraticCurveTo(control, start);
                                needClose = false;
                            }
                            break;

                        case PointType.Cubic:
                            throw new NotSupportedException();
                    }
                }

                if (needClose)
                    LineTo(start);

                firstIndex = lastIndex + 1;
            }
        }

        static BoundingBox ComputeHighResBoundingBox (GlyphOutline outline, BoundingBox clip) {
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

            // truncate to integer pixels
            box.MinX >>= 6;
            box.MinY >>= 6;
            box.MaxX = (box.MaxX + 63) >> 6;
            box.MaxY = (box.MaxY + 63) >> 6;

            // perform the intersection between the outline box and the clipping region
            box.MinX = Math.Max(box.MinX, clip.MinX);
            box.MinY = Math.Max(box.MinY, clip.MinY);
            box.MaxX = Math.Min(box.MaxX, clip.MaxX);
            box.MaxY = Math.Min(box.MaxY, clip.MaxY);

            return box;
        }

        static BoundingBox ComputeBoundingBox (GlyphOutline outline) {
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
            //box.MinX = Math.Max(box.MinX, clip.MinX);
            //box.MinY = Math.Max(box.MinY, clip.MinY);
            //box.MaxX = Math.Min(box.MaxX, clip.MaxX);
            //box.MaxY = Math.Min(box.MaxY, clip.MaxY);

            box.MinX = PixFloor(box.MinX);
            box.MinY = PixFloor(box.MinY);
            box.MaxX = PixCeil(box.MaxX);
            box.MaxY = PixCeil(box.MaxY);

            return box;
        }

        static void TranslateOutline (GlyphOutline outline, int offsetX, int offsetY) {
            var points = outline.Points;
            for (int i = 0; i < points.Length; i++) {
                points[i].X += offsetX;
                points[i].Y += offsetY;
            }
        }

        // methods for working with fixed-point pixel positions

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static Point Upscale (Point p) => new Point(Upscale(p.X), Upscale(p.Y));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static int Upscale (int x) => x << (PixelBits - 6);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static int Downscale (int x) => x >> (PixelBits - 6);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static int Truncate (int x) => x >> PixelBits;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static int Subpixels (int x) => x << PixelBits;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static int Floor (int x) => x & -OnePixel;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static int Ceiling (int x) => (x + OnePixel - 1) & -OnePixel;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static int Round (int x) => (x + OnePixel / 2) & -OnePixel;

        static void DivMod (int dividend, int divisor, out int quotient, out int remainder) {
            quotient = dividend / divisor;
            remainder = dividend % divisor;
            if (remainder < 0) {
                quotient--;
                remainder += divisor;
            }
        }

        static int PixFloor (int x) => x & ~63;
        static int PixCeil (int x) => PixFloor(x + 63);

        struct Band {
            public int Min;
            public int Max;
        }

        struct Cell {
            public Cell* Next;
            public int X;
            public int Coverage;
            public int Area;
        }

        struct Span {
            public int X;
            public int Length;
            public int Coverage;
        }

        class Context {
            public Cell* Cells;
            public Cell** Scanlines;
            public BoundingBox Bounds;
            public Point Coord;
            public Point Subpixel;
            public int Area;
            public int Coverage;
            public int LastSubpixelY;
            public int CellCount;
            public int BandHeight;
            public bool HasCell;

            public void Clear () {
            }
        }

        const int MaxBands = 39;
        const int CellBufferSize = 1024;
        const int BandSize = CellBufferSize >> 3;
        const int PixelBits = 8;
        const int OnePixel = 1 << PixelBits;
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
