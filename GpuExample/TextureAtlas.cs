using SharpBgfx;
using System;
using System.Numerics;

namespace GpuExample {
    class TextureAtlas : IDisposable {
        BinPacker packer;
        Texture texture;
        ResizableArray<Vector4> regions;

        public TextureAtlas (int size) {
            packer = new BinPacker(size, size);
            texture = Texture.Create2D(size, size, 1, TextureFormat.R8);
            regions = new ResizableArray<Vector4>(256);
        }

        public unsafe int AddRegion (int width, int height, IntPtr source) {
            // try to find a spot for the region
            var rect = packer.Insert(width, height);
            if (rect.Height == 0)
                return -1;

            var count = width * height;
            var mem = new MemoryBlock(count);
            var dest = mem.Data;

            if (rect.Width == width)
                Buffer.MemoryCopy((void*)source, (void*)dest, count, count);
            else {
                // the packer can flip the region 90 degrees, so handle that when blitting
                var sourcePtr = (byte*)source;
                var destPtrBase = (byte*)dest;
                for (int y = 0; y < height; y++) {
                    var destPtr = destPtrBase + height - y;
                    for (int x = 0; x < width; x++) {
                        *destPtr = *sourcePtr++;
                        destPtr += width;
                    }
                }
            }

            texture.Update2D(0, rect.X, rect.Y, rect.Width, rect.Height, mem, rect.Width);

            var pageWidth = (float)texture.Width;
            var pageHeight = (float)texture.Height;
            var region = new Vector4(
                rect.X / pageWidth,
                rect.Y / pageHeight,
                rect.Width / pageWidth,
                rect.Height / pageHeight
            );

            regions.Add(region);
            return regions.Count - 1;
        }

        public void Dispose () {
            texture.Dispose();
        }
    }

    struct Rect {
        public int X, Y, Width, Height;

        public int Right => X + Width;
        public int Bottom => Y + Height;

        public Rect (int x, int y, int width, int height) {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public bool Contains (Rect rect) {
            return rect.X >= X && rect.Y >= Y &&
                   rect.Right <= Right && rect.Bottom <= Bottom;
        }
    }

    struct ResizableArray<T> {
        public T[] Data;
        public int Count;

        public T this[int index] => Data[index];

        public ResizableArray (int capacity) {
            Data = new T[capacity];
            Count = 0;
        }

        public void Add (T value) {
            if (Count == Data.Length)
                Array.Resize(ref Data, (int)(Data.Length * 1.5));
            Data[Count++] = value;
        }

        public void RemoveAt (int index) {
            Count--;
            if (index < Count)
                Array.Copy(Data, index + 1, Data, index, Count - index);
        }
    }

    // based on the "MAXRECTS" method developed by Jukka Jylänki: http://clb.demon.fi/files/RectangleBinPack.pdf
    class BinPacker {
        ResizableArray<Rect> freeList;

        public BinPacker (int width, int height) {
            freeList = new ResizableArray<Rect>(16);
            freeList.Add(new Rect(0, 0, width, height));
        }

        public Rect Insert (int width, int height) {
            var bestNode = new Rect();
            var bestShortFit = int.MaxValue;
            var bestLongFit = int.MaxValue;

            var count = freeList.Count;
            for (int i = 0; i < count; i++) {
                // first try to place the rectangle in upright orientation
                var rect = freeList[i];
                TryFit(ref rect, width, height, ref bestNode, ref bestShortFit, ref bestLongFit);

                // now try it flipped over
                if (rect.Width >= height && rect.Height >= width) {
                    var leftoverX = Math.Abs(rect.Width - height);
                    var leftoverY = Math.Abs(rect.Height - width);
                    var shortFit = Math.Min(leftoverX, leftoverY);
                    var longFit = Math.Max(leftoverX, leftoverY);

                    if (shortFit < bestShortFit || (shortFit == bestShortFit && longFit < bestLongFit)) {
                        bestNode = new Rect(rect.X, rect.Y, width, height);
                        bestShortFit = shortFit;
                        bestLongFit = longFit;
                    }
                }
            }

            if (bestNode.Height == 0)
                return bestNode;

            for (int i = 0; i < count; i++) {
                if (SplitFreeNode(freeList[i], bestNode)) {
                    freeList.RemoveAt(i);
                    i--;
                    count--;
                }
            }

            PruneFreeList();
            return bestNode;
        }

        bool SplitFreeNode (Rect freeNode, Rect usedNode) {
            // test if the rects even intersect
            var insideX = usedNode.X < freeNode.Right && usedNode.Right > freeNode.X;
            var insideY = usedNode.Y < freeNode.Bottom && usedNode.Bottom > freeNode.Y;
            if (!insideX || !insideY)
                return false;

            if (insideX) {
                // new node at the top side of the used node
                if (usedNode.Y > freeNode.Y && usedNode.Y < freeNode.Bottom) {
                    var newNode = freeNode;
                    newNode.Height = usedNode.Y - newNode.Y;
                    freeList.Add(newNode);
                }

                // new node at the bottom side of the used node
                if (usedNode.Bottom < freeNode.Bottom) {
                    var newNode = freeNode;
                    newNode.Y = usedNode.Bottom;
                    newNode.Height = freeNode.Bottom - usedNode.Bottom;
                    freeList.Add(newNode);
                }
            }

            if (insideY) {
                // new node at the left side of the used node
                if (usedNode.X > freeNode.X && usedNode.X < freeNode.Right) {
                    var newNode = freeNode;
                    newNode.Width = usedNode.X - newNode.X;
                    freeList.Add(newNode);
                }

                // new node at the right side of the used node
                if (usedNode.Right < freeNode.Right) {
                    var newNode = freeNode;
                    newNode.X = usedNode.Right;
                    newNode.Width = freeNode.Right - usedNode.Right;
                    freeList.Add(newNode);
                }
            }

            return true;
        }

        void PruneFreeList () {
            for (int i = 0; i < freeList.Count; i++) {
                for (int j = i + 1; j < freeList.Count; j++) {
                    var idata = freeList[i];
                    var jdata = freeList[j];
                    if (jdata.Contains(idata)) {
                        freeList.RemoveAt(i);
                        i--;
                        break;
                    }

                    if (idata.Contains(jdata)) {
                        freeList.RemoveAt(j);
                        j--;
                    }
                }
            }
        }

        static void TryFit (ref Rect rect, int width, int height, ref Rect bestNode, ref int bestShortFit, ref int bestLongFit) {
            if (rect.Width < width || rect.Height < height)
                return;

            var leftoverX = Math.Abs(rect.Width - width);
            var leftoverY = Math.Abs(rect.Height - height);
            var shortFit = Math.Min(leftoverX, leftoverY);
            var longFit = Math.Max(leftoverX, leftoverY);

            if (shortFit < bestShortFit || (shortFit == bestShortFit && longFit < bestLongFit)) {
                bestNode = new Rect(rect.X, rect.Y, width, height);
                bestShortFit = shortFit;
                bestLongFit = longFit;
            }
        }
    }
}
