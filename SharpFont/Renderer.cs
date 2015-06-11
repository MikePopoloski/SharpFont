using System;

namespace SlimFont {
    // handles rasterizing curves to a bitmap
    // the algorithm is heavily inspired by the FreeType2 renderer; thanks guys!
    unsafe class Renderer {
        Surface surface;                // the surface we're currently rendering to
        int[] scanlines;                // one scanline per Y, points into cell buffer
        int[] curveLevels;
        V24Dot8[] bezierArc;            // points on a bezier arc
        Cell[] cells;
        F26Dot6 shiftX, shiftY;         // the amount to translate incoming points
        V24Dot8 subpixelPos;            // subpixel position of active point
        int activeArea;                 // running total of the active cell's area
        int activeCoverage;             // ditto for coverage
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
            bezierArc = new V24Dot8[curveLevels.Length * 3 + 1];
        }

        public void Clear () {
            scanlineCount = 0;
            cellCount = 0;
            activeArea = 0;
            activeCoverage = 0;
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
            subpixelPos = new V24Dot8(point.X + shiftX, point.Y + shiftY);
            cellX = Math.Max(minX - 1, Math.Min(subpixelPos.X.IntPart, maxX)) - minX;
            cellY = subpixelPos.Y.IntPart - minY;

            // activate if this is a valid cell location
            cellActive = cellX < maxX && cellY < maxY;
            activeArea = 0;
            activeCoverage = 0;
        }

        public void LineTo (Point point) {
            RenderLine(new V24Dot8(point.X + shiftX, point.Y + shiftY));
        }

        public void QuadraticCurveTo (Point control, Point point) {
            var levels = curveLevels;
            var arc = bezierArc;
            arc[0] = new V24Dot8(point.X + shiftX, point.Y + shiftY);
            arc[1] = new V24Dot8(control.X + shiftX, control.Y + shiftY);
            arc[2] = subpixelPos;

            var delta = FixedMath.Abs(arc[2] + arc[0] - 2 * arc[1]);
            var dx = (int)delta.X;
            if (dx < (int)delta.Y)
                dx = (int)delta.Y;

            int level = 0;
            do {
                dx >>= 2;
                level++;
            } while (dx > (int)F24Dot8.One / 4);

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
                var coverage = 0;
                var index = scanlines[y];

                while (index != -1) {
                    var cell = cells[index];
                    if (cell.X > x && coverage != 0)
                        FillHLine(x, y, coverage, cell.X - x);

                    coverage += cell.Coverage;

                    // cell.Area is in square subpixels, so we need to divide down
                    var area = ((coverage << 9) - cell.Area) >> 9;
                    if (area != 0 && cell.X >= 0)
                        FillHLine(cell.X, y, area, 1);

                    x = cell.X + 1;
                    index = cell.Next;
                }

                if (coverage != 0)
                    FillHLine(x, y, coverage, maxX - minX - x);
            }
        }

        void RenderLine (V24Dot8 target) {
            // figure out which scanlines this line crosses
            var startScanline = subpixelPos.Y.IntPart;
            var endScanline = target.Y.IntPart;

            // vertical clipping
            if (Math.Min(startScanline, endScanline) >= maxY ||
                Math.Max(startScanline, endScanline) < minY) {
                // just save this position since it's outside our bounds and continue
                subpixelPos = target;
                return;
            }

            // render the line
            var vector = target - subpixelPos;
            var fringeStart = subpixelPos.Y.FracPart;
            var fringeEnd = target.Y.FracPart;

            if (startScanline == endScanline) {
                // this is a horizontal line
                RenderScanline(startScanline, subpixelPos.X, fringeStart, target.X, fringeEnd);
            }
            else if ((int)vector.X == 0) {
                // this is a vertical line
                var xarea = (int)subpixelPos.X.FracPart << 1;
                var x = subpixelPos.X.IntPart;

                // check if we're scanning up or down
                var first = F24Dot8.One;
                var increment = 1;
                if ((int)vector.Y < 0) {
                    first = F24Dot8.Zero;
                    increment = -1;
                }

                // first cell fringe
                var deltaY = (int)(first - fringeStart);
                activeArea += xarea * deltaY;
                activeCoverage += deltaY;
                startScanline += increment;
                SetCurrentCell(x, startScanline);

                // any other cells covered by the line
                deltaY = (int)(first + first - F24Dot8.One);
                var area = xarea * deltaY;
                while (startScanline != endScanline) {
                    activeArea += area;
                    activeCoverage += deltaY;
                    startScanline += increment;
                    SetCurrentCell(x, startScanline);
                }

                // ending fringe
                deltaY = (int)(fringeEnd - F24Dot8.One + first);
                activeArea += xarea * deltaY;
                activeCoverage += deltaY;
            }
            else {
                // diagonal line
                // check if we're scanning up or down
                var dist = (F24Dot8.One - fringeStart) * vector.X;
                var first = F24Dot8.One;
                var increment = 1;
                if ((int)vector.Y < 0) {
                    dist = fringeStart * vector.X;
                    first = F24Dot8.Zero;
                    increment = -1;
                    vector.Y = -vector.Y;
                }

                // render the first scanline
                F24Dot8 delta, mod;
                FixedMath.DivMod(dist, vector.Y, out delta, out mod);

                var x = subpixelPos.X + delta;
                RenderScanline(startScanline, subpixelPos.X, fringeStart, x, first);
                startScanline += increment;
                SetCurrentCell(x.IntPart, startScanline);

                // step along the line
                if (startScanline != endScanline) {
                    F24Dot8 lift, rem;
                    FixedMath.DivMod(F24Dot8.One * vector.X, vector.Y, out lift, out rem);
                    mod -= vector.Y;

                    while (startScanline != endScanline) {
                        delta = lift;
                        mod += rem;
                        if ((int)mod >= 0) {
                            mod -= vector.Y;
                            delta++;
                        }

                        var x2 = x + delta;
                        RenderScanline(startScanline, x, F24Dot8.One - first, x2, first);
                        x = x2;

                        startScanline += increment;
                        SetCurrentCell(x.IntPart, startScanline);
                    }
                }

                // last scanline
                RenderScanline(startScanline, x, F24Dot8.One - first, target.X, fringeEnd);
            }

            subpixelPos = target;
        }

        void FillHLine (int x, int y, int coverage, int length) {
            if (coverage < 0)
                coverage = -coverage;

            // non-zero winding rule
            if (coverage >= 256)
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
                var coverage = spans->Coverage;
                if (coverage == 0)
                    continue;

                // finally fill pixels
                var p = bits + spans->X;
                for (int i = 0; i < spans->Length; i++)
                    *p++ = (byte)coverage;
            }
        }

        void RenderScanline (int scanline, F24Dot8 x1, F24Dot8 y1, F24Dot8 x2, F24Dot8 y2) {
            var startCell = x1.IntPart;
            var endCell = x2.IntPart;
            var fringeStart = x1.FracPart;
            var fringeEnd = x2.FracPart;

            // trivial case; exact same Y, down to the subpixel
            if (y1 == y2) {
                SetCurrentCell(endCell, scanline);
                return;
            }

            // trivial case; within the same cell
            if (startCell == endCell) {
                var deltaY = (int)(y2 - y1);
                activeArea += ((int)fringeStart + (int)fringeEnd) * deltaY;
                activeCoverage += deltaY;
                return;
            }

            // long case: render a run of adjacent cells on the scanline
            var dx = x2 - x1;
            var dy = y2 - y1;

            // check if we're going left or right
            var dist = (F24Dot8.One - fringeStart) * dy;
            var first = F24Dot8.One;
            var increment = 1;
            if ((int)dx < 0) {
                dist = fringeStart * dy;
                first = F24Dot8.Zero;
                increment = -1;
                dx = -dx;
            }

            // update the first cell
            F24Dot8 delta, mod;
            FixedMath.DivMod(dist, dx, out delta, out mod);
            activeArea += (int)((fringeStart + first) * delta);
            activeCoverage += (int)delta;

            startCell += increment;
            SetCurrentCell(startCell, scanline);
            y1 += delta;

            // update all covered cells
            if (startCell != endCell) {
                dist = F24Dot8.One * (y2 - y1 + delta);
                F24Dot8 lift, rem;
                FixedMath.DivMod(dist, dx, out lift, out rem);
                mod -= dx;

                while (startCell != endCell) {
                    delta = lift;
                    mod += rem;
                    if ((int)mod >= 0) {
                        mod -= dx;
                        delta++;
                    }

                    activeArea += (int)(F24Dot8.One * delta);
                    activeCoverage += (int)delta;
                    y1 += delta;
                    startCell += increment;
                    SetCurrentCell(startCell, scanline);
                }
            }

            // final cell
            delta = y2 - y1;
            activeArea += (int)((fringeEnd + F24Dot8.One - first) * delta);
            activeCoverage += (int)delta;
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

                activeArea = 0;
                activeCoverage = 0;
                cellX = x;
                cellY = y;
            }

            cellActive = cellX < maxX && cellY < maxY;
        }

        void RetireActiveCell () {
            // cells with no coverage have nothing to do
            if ((activeArea | activeCoverage) == 0)
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
            public int Coverage;
            public int Area;
        }

        struct Span {
            public int X;
            public int Length;
            public int Coverage;
        }
    }
}
