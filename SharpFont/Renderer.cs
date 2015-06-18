using System;
using System.Numerics;

namespace SharpFont {
    // handles rasterizing curves to a bitmap
    // the algorithm is heavily inspired by the FreeType2 renderer; thanks guys!
    unsafe class Renderer {
        Surface surface;                // the surface we're currently rendering to
        int[] scanlines;                // one scanline per Y, points into cell buffer
        int[] curveLevels;
        Vector2[] bezierArc;            // points on a bezier arc
        Cell[] cells;
        F26Dot6 shiftX, shiftY;         // the amount to translate incoming points
        Vector2 subpixelPos;            // subpixel position of active point
        float funitsToPixels;           // conversion factor from FUnits to pixels
        float activeArea;               // running total of the active cell's area
        float activeCoverage;           // ditto for coverage
        int cellX, cellY;               // pixel position of the active cell
        int cellCount;                  // number of cells in active use
        int scanlineCount;              // number of scanlines we're rendering
        int minX, minY;                 // bounds of the glyph surface, in plain old pixels
        int maxX, maxY;
        bool cellActive;                // whether the current cell has active data

        public Renderer () {
            cells = new Cell[1024];
            scanlines = new int[128];
            curveLevels = new int[32];
            bezierArc = new Vector2[curveLevels.Length * 3 + 1];
        }

        public void Clear () {
            scanlineCount = 0;
            cellCount = 0;
            activeArea = 0.0f;
            activeCoverage = 0.0f;
            cellActive = false;
        }

        public void SetBounds (int minX, int minY, int maxX, int maxY) {
            this.minX = minX;
            this.minY = minY;
            this.maxX = maxX;
            this.maxY = maxY;

            scanlineCount = maxY - minY;
            if (scanlineCount >= scanlines.Length)
                scanlines = new int[scanlineCount];

            for (int i = 0; i < scanlineCount; i++)
                scanlines[i] = -1;
        }

        public void SetOffset (F26Dot6 shiftX, F26Dot6 shiftY) {
            this.shiftX = shiftX;
            this.shiftY = shiftY;
        }

        public void MoveTo (Point point) {
            // record current cell, if any
            if (cellActive)
                RetireActiveCell();

            // calculate cell coordinates
            subpixelPos = new Vector2((int)(point.X + shiftX) / 64.0f, (int)(point.Y + shiftY) / 64.0f);
            cellX = Math.Max(minX - 1, Math.Min((int)subpixelPos.X, maxX)) - minX;
            cellY = (int)subpixelPos.Y - minY;

            // activate if this is a valid cell location
            cellActive = cellX < maxX && cellY < maxY;
            activeArea = 0.0f;
            activeCoverage = 0.0f;
        }

        public void LineTo (Point point) {
            RenderLine(new Vector2((int)(point.X + shiftX) / 64.0f, (int)(point.Y + shiftY) / 64.0f));
        }

        public void QuadraticCurveTo (Point control, Point point) {
            var levels = curveLevels;
            var arc = bezierArc;
            arc[0] = new Vector2((int)(point.X + shiftX) / 64.0f, (int)(point.Y + shiftY) / 64.0f);
            arc[1] = new Vector2((int)(control.X + shiftX) / 64.0f, (int)(control.Y + shiftY) / 64.0f);
            arc[2] = subpixelPos;
            
            var delta = Vector2.Abs(arc[2] + arc[0] - 2 * arc[1]);
            var dx = delta.X;
            if (dx < delta.Y)
                dx = delta.Y;

            // short cut for small arcs
            if (dx < 0.25f) {
                RenderLine(arc[0]);
                return;
            }

            int level = 0;
            do {
                dx /= 4.0f;
                level++;
            } while (dx > 0.25f);

            int top = 0;
            int arcIndex = 0;
            levels[0] = level;

            while (top >= 0) {
                level = levels[top];
                if (level > 0) {
                    // split the arc
                    arc[arcIndex + 4] = arc[arcIndex + 2];
                    var b = arc[arcIndex + 1];
                    var a = arc[arcIndex + 3] = (arc[arcIndex + 2] + b) / 2;
                    b = arc[arcIndex + 1] = (arc[arcIndex] + b) / 2;
                    arc[arcIndex + 2] = (a + b) / 2;

                    arcIndex += 2;
                    top++;
                    levels[top] = levels[top - 1] = level - 1;
                }
                else {
                    RenderLine(arc[arcIndex]);
                    top--;
                    arcIndex -= 2;
                }
            }
        }

        public void BlitTo (Surface surface) {
            if (cellActive)
                RetireActiveCell();

            // if we rendered nothing, there's nothing to do
            if (cellCount == 0)
                return;

            this.surface = surface;
            var sc = scanlineCount;
            for (int y = 0; y < sc; y++) {
                var x = 0;
                var coverage = 0.0f;
                var index = scanlines[y];

                while (index != -1) {
                    var cell = cells[index];
                    if (cell.X > x && coverage != 0.0f)
                        FillHLine(x, y, (int)Math.Round(coverage * 255, MidpointRounding.AwayFromZero), cell.X - x);

                    coverage += cell.Coverage;

                    // cell.Area is in square subpixels, so we need to divide down
                    var area = coverage * 2.0f - cell.Area;
                    if (area != 0.0f && cell.X >= 0)
                        FillHLine(cell.X, y, (int)Math.Round(area * 255 / 2, MidpointRounding.AwayFromZero), 1);

                    x = cell.X + 1;
                    index = cell.Next;
                }

                if (coverage != 0.0f)
                    FillHLine(x, y, (int)Math.Round(coverage * 255, MidpointRounding.AwayFromZero), maxX - minX - x);
            }
        }

        void RenderLine (Vector2 target) {
            // figure out which scanlines this line crosses
            var startScanline = (int)subpixelPos.Y;
            var endScanline = (int)target.Y;

            // vertical clipping
            if (Math.Min(startScanline, endScanline) >= maxY ||
                Math.Max(startScanline, endScanline) < minY) {
                // just save this position since it's outside our bounds and continue
                subpixelPos = target;
                return;
            }

            // render the line
            var vector = target - subpixelPos;
            var fringeStart = subpixelPos.Y - startScanline;
            var fringeEnd = target.Y - endScanline;

            if (startScanline == endScanline) {
                // this is a horizontal line
                RenderScanline(startScanline, subpixelPos.X, fringeStart, target.X, fringeEnd);
            }
            else if (vector.X == 0) {
                // this is a vertical line
                var x = (int)subpixelPos.X;
                var xarea = (subpixelPos.X - x) * 2;

                // check if we're scanning up or down
                var first = 1.0f;
                var increment = 1;
                if (vector.Y < 0) {
                    first = 0.0f;
                    increment = -1;
                }

                // first cell fringe
                var deltaY = (first - fringeStart);
                activeArea += xarea * deltaY;
                activeCoverage += deltaY;
                startScanline += increment;
                SetCurrentCell(x, startScanline);

                // any other cells covered by the line
                deltaY = first + first - 1.0f;
                var area = xarea * deltaY;
                while (startScanline != endScanline) {
                    activeArea += area;
                    activeCoverage += deltaY;
                    startScanline += increment;
                    SetCurrentCell(x, startScanline);
                }

                // ending fringe
                deltaY = fringeEnd - 1.0f + first;
                activeArea += xarea * deltaY;
                activeCoverage += deltaY;
            }
            else {
                // diagonal line
                // check if we're scanning up or down
                var dist = (1.0f - fringeStart) * vector.X;
                var first = 1.0f;
                var increment = 1;
                if (vector.Y < 0) {
                    dist = fringeStart * vector.X;
                    first = 0.0f;
                    increment = -1;
                    vector.Y = -vector.Y;
                }

                // render the first scanline
                float delta, mod;
                FixedMath.DivMod(dist, vector.Y, out delta, out mod);

                var x = subpixelPos.X + delta;
                RenderScanline(startScanline, subpixelPos.X, fringeStart, x, first);
                startScanline += increment;
                SetCurrentCell((int)x, startScanline);

                // step along the line
                if (startScanline != endScanline) {
                    float lift, rem;
                    FixedMath.DivMod(vector.X, vector.Y, out lift, out rem);
                    mod -= vector.Y;

                    while (startScanline != endScanline) {
                        delta = lift;
                        mod += rem;
                        if (mod >= 0) {
                            mod -= vector.Y;
                            delta++;
                        }

                        var x2 = x + delta;
                        RenderScanline(startScanline, x, 1.0f - first, x2, first);
                        x = x2;

                        startScanline += increment;
                        SetCurrentCell((int)x, startScanline);
                    }
                }

                // last scanline
                RenderScanline(startScanline, x, 1.0f - first, target.X, fringeEnd);
            }

            subpixelPos = target;
        }

        void FillHLine (int x, int y, int coverage, int length) {
            // non-zero winding rule
            coverage = Math.Abs(coverage);
            if (coverage > 255)
                coverage = 255;
            if (coverage == 0)
                return;

            x += minX;
            y += minY;
            x = Math.Min(x, 32767);

            var span = new Span {
                X = x,
                Length = length,
                Coverage = coverage
            };

            BlitSpans(y, &span, 1);
        }

        void BlitSpans (int y, Span* spans, int count) {
            // find the scanline offset
            var bits = (byte*)surface.Bits - y * surface.Pitch;
            if (surface.Pitch >= 0)
                bits += (surface.Height - 1) * surface.Pitch;

            for (; count > 0; count--, spans++) {
                // finally fill pixels
                var p = bits + spans->X;
                var coverage = spans->Coverage;
                for (int i = 0; i < spans->Length; i++)
                    *p++ = (byte)coverage;
            }
        }

        void RenderScanline (int scanline, float x1, float y1, float x2, float y2) {
            var startCell = (int)x1;
            var endCell = (int)x2;
            var fringeStart = x1 - startCell;
            var fringeEnd = x2 - endCell;

            // trivial case; exact same Y, down to the subpixel
            if (y1 == y2) {
                SetCurrentCell(endCell, scanline);
                return;
            }

            // trivial case; within the same cell
            if (startCell == endCell) {
                var deltaY = y2 - y1;
                activeArea += (fringeStart + fringeEnd) * deltaY;
                activeCoverage += deltaY;
                return;
            }

            // long case: render a run of adjacent cells on the scanline
            var dx = x2 - x1;
            var dy = y2 - y1;

            // check if we're going left or right
            var dist = (1.0f - fringeStart) * dy;
            var first = 1.0f;
            var increment = 1;
            if (dx < 0) {
                dist = fringeStart * dy;
                first = 0.0f;
                increment = -1;
                dx = -dx;
            }

            // update the first cell
            float delta, mod;
            FixedMath.DivMod(dist, dx, out delta, out mod);
            activeArea += (fringeStart + first) * delta;
            activeCoverage += delta;

            startCell += increment;
            SetCurrentCell(startCell, scanline);
            y1 += delta;

            // update all covered cells
            if (startCell != endCell) {
                dist = y2 - y1 + delta;
                float lift, rem;
                FixedMath.DivMod(dist, dx, out lift, out rem);
                mod -= dx;

                while (startCell != endCell) {
                    delta = lift;
                    mod += rem;
                    if (mod >= 0) {
                        mod -= dx;
                        delta++;
                    }

                    activeArea += delta;
                    activeCoverage += delta;
                    y1 += delta;
                    startCell += increment;
                    SetCurrentCell(startCell, scanline);
                }
            }

            // final cell
            delta = y2 - y1;
            activeArea += (fringeEnd + 1.0f - first) * delta;
            activeCoverage += delta;
        }

        void SetCurrentCell (int x, int y) {
            // all cells on the left of the clipping region go to the minX - 1 position
            y -= minY;
            x = Math.Min(x, maxX);
            x -= minX;
            x = Math.Max(x, -1);

            // moving to a new cell?
            if (x != cellX || y != cellY) {
                if (cellActive)
                    RetireActiveCell();

                activeArea = 0.0f;
                activeCoverage = 0.0f;
                cellX = x;
                cellY = y;
            }

            cellActive = cellX < maxX && cellY < maxY;
        }

        void RetireActiveCell () {
            // cells with no coverage have nothing to do
            if (activeArea == 0.0f && activeCoverage == 0.0f)
                return;

            // find the right spot to add or insert this cell
            var x = cellX;
            var y = cellY;
            var cell = scanlines[y];
            if (cell == -1 || cells[cell].X > x) {
                // no cells at all on this scanline yet, or the first one
                // is already beyond our X value, so grab a new one
                cell = GetNewCell(x, cell);
                scanlines[y] = cell;
                return;
            }

            while (cells[cell].X != x) {
                var next = cells[cell].Next;
                if (next == -1 || cells[next].X > x) {
                    // either we reached the end of the chain in this
                    // scanline, or the next cell has a larger X
                    next = GetNewCell(x, next);
                    cells[cell].Next = next;
                    return;
                }

                // move to next cell
                cell = next;
            }

            // we found a cell with identical coords, so adjust its coverage
            cells[cell].Area += activeArea;
            cells[cell].Coverage += activeCoverage;
        }

        int GetNewCell (int x, int next) {
            // resize our array if we've run out of room
            if (cellCount == cells.Length)
                Array.Resize(ref cells, (int)(cells.Length * 1.5));

            var index = cellCount++;
            cells[index].X = x;
            cells[index].Next = next;
            cells[index].Area = activeArea;
            cells[index].Coverage = activeCoverage;

            return index;
        }

        struct Cell {
            public int X;
            public int Next;
            public float Coverage;
            public float Area;
        }

        struct Span {
            public int X;
            public int Length;
            public int Coverage;
        }
    }
}
