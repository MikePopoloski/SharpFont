using SharpBgfx;

namespace GpuExample {
    static class Program {
        static void Main () {
            // create a platform window and kick off a separate render thread
            var window = new Window("Text Rendering Example", 1280, 720);
            window.Run(RenderThread);
        }

        static void RenderThread (Window window) {
            // initialize the renderer
            Bgfx.Init();
            Bgfx.Reset(window.Width, window.Height, ResetFlags.Vsync);
            Bgfx.SetDebugFeatures(DebugFeatures.DisplayText);
            Bgfx.SetViewClear(0, ClearTargets.Color | ClearTargets.Depth, 0x303030ff);

            // main loop
            while (window.ProcessEvents(ResetFlags.Vsync)) {
                Bgfx.SetViewRect(0, 0, 0, window.Width, window.Height);
                Bgfx.Submit(0);
                Bgfx.Frame();
            }

            // cleanup
            Bgfx.Shutdown();
        }
    }
}
