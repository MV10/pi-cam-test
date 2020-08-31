using MMALSharp.Common;
using System;
using System.Threading.Tasks;

namespace MMALSharp.Processors.Motion
{
    // Step 5
    // Normalized RGB seems to discard too much detail

    public class AnalyseNormalizedRGBCells : FrameDiffAnalysisBase, IFrameDiffAlgorithm
    {
        // readonly is thread safe
        private readonly int CellPixelPercentage = 50;
        private readonly int CellPixelThreshold = 150; // cells have 300 pixels at 640x480 / 32 = 20x15 per cell, this is 50%

        private readonly int CellCountThreshold = 20;

        private Action<byte[]> _writeCallback;
        private byte[] _analysisBuffer;

        public AnalyseNormalizedRGBCells(Action<byte[]> writeProcessedFrameCallback)
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
            for (int i = 0; i < buffer.CellDiff.Length; i++)
            {
                diff += buffer.CellDiff[i];
                if (buffer.CellDiff[i] == 1) HighlightCell(255, 0, 255, buffer, metrics, i, _analysisBuffer);
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

                    float r1 = buffer.TestFrame[index];
                    float g1 = buffer.TestFrame[index + 1];
                    float b1 = buffer.TestFrame[index + 2];
                    float rgb1 = r1 + g1 + b1;
                    r1 = (r1 / rgb1) * 255.999f;
                    g1 = (g1 / rgb1) * 255.999f;
                    b1 = (b1 / rgb1) * 255.999f;
                    rgb1 = r1 + g1 + b1;

                    float r2 = buffer.CurrentFrame[index];
                    float g2 = buffer.CurrentFrame[index + 1];
                    float b2 = buffer.CurrentFrame[index + 2];
                    float rgb2 = r2 + g2 + b2;
                    r2 = (r2 / rgb2) * 255.999f;
                    g2 = (g2 / rgb2) * 255.999f;
                    b2 = (b2 / rgb2) * 255.999f;
                    rgb2 = r2 + g2 + b2;

                    float rgbDiff = Math.Abs(rgb2 - rgb1);

                    if (rgbDiff > metrics.Threshold)
                    {
                        diff++;
                    }

                    // highlight cell corners
                    if ((col == rect.X || col == x2 - 1) && (row == rect.Y || row == y2 - 1))
                    {
                        r2 = 128;
                        g2 = 0;
                        b2 = 128;
                    }

                    _analysisBuffer[index] = (byte)r2;
                    _analysisBuffer[index + 1] = (byte)g2;
                    _analysisBuffer[index + 2] = (byte)b2;

                    // No early exit for analysis purposes
                }
            }

            buffer.CellDiff[cellIndex] = (diff >= CellPixelThreshold) ? 1 : 0;
        }
    }
}
