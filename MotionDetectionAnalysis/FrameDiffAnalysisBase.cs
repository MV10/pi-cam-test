using System;
using System.Threading.Tasks;

// Contains generally-useful utility methods.

namespace MMALSharp.Processors.Motion
{
    public abstract class FrameDiffAnalysisBase
    {

        // Sets pixels in the top corner to a specified color
        protected void DrawIndicator(byte r, byte g, byte b, int x1, int x2, int y1, int y2, byte[] buffer, FrameDiffMetrics metrics)
        {
            for (int x = x1; x <= x2; x++)
            {
                for (int y = y1; y <= y2; y++)
                {
                    var i = (x * metrics.FrameBpp) + (y * metrics.FrameStride);
                    buffer[i] = r;
                    buffer[i + 1] = g;
                    buffer[i + 2] = b;
                }
            }
        }

        // move to MMALSharp utils color class
        protected byte Grayscale(byte r, byte g, byte b)
            => (byte)((int)(r * 0.2989f) + (int)(g * 0.587f) + (int)(b * 0.114f));
    }
}
