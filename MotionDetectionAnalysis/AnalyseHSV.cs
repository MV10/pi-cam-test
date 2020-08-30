using System;
using System.Threading.Tasks;

namespace MMALSharp.Processors.Motion
{
    // Step 4
    // HSV

    public class AnalyseHSV : FrameDiffAnalysisBase, IFrameDiffAlgorithm
    {
        // readonly is thread safe


        private readonly int CellPixelPercentage = 50;
        private readonly int CellPixelThreshold = 150; // cells have 300 pixels at 640x480 / 32 = 20x15 per cell, this is 50%

        private readonly int CellCountThreshold = 20;

        private Action<byte[]> _writeCallback;
        private byte[] _analysisBuffer;

        public AnalyseHSV(Action<byte[]> writeProcessedFrameCallback)
        {
            _writeCallback = writeProcessedFrameCallback;
        }

        public void FirstFrameCompleted(FrameDiffDriver buffer, FrameDiffMetrics metrics)
        {
            _analysisBuffer = new byte[buffer.TestFrame.Length];
            // not necessary for this analysis, CheckDiff overwrites the buffer completely
            // Array.Copy(buffer.TestFrame, _analysisBuffer, _analysisBuffer.Length);
            _writeCallback(buffer.TestFrame);
        }

        public bool AnalyseFrames(FrameDiffDriver buffer, FrameDiffMetrics metrics)
        {
            Parallel.ForEach(buffer.CellDiff, (cell, loopState, loopIndex)
                => CheckDiff(loopIndex, buffer, metrics, loopState));

            int diff = 0;
            foreach (var cellDiff in buffer.CellDiff)
            {
                diff += cellDiff;
            }

            var detected = (diff > CellCountThreshold);

            // red indicates motion, green indicates no motion
            if (diff > 0)
            {
                int x2 = (int)((diff / 1024f) * 639f); // draw a bar across the screen
                if (detected)
                {
                    DrawIndicator(255, 0, 0, 0, x2, 0, 10, _analysisBuffer, metrics);
                }
                else
                {
                    DrawIndicator(0, 255, 0, 0, x2, 0, 10, _analysisBuffer, metrics);
                }
            }

            // changes color each time the test frame is updated
            if (metrics.Analysis_TestFrameUpdated)
            {
                DrawIndicator(255, 0, 255, 148, 167, 459, 479, _analysisBuffer, metrics);
            }
            else
            {
                DrawIndicator(0, 255, 255, 148, 167, 459, 479, _analysisBuffer, metrics);
            }

            _writeCallback(_analysisBuffer);

            return detected;
        }

        public void ResetAnalyser(FrameDiffDriver buffer, FrameDiffMetrics metrics)
        { } // no need to reset the analysis buffer

        // FrameDiffMetrics is a structure; it is a by-value copy and all fields are value types which makes it thread safe
        protected virtual void CheckDiff(long cellIndex, FrameDiffDriver buffer, FrameDiffMetrics metrics, ParallelLoopState loopState)
        {
            int diff = 0;
            var rect = buffer.CellRect[cellIndex];

            int x2 = rect.X + rect.Width;
            int y2 = rect.Y + rect.Height;

            for (var col = rect.X; col < x2; col++)
            {
                for (var row = rect.Y; row < y2; row++)
                {
                    var index = (col * metrics.FrameBpp) + (row * metrics.FrameStride);

                    // Ignore the mask for analysis purposes

                    byte r = buffer.TestFrame[index];
                    byte g = buffer.TestFrame[index + 1];
                    byte b = buffer.TestFrame[index + 2];
                    var hsv1 = RGBToHSV(r, g, b);

                    r = buffer.CurrentFrame[index];
                    g = buffer.CurrentFrame[index + 1];
                    b = buffer.CurrentFrame[index + 2];
                    var hsv2 = RGBToHSV(r, g, b);

                    // variance in hue
                    var hueVariance = 30f / 360f;
                    var hueDiff = Math.Abs(hsv1.h - hsv2.h);

                    if(hueDiff > hueVariance)
                    {
                        // leave pixel on
                    }
                    else
                    {
                        r = 0;
                        g = 0;
                        b = 0;
                    }

                    //if (score > MinScore)
                    //{
                    //    diff++;

                    //    // set diff pixels to full color
                    //    r = buffer.CurrentFrame[index];
                    //    g = buffer.CurrentFrame[index + 1];
                    //    b = buffer.CurrentFrame[index + 2];
                    //}
                    //else
                    //{
                    //    // set non-diff pixels to dim monochrome
                    //    byte dimgray = (byte)((Grayscale(r, g, b)) / 3);
                    //    r = dimgray;
                    //    g = dimgray;
                    //    b = dimgray;
                    //}

                    //(r, g, b) = HSVToRGB(hsv2.h, hsv2.s, hsv2.v);
                    //(r, g, b) = HSVToRGB(hsv2.h, 0.5f, 0.5f);

                    // highlight cell corners
                    if ((col == rect.X || col == x2 - 1) && (row == rect.Y || row == y2 - 1))
                    {
                        r = 128;
                        g = 0;
                        b = 128;
                    }

                    _analysisBuffer[index] = r;
                    _analysisBuffer[index + 1] = g;
                    _analysisBuffer[index + 2] = b;

                    // No early exit for analysis purposes
                }
            }

            buffer.CellDiff[cellIndex] = (diff >= CellPixelThreshold) ? 1 : 0;
        }
    }
}
