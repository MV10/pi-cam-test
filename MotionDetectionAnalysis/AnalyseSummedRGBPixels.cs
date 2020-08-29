using System;
using System.Threading.Tasks;

namespace MMALSharp.Processors.Motion
{
    // This is a variation on the MMALSharp algorithm to address the Threshold problem.
    // Instead of referencing the config Threshold, this uses two values:
    // PixelThreshold = summed RGB diff (still probably not a good measure)
    // FrameThreshold = number of diffed pixels per frame


    public class AnalyseSummedRGBPixels : FrameDiffAnalysisBase, IFrameDiffAlgorithm
    {
        // readonly is thread safe
        private readonly int PixelThreshold = 130;  // 640x480 @ 32 divs = 1024 cells @ 20x15 = 300 pixels each
        private readonly int FrameThreshold = 1500; // 640x480 = 307,200 pixels

        private Action<byte[]> _writeCallback;
        private byte[] _analysisBuffer;

        public AnalyseSummedRGBPixels(Action<byte[]> writeProcessedFrameCallback)
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

            var detected = (diff > FrameThreshold);

            // red indicates motion, green indicates no motion
            if (detected)
            {
                DrawIndicator(255, 0, 0, 108, 127, 459, 479, _analysisBuffer, metrics);
            }
            else
            {
                DrawIndicator(0, 255, 0, 108, 127, 459, 479, _analysisBuffer, metrics);
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

                    var rgb1 = buffer.TestFrame[index] + buffer.TestFrame[index + 1] + buffer.TestFrame[index + 2];

                    byte r2 = buffer.CurrentFrame[index];
                    byte g2 = buffer.CurrentFrame[index + 1];
                    byte b2 = buffer.CurrentFrame[index + 2];
                    var rgb2 = r2 + g2 + b2;

                    if (rgb2 - rgb1 > PixelThreshold)
                    {
                        diff++;
                    }
                    else
                    {
                        // set non-diff pixels to dim monochrome
                        byte g = (byte)((Grayscale(r2, g2, b2)) / 3);
                        r2 = g;
                        g2 = g;
                        b2 = g;
                    }

                    // highlight cell corners
                    if ((col == rect.X || col == x2 - 1) && (row == rect.Y || row == y2 - 1))
                    {
                        r2 = 128;
                        g2 = 0;
                        b2 = 128;
                    }

                    _analysisBuffer[index] = r2;
                    _analysisBuffer[index + 1] = g2;
                    _analysisBuffer[index + 2] = b2;

                    // No early exit for analysis purposes
                }
            }

            buffer.CellDiff[cellIndex] = diff;
        }
    }
}
