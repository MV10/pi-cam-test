using System;
using System.IO;
using MMALSharp.Common;
using MMALSharp.Processors.Motion;

namespace MMALSharp.Handlers
{
    public class MotionAnalysisCaptureHandler : IOutputCaptureHandler, IVideoCaptureHandler
    {
        private FrameDiffDriver _driver;
        private FileStream _stream;

        public MotionAnalysisCaptureHandler(string pathname, MotionConfig config, Action onDetect = null)
        {
            _stream = new FileStream(pathname, FileMode.Create, FileAccess.Write);
            //var algorithm = new AnalyseSummedRGB(WriteProcessedFrame);
            //var algorithm = new AnalyseSummedRGBPixels(WriteProcessedFrame);
            //var algorithm = new AnalyseSummedRGBCells(WriteProcessedFrame);
            var algorithm = new AnalyseHSV(WriteProcessedFrame);
            _driver = new FrameDiffDriver(config, algorithm, onDetect);
        }

        public void Process(ImageContext context) 
        {
            _driver.Apply(context);
        }

        public void WriteProcessedFrame(byte[] fullFrame)
        {
            if (!_stream.CanWrite)
                throw new IOException("Stream not writeable.");

            _stream.Write(fullFrame, 0, fullFrame.Length);
        }

        public void Dispose()
        {
            _stream?.Flush();
            _stream?.Close();
            _stream?.Dispose();
        }

        // unused, required by IOutputCaptureHandler
        public void PostProcess() { }
        public string TotalProcessed() => string.Empty;

        // unused, required by IVideoCaptureHandler
        public void Split() { }
    }
}
