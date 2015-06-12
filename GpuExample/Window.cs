using SharpBgfx;
using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace GpuExample {
    public class Window {
        EventQueue eventQueue = new EventQueue();
        Form form;
        Thread thread;

        public int Width {
            get;
            private set;
        }

        public int Height {
            get;
            private set;
        }

        public Window (string name, int width, int height) {
            Width = width;
            Height = height;

            form = new Form {
                Text = name,
                ClientSize = new Size(width, height)
            };

            form.ClientSizeChanged += (o, e) => eventQueue.Post(new SizeEvent(width, height));
            form.FormClosing += OnFormClosing;
            form.FormClosed += (o, e) => eventQueue.Post(new Event(EventType.Exit));

            Bgfx.SetWindowHandle(form.Handle);
        }

        public void Run (Action<Window> renderThread) {
            thread = new Thread(() => renderThread(this));
            thread.Start();

            Application.Run(form);
        }

        public bool ProcessEvents (ResetFlags resetFlags) {
            Event ev;
            bool resizeRequired = false;

            while ((ev = eventQueue.Poll()) != null) {
                switch (ev.Type) {
                    case EventType.Exit:
                        return false;

                    case EventType.Size:
                        var size = (SizeEvent)ev;
                        Width = size.Width;
                        Height = size.Height;
                        resizeRequired = true;
                        break;
                }
            }

            if (resizeRequired)
                Bgfx.Reset(Width, Height, resetFlags);

            return true;
        }

        void OnFormClosing (object sender, FormClosingEventArgs e) {
            // kill all rendering and shutdown before closing the
            // window, or we'll get errors from the graphics driver
            eventQueue.Post(new Event(EventType.Exit));
            thread.Join();
        }

        class EventQueue {
            ConcurrentQueue<Event> queue = new ConcurrentQueue<Event>();

            public void Post (Event ev) => queue.Enqueue(ev);

            public Event Poll () {
                Event ev;
                if (queue.TryDequeue(out ev))
                    return ev;

                return null;
            }
        }

        enum EventType {
            Exit,
            Key,
            Mouse,
            Size
        }

        class Event {
            public readonly EventType Type;

            public Event (EventType type) {
                Type = type;
            }
        }

        class SizeEvent : Event {
            public readonly int Width;
            public readonly int Height;

            public SizeEvent (int width, int height)
                : base(EventType.Size) {

                Width = width;
                Height = height;
            }
        }
    }
}
