using MMALSharp.Common;
using MMALSharp.Common.Utility;
using System;
using System.Drawing;
using System.Threading.Tasks;

// Contains generally-useful utility methods.

namespace MMALSharp.Processors.Motion
{
    public abstract class FrameDiffAnalysisBase
    {
        // Sets pixels in the top corner to a specified color
        public void DrawIndicator(byte r, byte g, byte b, int x1, int x2, int y1, int y2, byte[] buffer, FrameDiffMetrics metrics)
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

        public void HighlightCell(byte r, byte g, byte b, FrameDiffDriver driver, FrameDiffMetrics metrics, int index, byte[] buffer)
        {
            for(int x = driver.CellRect[index].X; x < driver.CellRect[index].X + driver.CellRect[index].Width; x++)
            {
                var y = driver.CellRect[index].Y;
                var i = (x * metrics.FrameBpp) + (y * metrics.FrameStride);
                buffer[i] = r;
                buffer[i + 1] = g;
                buffer[i + 2] = b;
                y += driver.CellRect[index].Height - 1;
                i = (x * metrics.FrameBpp) + (y * metrics.FrameStride);
                buffer[i] = r;
                buffer[i + 1] = g;
                buffer[i + 2] = b;
            }
            for(int y = driver.CellRect[index].Y; y < driver.CellRect[index].Y + driver.CellRect[index].Height; y++)
            {
                var x = driver.CellRect[index].X;
                var i = (x * metrics.FrameBpp) + (y * metrics.FrameStride);
                buffer[i] = r;
                buffer[i + 1] = g;
                buffer[i + 2] = b;
                x += driver.CellRect[index].Width - 1;
                i = (x * metrics.FrameBpp) + (y * metrics.FrameStride);
                buffer[i] = r;
                buffer[i + 1] = g;
                buffer[i + 2] = b;
            }
        }

        // move to MMALSharp utils color class
        public byte Grayscale(byte r, byte g, byte b)
            => (byte)((int)(r * 0.2989f) + (int)(g * 0.587f) + (int)(b * 0.114f));

        public (float h, float s, float v) RGBToHSV(byte r, byte g, byte b)
        {
            //var hsv = MMALColor.RGBToHSV(Color.FromArgb(r, g, b));

            // normalize
            float rf = r.ToFloat();
            float gf = g.ToFloat();
            float bf = b.ToFloat();

            float maxc = Math.Max(rf, Math.Max(gf, bf));
            float minc = Math.Min(rf, Math.Min(gf, bf));

            if (minc == maxc)
            {
                return (0, 0, 0);
            }

            float v = maxc;

            float s = (maxc - minc) / maxc;

            float rc = (maxc - rf) / (maxc - minc);
            float gc = (maxc - gf) / (maxc - minc);
            float bc = (maxc - bf) / (maxc - minc);

            float h;
            if (rf == maxc)
            {
                h = bc - gc;
            }
            else if (gf == maxc)
            {
                h = 2.0f + rc - bc;
            }
            else
            {
                h = 4.0f + gc - rc;
            }

            h = (h / 6.0f) % 1.0f;

            return (
                Math.Max(0, Math.Min(1, h)),
                Math.Max(0, Math.Min(1, s)),
                Math.Max(0, Math.Min(1, v)));
        }

        public (byte r, byte g, byte b) HSVToRGB(float h, float s, float v)
        {
            if (s == 0.0f) return (v.ToByte(), v.ToByte(), v.ToByte());

            int i = (int)(h * 6);
            float f = (h * 6.0f) - i;
            float p = v * (1.0f - s);
            float q = v * (1.0f - (s * f));
            float t = v * (1.0f - (s * (1.0f - f)));

            i %= 6;

            if (i == 0) return (v.ToByte(), t.ToByte(), p.ToByte());
            if (i == 1) return (q.ToByte(), v.ToByte(), p.ToByte());
            if (i == 2) return (p.ToByte(), v.ToByte(), t.ToByte());
            if (i == 3) return (p.ToByte(), q.ToByte(), v.ToByte());
            if (i == 4) return (t.ToByte(), p.ToByte(), v.ToByte());
            if (i == 5) return (v.ToByte(), p.ToByte(), q.ToByte());

            throw new Exception("Calculated invalid HSV");
        }

    }
}
