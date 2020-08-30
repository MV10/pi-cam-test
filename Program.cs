using Microsoft.Extensions.Logging;
using MMALSharp;
using MMALSharp.Callbacks;
using MMALSharp.Common;
using MMALSharp.Common.Utility;
using MMALSharp.Components;
using MMALSharp.Config;
using MMALSharp.Handlers;
using MMALSharp.Native;
using MMALSharp.Ports;
using MMALSharp.Ports.Outputs;
using MMALSharp.Processors.Effects;
using MMALSharp.Processors.Motion;
using Mono.Unix.Native;
using Serilog;
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace pi_cam_test
{
    class Program
    {
        private static readonly string ramdiskPath = "/media/ramdisk/";
        private static readonly string networkPath = "/media/nas_dvr/smartcam/";
        private static readonly string sdcardPath = "/home/pi/";
        private static readonly string motionMaskPath = "../motionmask.bmp";

        private static bool useDebug = false;

        private static Stopwatch stopwatch = new Stopwatch();

        static async Task Main(string[] args)
        {
            stopwatch.Start();
            try
            {
                Console.WriteLine("\npi-cam-test\n");

                bool showHelp = true;
                if (args.Length > 0)
                {
                    var cmd = args[0].ToLower();
                    Console.WriteLine(cmd);

                    useDebug = args[args.Length - 1].ToLower().Equals("-debug");

                    int seconds = 0;
                    bool hasSeconds = args.Length > 1 && int.TryParse(args[1], out seconds) && seconds > 0;

                    switch (args[0].ToLower())
                    {
                        case "-jpg":
                            showHelp = false;
                            await jpg();
                            break;

                        case "-bmp":
                            showHelp = false;
                            await bmp();
                            break;

                        case "-vis":
                            if (args.Length < 2) break;
                            showHelp = false;
                            await visualize(args[1]);
                            break;

                        case "-rawtomp4":
                            if (args.Length < 2) break;
                            showHelp = false;
                            await rawtomp4(args[1]);
                            break;

                        case "-rawrgb24":
                            if (hasSeconds)
                            {
                                showHelp = false;
                                await rawrgb24(seconds);
                            }
                            break;

                        case "-stream":
                            if (hasSeconds)
                            {
                                showHelp = false;
                                await stream(seconds);
                            }
                            break;

                        case "-h264":
                            if (hasSeconds)
                            {
                                showHelp = false;
                                await h264(seconds);
                            }
                            break;

                        case "-h264tomp4":
                            if (args.Length < 3) break;
                            showHelp = false;
                            h264tomp4(args[1], args[2]);
                            break;

                        case "-motion":
                            if (hasSeconds)
                            {
                                showHelp = false;

                                int recordSeconds = 0;
                                int sensitivity = 0;
                                if (args.Length > 2) int.TryParse(args[2], out recordSeconds);
                                if (args.Length > 3) int.TryParse(args[3], out sensitivity);
                                // either of those could have been the "-debug" flag
                                if (recordSeconds == 0) recordSeconds = 10;
                                if (sensitivity == 0) sensitivity = 130;

                                await motion(seconds, recordSeconds, sensitivity);
                            }
                            break;

                        case "-splitter":
                            if (hasSeconds)
                            {
                                showHelp = false;

                                int recordSeconds = 0;
                                int jpgInterval = 0;
                                int sensitivity = 0;
                                if (args.Length > 2) int.TryParse(args[2], out recordSeconds);
                                if (args.Length > 3) int.TryParse(args[3], out jpgInterval);
                                if (args.Length > 4) int.TryParse(args[4], out sensitivity);
                                // any of those could have been the "-debug" flag
                                if (recordSeconds == 0) recordSeconds = 10;
                                if (jpgInterval == 0) jpgInterval = 1;
                                if (sensitivity == 0) sensitivity = 130;

                                await splitter(seconds, recordSeconds, jpgInterval, sensitivity);
                            }
                            break;

                        case "-copyperf":
                            showHelp = false;
                            copyperf();
                            break;

                        case "-transcodeperf":
                            if (args.Length != 2) break;
                            if(args[1].ToLower() == "ram")
                            {
                                showHelp = false;
                                transcodeperf(ramdiskPath);
                            }
                            if (args[1].ToLower() == "sd")
                            {
                                showHelp = false;
                                transcodeperf(sdcardPath);
                            }
                            if (args[1].ToLower() == "lan")
                            {
                                showHelp = false;
                                transcodeperf(networkPath);
                            }
                            break;

                        case "-fragmp4":
                            if (hasSeconds)
                            {
                                showHelp = false;
                                await fragmp4(seconds);
                            }
                            break;

                        case "-badmp4":
                            if (hasSeconds)
                            {
                                showHelp = false;
                                await badmp4(seconds);
                            }
                            break;
                    }
                }

                if(showHelp)
                {
                    Console.WriteLine("Usage:\n");
                    Console.WriteLine("pi-cam-test -jpg");
                    Console.WriteLine("pi-cam-test -bmp");
                    Console.WriteLine("pi-cam-test -rawrgb24 [seconds]");
                    Console.WriteLine("pi-cam-test -vis [raw_filename]");
                    Console.WriteLine("pi-cam-test -rawtomp4 [raw_filename]");
                    Console.WriteLine("pi-cam-test -h264tomp4 [h264_pathname] [mp4_pathname]");
                    Console.WriteLine("pi-cam-test -h264 [seconds]");
                    Console.WriteLine("pi-cam-test -stream [seconds]");
                    Console.WriteLine("pi-cam-test -motion [total_seconds] [record_seconds=10] [sensitivity=130]");
                    Console.WriteLine("pi-cam-test -splitter [total_seconds] [record_seconds=10] [jpg-interval=1] [sensitivity=130]");
                    Console.WriteLine("pi-cam-test -copyperf");
                    Console.WriteLine("pi-cam-test -transcodeperf [ram|sd|lan]");
                    Console.WriteLine("pi-cam-test -fragmp4 [seconds]");
                    Console.WriteLine("pi-cam-test -badmp4 [seconds]");
                    Console.WriteLine($"\nAdd -debug to activate MMALSharp verbose debug logging.\n\nLocal output: {ramdiskPath}\nNetwork output: {networkPath}\n\nMotion detection deletes all .raw and .h264 files from the ramdisk.\n");
                }
            }
            catch(Exception ex)
            {
                var msg = $"Exception: {ex.Message}";
                Console.WriteLine($"\n\n{msg}");
                if (MMALLog.Logger != null) MMALLog.Logger.LogError(msg);
            }
            stopwatch.Stop();
            WriteElapsedTime();
            MMALLog.Logger.LogDebug("Application exit");
        }

        static async Task jpg()
        {
            var cam = GetConfiguredCamera();
            var pathname = ramdiskPath + "snapshot.jpg";
            using var handler = new ImageStreamCaptureHandler(pathname);
            Console.WriteLine($"Capturing JPG: {pathname}");
            await cam.TakePicture(handler, MMALEncoding.JPEG, MMALEncoding.I420).ConfigureAwait(false);
            cam.Cleanup();
            Console.WriteLine("Exiting.");
        }

        static async Task bmp()
        {
            var cam = GetConfiguredCamera();
            var pathname = ramdiskPath + "snapshot.bmp";
            using var handler = new ImageStreamCaptureHandler(pathname);
            Console.WriteLine($"Capturing BMP: {pathname}");
            await cam.TakePicture(handler, MMALEncoding.BMP, MMALEncoding.I420).ConfigureAwait(false);
            cam.Cleanup();
            Console.WriteLine("Exiting.");
        }

        static async Task fragmp4(int seconds)
        {
            // This generates a "fragmented" MP4 which should be larger than an MP4
            // with a normal single MOOV atom trailer at the end of the file. See:
            // https://superuser.com/a/1530949/143047
            // 10 seconds of "-badmp4" is 34MB
            // 10 seconds of "-fragmp4" is 26MB regardless of the other options described in the link
            // 60 seconds (bad) is 219MB versus 208MB (frag) and again we lose 2 seconds
            // -g is keyframe rate, default is 250
            // -flush_packets 1 flushes the I/O stream after each packet, decreasing latency (apparently)
            // adding two seconds to the requested duration approximates the regular file size (10s = 33MB, 60s = 218MB)

            seconds += 2; // HACK! see above

            var cam = GetConfiguredCamera();
            var pathname = ramdiskPath + "video.mp4";
            Directory.CreateDirectory(ramdiskPath);
            File.Delete(pathname);

            Console.WriteLine("Preparing pipeline...");
            cam.ConfigureCameraSettings();

            using (var ffmpeg = new ExternalProcessCaptureHandler(
                new ExternalProcessCaptureHandlerOptions
                {
                    Filename = "ffmpeg",
                    Arguments = $"-framerate 24 -i - -b:v 2500k -c copy -movflags +frag_keyframe+separate_moof+omit_tfhd_offset+empty_moov {pathname}",
                    EchoOutput = true,
                    DrainOutputDelayMs = 500, // default
                    TerminationSignals = new[] { Signum.SIGINT, Signum.SIGQUIT }, // not the supposedly-correct SIGINT+SIGINT but this produces some exit output
                }))
            {
                // quality arg-help says set bitrate zero to use quality for VBR
                var portCfg = new MMALPortConfig(MMALEncoding.H264, MMALEncoding.I420, quality: 10, bitrate: 0, timeout: null);
                using var encoder = new MMALVideoEncoder();
                using var renderer = new MMALVideoRenderer();
                encoder.ConfigureOutputPort(portCfg, ffmpeg);
                cam.Camera.VideoPort.ConnectTo(encoder);
                cam.Camera.PreviewPort.ConnectTo(renderer);

                Console.WriteLine("Camera warmup...");
                await Task.Delay(2000);

                Console.WriteLine($"Capturing MP4: {pathname}");
                var timerToken = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
                await Task.WhenAll(new Task[]{
                    ffmpeg.ProcessExternalAsync(timerToken.Token),
                    cam.ProcessAsync(cam.Camera.VideoPort, timerToken.Token)
                }).ConfigureAwait(false);
            }

            // can't use the convenient fall-through using or MMALCamera.Cleanup
            // throws: Argument is invalid. Unable to destroy component
            cam.Cleanup();

            Console.WriteLine("Exiting.");
        }

        static async Task h264(int seconds)
        {
            var cam = GetConfiguredCamera();
            var pathname = ramdiskPath + "capture.h264";
            using var handler = new VideoStreamCaptureHandler(pathname);
            var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
            Console.WriteLine($"Capturing h.264: {pathname}");
            await cam.TakeVideo(handler, timeout.Token);
            cam.Cleanup();
            Console.WriteLine("Exiting.");
        }

        static async Task badmp4(int seconds)
        {
            Console.WriteLine("\n\nWARNING:\nffmpeg can't create a valid MP4 when running as a child process.\nSee repository README. This code is here for reference only.\n\nPress any key...");
            Console.ReadKey(true);

            var cam = GetConfiguredCamera();
            var pathname = ramdiskPath + "video.mp4";
            Directory.CreateDirectory(ramdiskPath);
            File.Delete(pathname);

            Console.WriteLine("Preparing pipeline...");
            cam.ConfigureCameraSettings();

            using (var ffmpeg = new ExternalProcessCaptureHandler(
                new ExternalProcessCaptureHandlerOptions
                {
                    Filename = "ffmpeg",
                    Arguments = $"-framerate 24 -i - -b:v 2500k -c copy {pathname}",
                    EchoOutput = true,
                    DrainOutputDelayMs = 500, // default
                    TerminationSignals = new[] { Signum.SIGINT, Signum.SIGQUIT }, // not the supposedly-correct SIGINT+SIGINT but this produces some exit output
                }))
            {
                // quality arg-help says set bitrate zero to use quality for VBR
                var portCfg = new MMALPortConfig(MMALEncoding.H264, MMALEncoding.I420, quality: 10, bitrate: 0, timeout: null);
                using var encoder = new MMALVideoEncoder();
                using var renderer = new MMALVideoRenderer();
                encoder.ConfigureOutputPort(portCfg, ffmpeg);
                cam.Camera.VideoPort.ConnectTo(encoder);
                cam.Camera.PreviewPort.ConnectTo(renderer);

                Console.WriteLine("Camera warmup...");
                await Task.Delay(2000);

                Console.WriteLine($"Capturing MP4: {pathname}");
                var timerToken = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
                await Task.WhenAll(new Task[]{
                    ffmpeg.ProcessExternalAsync(timerToken.Token),
                    cam.ProcessAsync(cam.Camera.VideoPort, timerToken.Token),
                }).ConfigureAwait(false);
            }

            // can't use the convenient fall-through using or MMALCamera.Cleanup
            // throws: Argument is invalid. Unable to destroy component
            cam.Cleanup();

            Console.WriteLine("Exiting. Remember, video.mp4 is not valid.");
        }

        static async Task stream(int seconds)
        {
            var cam = GetConfiguredCamera();

            MMALCameraConfig.Resolution = new Resolution(640, 480);
            MMALCameraConfig.SensorMode = MMALSensorMode.Mode7; // for some reason mode 6 has a pinkish tinge
            MMALCameraConfig.Framerate = new MMAL_RATIONAL_T(20, 1);

            Console.WriteLine("Preparing pipeline...");
            cam.ConfigureCameraSettings();
            // note cvlc requires real quotes even though we used apostrophes for the command line equivalent
            using (var vlc = new ExternalProcessCaptureHandler(
                new ExternalProcessCaptureHandlerOptions
                {
                    Filename = "cvlc",
                    Arguments = @"stream:///dev/stdin --sout ""#transcode{vcodec=mjpg,vb=2500,fps=20,acodec=none}:standard{access=http{mime=multipart/x-mixed-replace;boundary=7b3cc56e5f51db803f790dad720ed50a},mux=mpjpeg,dst=:8554/}"" :demux=h264",
                    EchoOutput = true,
                    DrainOutputDelayMs = 500, // default
                    TerminationSignals = ExternalProcessCaptureHandlerOptions.SignalsVLC
                }))
            {
                var portCfg = new MMALPortConfig(MMALEncoding.H264, MMALEncoding.I420, quality: 0, bitrate: MMALVideoEncoder.MaxBitrateMJPEG, timeout: null);

                using var encoder = new MMALVideoEncoder();
                using var renderer = new MMALVideoRenderer();
                encoder.ConfigureOutputPort(portCfg, vlc);
                cam.Camera.VideoPort.ConnectTo(encoder);
                cam.Camera.PreviewPort.ConnectTo(renderer);

                Console.WriteLine("Camera warmup...");
                await Task.Delay(2000);

                Console.WriteLine($"Streaming MJPEG for {seconds} sec to:");
                Console.WriteLine($"http://{Environment.MachineName}.local:8554/");
                var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
                await Task.WhenAll(new Task[]{
                    vlc.ProcessExternalAsync(timeout.Token),
                    cam.ProcessAsync(cam.Camera.VideoPort, timeout.Token),
                }).ConfigureAwait(false);
            }

            // can't use the convenient fall-through using or MMALCamera.Cleanup
            // throws: Argument is invalid. Unable to destroy component
            cam.Cleanup();

            Console.WriteLine("Exiting.");
        }

        static async Task motion(int totalSeconds, int recordSeconds, int sensitivity)
        {
            DeleteFiles(ramdiskPath, "*.h264");

            var cam = GetConfiguredCamera();
            MMALCameraConfig.InlineHeaders = true; // h.264 requires key frames for the circular buffer capture handler.
            cam.ConfigureCameraSettings();

            Console.WriteLine("Preparing pipeline...");
            using (var splitter = new MMALSplitterComponent())
            {
                // Two capture handlers are being used here, one for motion detection and the other to record a H.264 stream.
                using var vidCaptureHandler = new CircularBufferCaptureHandler(4000000, ramdiskPath, "h264");
                //using var motionCaptureHandler = new FrameBufferCaptureHandler();
                using var motionCaptureHandler = new TempFrameBufferCaptureHandler();
                using var resizer = new MMALIspComponent();
                using var vidEncoder = new MMALVideoEncoder();
                using var renderer = new MMALVideoRenderer();

                splitter.ConfigureInputPort(new MMALPortConfig(MMALEncoding.OPAQUE, MMALEncoding.I420), cam.Camera.VideoPort, null);
                vidEncoder.ConfigureInputPort(new MMALPortConfig(MMALEncoding.OPAQUE, MMALEncoding.I420), splitter.Outputs[1], null);

                // The ISP resizer is being used for better performance. Frame difference motion detection will only work if using raw video data. Do not encode to H.264/MJPEG.
                // Resizing to a smaller image may improve performance, but ensure that the width/height are multiples of 32 and 16 respectively to avoid cropping.
                resizer.ConfigureOutputPort<VideoPort>(0, new MMALPortConfig(MMALEncoding.RGB24, MMALEncoding.RGB24, width: 640, height: 480), motionCaptureHandler);
                vidEncoder.ConfigureOutputPort(new MMALPortConfig(MMALEncoding.H264, MMALEncoding.I420, 0, MMALVideoEncoder.MaxBitrateLevel4, null), vidCaptureHandler);

                cam.Camera.VideoPort.ConnectTo(splitter);
                cam.Camera.PreviewPort.ConnectTo(renderer);

                splitter.Outputs[0].ConnectTo(resizer);
                splitter.Outputs[1].ConnectTo(vidEncoder);

                Console.WriteLine("Camera warmup...");
                await Task.Delay(2000);

                var cts = new CancellationTokenSource(TimeSpan.FromSeconds(totalSeconds));

                //var motionConfig = new MotionConfig(threshold: sensitivity, testFrameInterval: TimeSpan.FromSeconds(3), motionMaskPathname: motionMaskPath);
                var motionConfig = new MotionConfig(threshold: sensitivity, testFrameInterval: TimeSpan.FromSeconds(3));

                //var motionConfig = new MotionConfigHSV(testFrameInterval: TimeSpan.FromSeconds(3))
                //{
                //    PerCellThreshold = true,    // frame-level diff is cell count, not pixels
                //    Threshold = 5,              // minimum number of cells that must have changes
                //    CellMinimumDiff = 200,      // minimum pixel diff per cell to qualify as changed

                //    // 640 x 480 with 32 divisions = 1024 cells
                //    // each cell is 20 x 15 = 300 pixels
                //};

                Console.WriteLine($"Detecting motion for {totalSeconds} seconds with sensitivity threshold {sensitivity}...");

                await cam.WithMotionDetection(
                    motionCaptureHandler,
                    motionConfig,
                    // This callback will be invoked when motion has been detected.
                    async () =>
                    {
                        Console.WriteLine($"\n     {DateTime.Now:hh\\:mm\\:ss} Motion detected, recording {recordSeconds} seconds...");

                        motionCaptureHandler.DisableMotionDetection();

                        // Prepare to record
                        // Stephen Cleary says CTS disposal is unnecessary as long as you cancel! https://stackoverflow.com/a/19005066/152997
                        var stopRecordingCts = new CancellationTokenSource();

                        // When the token expires, stop recording and re-enable capture
                        stopRecordingCts.Token.Register(() =>
                        {
                            Console.WriteLine($"     {DateTime.Now:hh\\:mm\\:ss} ...recording stopped.");

                            motionCaptureHandler.EnableMotionDetection();
                            vidCaptureHandler.StopRecording();
                            vidCaptureHandler.Split();
                        });

                        // Start the clock
                        stopRecordingCts.CancelAfter(recordSeconds * 1000);

                        // Record until the duration passes or the overall motion detection token expires
                        await Task.WhenAny(
                            vidCaptureHandler.StartRecording(vidEncoder.RequestIFrame, stopRecordingCts.Token),
                            cts.Token.AsTask()
                        );
                        if (!stopRecordingCts.IsCancellationRequested) stopRecordingCts.Cancel();
                    })
                    .ProcessAsync(cam.Camera.VideoPort, cts.Token);
            }

            // can't use the convenient fall-through using or MMALCamera.Cleanup
            // throws: Argument is invalid. Unable to destroy component
            cam.Cleanup();

            Console.WriteLine("Exiting.");
        }

        static async Task splitter(int totalSeconds, int recordSeconds, int jpgInterval, int sensitivity)
        {
            DeleteFiles(ramdiskPath, "*.h264");
            DeleteFiles(ramdiskPath, "*.jpg");

            var cam = GetConfiguredCamera();
            MMALCameraConfig.InlineHeaders = true;
            cam.ConfigureCameraSettings();

            Console.WriteLine("Preparing pipeline...");
            using (var vidCaptureHandler = new CircularBufferCaptureHandler(4000000, ramdiskPath, "h264"))
            using (var motionCaptureHandler = new FrameBufferCaptureHandler()) // new vs motion example
            using (var imgCaptureHandler = new FrameBufferCaptureHandler(ramdiskPath, "jpg")) // new vs motion example
            using (var splitter = new MMALSplitterComponent())
            using (var resizer = new MMALIspComponent())
            using (var vidEncoder = new MMALVideoEncoder())
            using (var imgEncoder = new MMALImageEncoder(continuousCapture: true)) // new vs motion example
            using (var renderer = new MMALVideoRenderer())
            {
                splitter.ConfigureInputPort(new MMALPortConfig(MMALEncoding.OPAQUE, MMALEncoding.I420), cam.Camera.VideoPort, null);

                resizer.ConfigureOutputPort<VideoPort>(0, new MMALPortConfig(MMALEncoding.RGB24, MMALEncoding.RGB24, width: 640, height: 480), motionCaptureHandler);
                vidEncoder.ConfigureOutputPort(new MMALPortConfig(MMALEncoding.H264, MMALEncoding.I420, quality: 10, bitrate: MMALVideoEncoder.MaxBitrateLevel4, null), vidCaptureHandler);
                imgEncoder.ConfigureOutputPort(new MMALPortConfig(MMALEncoding.JPEG, MMALEncoding.I420, quality: 90), imgCaptureHandler); // new vs motion example

                cam.Camera.VideoPort.ConnectTo(splitter);
                cam.Camera.PreviewPort.ConnectTo(renderer);

                splitter.Outputs[0].ConnectTo(resizer);
                splitter.Outputs[1].ConnectTo(vidEncoder);
                splitter.Outputs[2].ConnectTo(imgEncoder); // new vs motion example

                Console.WriteLine("Camera warmup...");
                await Task.Delay(2000);

                var cts = new CancellationTokenSource(TimeSpan.FromSeconds(totalSeconds));

                Console.WriteLine($"Detecting motion for {totalSeconds} seconds with sensitivity threshold {sensitivity}...");

                var motionConfig = new MotionConfig(threshold: sensitivity, testFrameInterval: TimeSpan.FromSeconds(3), motionMaskPathname: motionMaskPath);

                await cam.WithMotionDetection(
                    motionCaptureHandler,
                    motionConfig,
                    async () =>
                    {
                        Console.WriteLine($"\n     {DateTime.Now:hh\\:mm\\:ss} Motion detected, recording {recordSeconds} seconds...");
                        motionCaptureHandler.DisableMotionDetection();

                        // Save a snapshot as soon as motion is detected
                        imgCaptureHandler.WriteFrame();

                        var stopRecordingCts = new CancellationTokenSource();
                        stopRecordingCts.Token.Register(() =>
                        {
                            Console.WriteLine($"     {DateTime.Now:hh\\:mm\\:ss} ...recording stopped.");
                            motionCaptureHandler.EnableMotionDetection();
                            vidCaptureHandler.StopRecording();
                            vidCaptureHandler.Split();
                        });

                        // Save additional snapshots 1- and 2-seconds after motion is detected
                        var stillFrameOneSecondCts = new CancellationTokenSource();
                        var stillFrameTwoSecondsCts = new CancellationTokenSource();
                        stillFrameOneSecondCts.Token.Register(imgCaptureHandler.WriteFrame);
                        stillFrameTwoSecondsCts.Token.Register(imgCaptureHandler.WriteFrame);

                        // Set token cancellation timeouts
                        stopRecordingCts.CancelAfter(recordSeconds * 1000);
                        stillFrameTwoSecondsCts.CancelAfter(2000);
                        stillFrameOneSecondCts.CancelAfter(1000);

                        await Task.WhenAny(
                                vidCaptureHandler.StartRecording(vidEncoder.RequestIFrame, stopRecordingCts.Token),
                                cts.Token.AsTask()
                        );

                        // Ensure all tokens are cancelled if the overall cts.Token timed out
                        if (!stopRecordingCts.IsCancellationRequested)
                        {
                            stillFrameOneSecondCts.Cancel();
                            stillFrameTwoSecondsCts.Cancel();
                            stopRecordingCts.Cancel();
                        }
                    })
                    .ProcessAsync(cam.Camera.VideoPort, cts.Token);
            }

            cam.Cleanup();
            Console.WriteLine("Exiting.");
        }

        static async Task rawrgb24(int totalSeconds)
        {
            var cam = GetConfiguredCamera(withOverlay: false);

            Console.WriteLine("Preparing pipeline...");
            cam.ConfigureCameraSettings();

            var rawPathname = ramdiskPath + "output.raw";
            File.Delete(rawPathname);

            using (var capture = new VideoStreamCaptureHandler(rawPathname))
            using (var splitter = new MMALSplitterComponent())
            using (var resizer = new MMALIspComponent())
            {
                splitter.ConfigureInputPort(new MMALPortConfig(MMALEncoding.OPAQUE, MMALEncoding.I420), cam.Camera.VideoPort, null);
                resizer.ConfigureOutputPort<VideoPort>(0, new MMALPortConfig(MMALEncoding.RGB24, MMALEncoding.RGB24, width: 640, height: 480), capture);
                cam.Camera.VideoPort.ConnectTo(splitter);
                splitter.Outputs[0].ConnectTo(resizer);

                Console.WriteLine("Camera warmup...");
                await Task.Delay(2000);

                Console.WriteLine($"Capturing {totalSeconds} seconds of raw RGB24 video...");
                var cts = new CancellationTokenSource(TimeSpan.FromSeconds(totalSeconds));
                await cam.ProcessAsync(cam.Camera.VideoPort, cts.Token);
            }
            cam.Cleanup();
            Console.WriteLine("Exiting.");
        }

        static async Task visualize(string rawFilename)
        {
            Console.WriteLine("Preparing pipeline...");
            MMALStandalone standalone = MMALStandalone.Instance;

            var rawPathname = ramdiskPath + rawFilename;
            var analysisPathname = ramdiskPath + "analysis.raw";
            File.Delete(analysisPathname);

            // SummedRGB problem: Threshold is both the summed-RGB difference and the total count of changed pixels
            var motionConfig = new MotionConfig()
            {
                Threshold = 130,                              // 130 is default, grid cells are 300px (20x15x1024)
                TestFrameInterval = TimeSpan.FromSeconds(999) // 3 sec is default test frame interval (we now have motion cooldown, enable when detection improves)
            }; 

            using (var stream = File.OpenRead(rawPathname))
            using (var input = new InputCaptureHandler(stream))
            using (var analysis = new MotionAnalysisCaptureHandler(analysisPathname, motionConfig))
            using (var resizer = new MMALIspComponent())
            {
                var cfg = new MMALPortConfig(MMALEncoding.RGB24, MMALEncoding.RGB24, width: 640, height: 480, framerate: 24, zeroCopy: true);
                resizer.ConfigureInputPort(cfg, null, input);
                resizer.ConfigureOutputPort<FileEncodeOutputPort>(0, cfg, analysis);

                Console.WriteLine("Processing raw RGB24 file through motion analysis filter...");
                await standalone.ProcessAsync(resizer);
            }
            standalone.Cleanup();

            Console.WriteLine("Analysis complete.");
            WriteElapsedTime();

            await rawtomp4("analysis.raw");
        }

        static async Task rawtomp4(string rawFilename)
        {
            Console.WriteLine("Preparing pipeline...");
            MMALStandalone standalone = MMALStandalone.Instance;

            var rawPathname = ramdiskPath + rawFilename;
            var h264Pathname = ramdiskPath + "output.h264";
            var mp4Pathname = ramdiskPath + "output.mp4";
            File.Delete(h264Pathname);
            File.Delete(mp4Pathname);

            using (var stream = File.OpenRead(rawPathname))
            using (var input = new InputCaptureHandler(stream))
            using (var output = new VideoStreamCaptureHandler(h264Pathname))
            using (var encoder = new MMALVideoEncoder())
            {
                var rgb24config = new MMALPortConfig(MMALEncoding.RGB24, MMALEncoding.RGB24, width: 640, height: 480, framerate: 24, zeroCopy: true);
                var h264config = new MMALPortConfig(MMALEncoding.H264, MMALEncoding.I420, width: 640, height: 480, framerate: 24, quality: 10, bitrate: MMALVideoEncoder.MaxBitrateLevel4);
                encoder.ConfigureInputPort(rgb24config, null, input);
                encoder.ConfigureOutputPort<FileEncodeOutputPort>(0, h264config, output);

                Console.WriteLine("Processing raw RGB24 file to h.264...");
                await standalone.ProcessAsync(encoder);
            }
            standalone.Cleanup();

            h264tomp4(h264Pathname, mp4Pathname);

            Console.WriteLine("Exiting.");
        }

        static void h264tomp4(string h264Pathname, string mp4Pathname)
        {
            Console.WriteLine("\nTranscoding h.264 to mp4...");
            using (var proc = new Process())
            {
                proc.StartInfo = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = $"-hide_banner -loglevel error -stats -framerate 24 -i \"{h264Pathname}\" -c copy \"{mp4Pathname}\""
                };
                proc.Start();
                proc.WaitForExit();
                proc.Dispose();
            }
        }

        static void copyperf()
        {
            var source = ramdiskPath + "video.mp4";
            if(!File.Exists(source))
            {
                Console.WriteLine("Create a video.mp4 recording on the ramdisk to check file-copy performance to the NAS.");
                return;
            }

            var dest = networkPath + "video.mp4";
            File.Delete(dest);

            var fi = new FileInfo(source);
            long bytes = fi.Length;

            Console.WriteLine($"Copying {bytes:#,#} bytes:\n  src : {source}\n  dest: {dest}");

            var timer = new Stopwatch();
            timer.Start();
            File.Copy(source, dest);
            timer.Stop();
            Console.WriteLine($"Time to copy: {timer.Elapsed:hh\\:mm\\:ss}\n\n");

            Console.WriteLine("Verifying destination size...");
            fi = new FileInfo(dest);
            if(bytes != fi.Length)
            {
                Console.WriteLine($"DESTINATION FILE-SIZE MISMATCH: {fi.Length:#,#} bytes");
            }
            else
            {
                Console.WriteLine("File-sizes match.");
            }

            Console.WriteLine("Exiting.");
        }

        static void transcodeperf(string targetPath)
        {
            var source = ramdiskPath + "capture.h264";
            if (!File.Exists(source))
            {
                Console.WriteLine("Create a capture.h264 recording on the ramdisk to check transcoding performance.");
                return;
            }

            var dest = targetPath + "video.mp4";
            File.Delete(dest);

            // We don't need the complexity of output buffering (as seen in ExternalProcessCaptureHandler)
            // because the input is a file, there is no chance of dropped frames. The async buffering would
            // improve performance mildly but in my real app this will be a background process, mostly I want
            // to compare local to network performance.

            Console.WriteLine($"Transcoding:\n  src : {source}\n  dest: {dest}");

            var processStartInfo = new ProcessStartInfo
            {
                UseShellExecute = false,
                RedirectStandardInput = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                FileName = "ffmpeg",
                
                // see README about the faststart option
                Arguments = $"-framerate 24 -i {source} -b:v 2500k -c copy -movflags -faststart {dest}", 
                //Arguments = $"-framerate 24 -i {source} -b:v 2500k -c copy -movflags +faststart {dest}",
            };

            var process = new Process();
            process.StartInfo = processStartInfo;

            process.EnableRaisingEvents = true;
            process.OutputDataReceived += 
                (object sendingProcess, DataReceivedEventArgs e) =>
                {
                    if (e.Data != null) Console.WriteLine(e.Data);
                };
            process.ErrorDataReceived +=
                (object sendingProcess, DataReceivedEventArgs e) =>
                {
                    if (e.Data != null) Console.WriteLine(e.Data);
                };

            var timer = new Stopwatch();
            timer.Start();
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();
            timer.Stop();
            process.Dispose();

            if(File.Exists(dest))
            {
                var fi = new FileInfo(dest);
                Console.WriteLine($"{fi.Length:#,#} bytes written to {dest}");
                Console.WriteLine($"Time to transcode: {timer.Elapsed:hh\\:mm\\:ss}\n\n");
            }
            else
            {
                Console.WriteLine($"Failed to create {dest}");
            }

            Console.WriteLine("Exiting.");
        }

        static MMALCamera GetConfiguredCamera(bool withOverlay = true)
        {
            if (useDebug)
            {
                Console.WriteLine("\nVerbose debug logging enabled to debug.log in current directory.");

                File.Delete("debug.log");

                MMALCameraConfig.Debug = true;
                MMALLog.LoggerFactory = new LoggerFactory()
                    .AddSerilog(new LoggerConfiguration()
                        .MinimumLevel.Verbose()
                        .WriteTo.File("debug.log")
                        .CreateLogger());
            }

            Console.WriteLine("Initializing...");
            var cam = MMALCamera.Instance;

            // 1296 x 972 with 2x2 binning (full-frame 4:3 capture)
            MMALCameraConfig.Resolution = new Resolution(1296, 972);
            MMALCameraConfig.SensorMode = MMALSensorMode.Mode4;
            MMALCameraConfig.Framerate = new MMAL_RATIONAL_T(24, 1); // numerator & denominator

            if(withOverlay)
            {
                // overlay text
                var overlay = new AnnotateImage(Environment.MachineName, 30, Color.White)
                {
                    ShowDateText = true,
                    ShowTimeText = true,
                };

                // new v0.7 properties
                overlay.DateFormat = "yyyy-MM-dd";
                overlay.TimeFormat = "HH:mm:ss";
                overlay.RefreshRate = DateTimeTextRefreshRate.Seconds;

                MMALCameraConfig.Annotate = overlay;
                cam.EnableAnnotation();
            }

            // image quality tweaks to play with later
            MMALCameraConfig.Sharpness = 0;             // 0 = auto, default; -100 to 100
            MMALCameraConfig.Contrast = 0;              // 0 = auto, default; -100 to 100
            MMALCameraConfig.Brightness = 50;           // 50 = default; 0 = black, 100 = white
            MMALCameraConfig.Saturation = 0;            // 0 = default; -100 to 100
            MMALCameraConfig.ExposureCompensation = 0;  // 0 = none, default; -10 to 10, lightens/darkens the image

            // low-light tweaks which don't seem to degrade full-light recording
            MMALCameraConfig.ExposureMode = MMAL_PARAM_EXPOSUREMODE_T.MMAL_PARAM_EXPOSUREMODE_NIGHT;
            MMALCameraConfig.ExposureMeterMode = MMAL_PARAM_EXPOSUREMETERINGMODE_T.MMAL_PARAM_EXPOSUREMETERINGMODE_MATRIX;
            MMALCameraConfig.DrcLevel = MMAL_PARAMETER_DRC_STRENGTH_T.MMAL_PARAMETER_DRC_STRENGTH_HIGH;

            // produces a grayscale image
            //MMALCameraConfig.ColourFx = new ColourEffects(true, Color.FromArgb(128, 128, 128));

            return cam;
        }

        static void DeleteFiles(string path, string filespec)
        {
            foreach (string fp in Directory.EnumerateFiles(path, filespec))
            {
                File.Delete(fp);
            }
        }

        static void WriteElapsedTime() => Console.WriteLine($"\nElapsed: {stopwatch.Elapsed:hh\\:mm\\:ss}\n\n");
    }
}
