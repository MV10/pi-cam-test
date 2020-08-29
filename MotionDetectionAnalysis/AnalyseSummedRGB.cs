using System;
using System.Threading.Tasks;

namespace MMALSharp.Processors.Motion
{
    public class AnalyseSummedRGB : IFrameDiffAlgorithm
    {
        private Action<byte[]> _writeFrame;
        private byte[] _analysisBuffer;

        public AnalyseSummedRGB(Action<byte[]> writeProcessedFrameCallback) 
        {
            _writeFrame = writeProcessedFrameCallback;
        }

        public void FirstFrameCompleted(FrameDiffBuffer buffer, FrameDiffMetrics metrics)
        {
            _analysisBuffer = new byte[buffer.TestFrame.Length];
            // not necessary for this analysis, CheckDiff overwrites the buffer completely
            // Array.Copy(buffer.TestFrame, _analysisBuffer, _analysisBuffer.Length);
            _writeFrame(buffer.TestFrame);
        }

        public bool AnalyseFrames(FrameDiffBuffer buffer, FrameDiffMetrics metrics)
        {
            Parallel.ForEach(buffer.CellDiff, (cell, loopState, loopIndex)
                => CheckDiff(loopIndex, buffer, metrics, loopState));

            int diff = 0;
            foreach (var cellDiff in buffer.CellDiff)
            {
                diff += cellDiff;
            }

            var detected = (diff > metrics.Threshold);

            if(detected)
            {
                DrawIndicator(255, 0, 0, buffer, metrics); // red = motion
            }
            else
            {
                DrawIndicator(0, 255, 0, buffer, metrics); // green = no motion
            }

            _writeFrame(_analysisBuffer);

            return detected;
        }

        public void ResetAnalyser(FrameDiffBuffer buffer, FrameDiffMetrics metrics)
        { } // no need to reset the analysis buffer

        // FrameDiffMetrics is a structure; it is a by-value copy and all fields are value types which makes it thread safe
        protected virtual void CheckDiff(long cellIndex, FrameDiffBuffer buffer, FrameDiffMetrics metrics, ParallelLoopState loopState)
        {
            int diff = 0;
            var rect = buffer.CellRect[cellIndex];

            for (var col = rect.X; col < rect.X + rect.Width; col++)
            {
                for (var row = rect.Y; row < rect.Y + rect.Height; row++)
                {
                    var index = (col * metrics.FrameBpp) + (row * metrics.FrameStride);

                    // Ignore the mask for analysis purposes

                    var rgb1 = buffer.TestFrame[index] + buffer.TestFrame[index + 1] + buffer.TestFrame[index + 2];

                    byte r2 = buffer.CurrentFrame[index];
                    byte g2 = buffer.CurrentFrame[index + 1];
                    byte b2 = buffer.CurrentFrame[index + 2];
                    var rgb2 = r2 + g2 + b2;

                    if (rgb2 - rgb1 > metrics.Threshold)
                    {
                        diff++;
                    }
                    else
                    {
                        byte g = (byte)((Grayscale(r2, g2, b2)) / 4);
                        r2 = g;
                        g2 = g;
                        b2 = g;
                    }

                    _analysisBuffer[index] = r2;
                    _analysisBuffer[index + 1] = g2;
                    _analysisBuffer[index + 2] = b2;

                    // No early exit for analysis purposes
                }
            }

            buffer.CellDiff[cellIndex] = diff;
        }

        // Sets pixels in the top corner to a specified color
        private void DrawIndicator(byte r, byte g, byte b, FrameDiffBuffer buffer, FrameDiffMetrics metrics)
        {
            for(int x = 0; x < 20; x++)
            {
                for(int y = 0; y < 20; y++)
                {
                    var i = (x * metrics.FrameBpp) + (y * metrics.FrameStride);
                    _analysisBuffer[i] = r;
                    _analysisBuffer[i + 1] = g;
                    _analysisBuffer[i + 2] = b;
                }
            }
        }

        private byte Grayscale(byte r, byte g, byte b)
            => (byte)((int)(r * 0.2989f) + (int)(g * 0.587f) + (int)(b * 0.114f));
    }
}
