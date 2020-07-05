using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MMALSharp.Common;

namespace MMALSharp.Handlers
{
    /// <summary>
    /// This is a variation on the MMALSharp project's FFmpegCaptureHandler.
    ///
    /// It omits the stream and AVI helper methods, which my projects will never use,
    /// it adds channel buffering of stdout console output, and it accepts arbitrary
    /// process names.
    ///
    /// It also properly diposes the Process object but it appears Dispose isn't
    /// called somewhere further up the pipeline.
    ///
    /// See https://github.com/techyian/MMALSharp/issues/154
    /// </summary>
    public class ExternalProcessCaptureHandler : IVideoCaptureHandler
    {
        private Process _process;
        private Channel<string> _outputBuffer;

        /// <summary>
        /// The total size of data that has been processed by this capture handler.
        /// </summary>
        protected int Processed { get; set; }

        /// <summary>
        /// Convenience method for creating the Channel used to buffer console output. Pass
        /// this to the constructor, then await Task.WhenAll for both MMALCamera.ProcessAsync
        /// and EmitStdOutBuffer.
        /// </summary>
        /// <returns>A new unbounded Channel object.</returns>
        public static Channel<string> CreateStdOutBuffer()
            => Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });

        /// <summary>
        /// Creates a new instance of <see cref="ExternalProcessCaptureHandler"/> with the specified process arguments.
        /// </summary>
        /// <param name="processFilename">The process to receive the capture stream, such as "ffmpeg" or "cvlc"</param>
        /// <param name="argument">The <see cref="ProcessStartInfo"/> argument.</param>
        /// <param name="stdoutBuffer">Console output, <see cref="CreateStdOutBuffer"/>; if null, console output will be suppressed.</param>
        public ExternalProcessCaptureHandler(string processFilename, string argument, Channel<string> stdoutBuffer = null)
        {
            _outputBuffer = stdoutBuffer;

            var processStartInfo = new ProcessStartInfo
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                FileName = processFilename,
                Arguments = argument
            };

            _process = new Process();
            _process.StartInfo = processStartInfo;

            Console.InputEncoding = Encoding.ASCII;

            _process.EnableRaisingEvents = true;
            _process.OutputDataReceived += WriteToStdOut;
            _process.ErrorDataReceived += WriteToStdOut;

            _process.Start();

            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
        }

        // Using "async void" is ok for an event-handler. The purpose of a Task is to communicate the
        // result of an operation to some external "observer" -- but by definition the caller that fires
        // an event doesn't care about the result of the operation. The only caveat is that you get no
        // exception handling; an unhandled exception would terminate the process.
        private async void WriteToStdOut(object sendingProcess, DataReceivedEventArgs e)
        {
            try
            {
                if (_outputBuffer != null && e.Data != null)
                    await _outputBuffer.Writer.WriteAsync(e.Data).ConfigureAwait(false);
            }
            catch
            { }
        }

        /// <summary>
        /// When console output is buffered, this asynchronously read/writes the buffer contents
        /// without blocking ffmpeg, as a raw Console.WriteLine would in response to process output.
        /// </summary>
        public static async Task EmitStdOutBuffer(Channel<string> stdoutBuffer, CancellationToken cancellationToken)
        {
            try
            {
                while (await stdoutBuffer.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
                    Console.WriteLine(await stdoutBuffer.Reader.ReadAsync().ConfigureAwait(false));
            }
            catch(OperationCanceledException)
            {
                // token cancellation, disregard
            }
        }

        /// <summary>
        /// Returns whether this capture handler features the split file functionality.
        /// </summary>
        /// <returns>True if can split.</returns>
        public bool CanSplit() => false;

        /// <summary>
        /// Not used.
        /// </summary>
        public void PostProcess() 
        { }

        /// <summary>
        /// Not used.
        /// </summary>
        /// <returns>A NotImplementedException.</returns>
        /// <exception cref="NotImplementedException"></exception>
        public string GetDirectory()
            => throw new NotImplementedException();

        /// <summary>
        /// Not used.
        /// </summary>
        /// <param name="allocSize">N/A.</param>
        /// <returns>A NotImplementedException.</returns>
        /// <exception cref="NotImplementedException"></exception>
        public ProcessResult Process(uint allocSize)
            => throw new NotImplementedException();

        /// <summary>
        /// Writes frame data to the StandardInput stream to be processed by FFmpeg.
        /// </summary>
        /// <param name="context">Contains the data and metadata for an image frame.</param>
        public void Process(ImageContext context)
        {
            try
            {
                _process.StandardInput.BaseStream.Write(context.Data, 0, context.Data.Length);
                _process.StandardInput.BaseStream.Flush();
                this.Processed += context.Data.Length;
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        /// <summary>
        /// Not used.
        /// </summary>
        /// <exception cref="NotImplementedException"></exception>
        public void Split()
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Returns the total number of bytes processed by this capture handler.
        /// </summary>
        /// <returns>The total number of bytes processed by this capture handler.</returns>
        public string TotalProcessed()
        {
            return $"{this.Processed}";
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (Common.Utility.MMALLog.Logger != null) 
                Common.Utility.MMALLog.Logger.LogTrace("Disposing ExternalProcessCaptureHandler");

            if (!_process.HasExited)
            {
                _process.Kill();
            }

            _process.Close();
            _process.Dispose();
        }
    }
}
