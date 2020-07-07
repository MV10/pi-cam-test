using Mono.Unix.Native;

namespace MMALSharp.Handlers
{
    public class ExternalProcessCaptureHandlerOptions
    {
        /// <summary>
        /// The name of the process to be launched (e.g. ffmpeg, cvlc, etc.)
        /// </summary>
        public string Filename = string.Empty;

        /// <summary>
        /// Command line arguments used to start the process.
        /// </summary>
        public string Arguments = string.Empty;

        /// <summary>
        /// When true, stdout and stderr data is asynchronously buffered and output. When false, output is
        /// completely suppressed, which may improve release-build performance. Must be true to capture
        /// output in the MMAL verbose debug log.
        /// </summary>
        public bool EchoOutput = true;

        /// <summary>
        /// When the <see cref= "ExternalProcessCaptureHandler.ManageProcessLifecycle" /> token is canceled,
        /// a short delay will ensure any final output from the process is echoed. Ignored if EchoOutput is
        /// false. This delay occurs after any TerminationSignals are issued.
        /// </summary>
        public int DrainOutputDelayMs = 500;

        /// <summary>
        /// If present, when the <see cref="ExternalProcessCaptureHandler.ManageProcessLifecycle"/> token is
        /// canceled, these signals will be sent to the process. Some processes expect a CTRL+C (SIGINT).
        /// </summary>
        public Signum[] TerminationSignals = new Signum[]{ };

        /// <summary>
        /// In theory, ffmpeg responds to a series of SIGINT signals with a clean shutdown, although in
        /// practice this doesn't appear to work when ffmpeg is running as a child process.
        /// </summary>
        public static Signum[] signalsFFmpeg = new[] { Signum.SIGINT, Signum.SIGINT };

        /// <summary>
        /// Clean termination signals for a VLC / cvlc process.
        /// </summary>
        public static Signum[] signalsVLC = new[] { Signum.SIGINT };

        // With ffmpeg, these combinations all result in a corrupt an MP4 file (missing end-header MOOV atom).
        // Testing also shows it doesn't help to delay between sending the signals.
        //
        // SIGINT followed by:
        //
        // SIGINT, SIGABRT, SIGALRM, SIGBUS, SIGTERM, SIGHUP
        // immediate stop, no output
        //
        // SIGQUIT
        // only one with output; stops, tries to write trailer (MOOV atom), aborts
        //
        // VLC signals documented here:
        // https://wiki.videolan.org/Hacker_Guide/Interfaces/#A_typical_VLC_run_course
    }
}
