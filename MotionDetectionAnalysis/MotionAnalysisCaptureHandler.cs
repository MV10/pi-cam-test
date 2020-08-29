﻿using System.IO;
using MMALSharp.Common;
using MMALSharp.Processors.Motion;

namespace MMALSharp.Handlers
{
    public class MotionAnalysisCaptureHandler : IOutputCaptureHandler, IVideoCaptureHandler
    {
        private FrameDiffBuffer _frameDiffBuffer;
        private FileStream _stream;

        public MotionAnalysisCaptureHandler(string pathname, MotionConfig config)
        {
            _stream = new FileStream(pathname, FileMode.Create, FileAccess.Write);
            var algorithm = new AnalyseSummedRGB(WriteProcessedFrame);
            _frameDiffBuffer = new FrameDiffBuffer(config, algorithm);
        }

        public void Process(ImageContext context) 
        {
            _frameDiffBuffer.Apply(context);
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