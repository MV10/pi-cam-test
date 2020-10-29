using MMALSharp.Processors.Effects;

namespace pi_cam_test
{
    public struct VignetteMetadata : IPixelProcessorMetadata
    {
        // Pre-calculate expensive metadata outside of the parallel processing delegate.

        public double centerX;
        public double centerY;
        public double centerDist;

        // C# structs do not support inheritance, so the following must be cut-and-pasted
        // from the library's PixelProcessorMetadata source code:

        public int width;
        public int height;
        public int x;
        public int y;
        public int r;
        public int g;
        public int b;

        public int Width { set => width = value; }
        public int Height { set => height = value; }
        public int X { set => x = value; }
        public int Y { set => y = value; }
        public int R { set => r = value; }
        public int G { set => g = value; }
        public int B { set => b = value; }
    }
}
