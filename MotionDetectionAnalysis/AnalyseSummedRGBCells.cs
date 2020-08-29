using System;
using System.Threading.Tasks;

namespace MMALSharp.Processors.Motion
{
    // Step 3
    // This is a variation on the MMALSharp algorithm to address the Threshold problem.
    // Grayscale suppresses some of the noise. Still too sensitive to shadows.

    // Instead of referencing the config Threshold, this uses additional values:
    // RGBThreshold = summed RGB diff (still probably not a good measure)
    // CellPixelPercentage = number of diff pixels per cell to consider the cell changed
    // CellCountThreshold = number of cells with diffs to trigger motion detection


    public class AnalyseSummedRGBCells : FrameDiffAnalysisBase, IFrameDiffAlgorithm
    {
        // readonly is thread safe
        private readonly int RGBThreshold = 130;       // 640x480 @ 32 divs = 1024 cells @ 20x15 = 300 pixels each
        private readonly int CellPixelPercentage = 50; // percentage of pixels in the cell to mark the cell as changed
        private readonly int CellCountThreshold = 20;  // number of cells with diffs to trigger motion detection

        private readonly int CellPixelThreshold = (int)((640f / 32f) * (480f / 32f) * (50f / 100f)); // (last bit is CellPixelPercentage)

        private Action<byte[]> _writeCallback;
        private byte[] _analysisBuffer;

        public AnalyseSummedRGBCells(Action<byte[]> writeProcessedFrameCallback)
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
                    byte gs = Grayscale(r, g, b);
                    var rgb1 = gs * 3;

                    r = buffer.CurrentFrame[index];
                    g = buffer.CurrentFrame[index + 1];
                    b = buffer.CurrentFrame[index + 2];
                    gs = Grayscale(r, g, b);
                    var rgb2 = gs * 3;

                    var rgbDiff = Math.Abs(rgb2 - rgb1);

                    if (rgbDiff > RGBThreshold)
                    {
                        diff++;

                        // set diff pixels to full color
                        r = buffer.CurrentFrame[index];
                        g = buffer.CurrentFrame[index + 1];
                        b = buffer.CurrentFrame[index + 2];
                    }
                    else
                    {
                        // set non-diff pixels to dim monochrome
                        byte dimgray = (byte)((Grayscale(r, g, b)) / 3);
                        r = dimgray;
                        g = dimgray;
                        b = dimgray;
                    }

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
