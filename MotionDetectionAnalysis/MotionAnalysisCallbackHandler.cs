using MMALSharp.Handlers;
using MMALSharp.Native;
using MMALSharp.Ports.Outputs;
using MMALSharp.Processors.Motion;

namespace MMALSharp.Callbacks
{
    public class MotionAnalysisCallbackHandler : PortCallbackHandler<IVideoPort, IOutputCaptureHandler>
    {
        public FrameDiffBuffer _frameDiffBuffer;

        public MotionAnalysisCallbackHandler(IVideoPort videoPort, IOutputCaptureHandler output)
            : base(videoPort, output)
        { }

        /// <inheritdoc />
        public override void Callback(IBuffer buffer)
        {
            base.Callback(buffer);

            var eos = buffer.AssertProperty(MMALBufferProperties.MMAL_BUFFER_HEADER_FLAG_FRAME_END) ||
                      buffer.AssertProperty(MMALBufferProperties.MMAL_BUFFER_HEADER_FLAG_EOS);

            if (eos)
            {
                //(this.CaptureHandler as MotionAnalysisCaptureHandler)?. ...then do what?... ;
            }
        }
    }
}
