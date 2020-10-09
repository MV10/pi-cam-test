﻿using Microsoft.Extensions.Logging;
using MMALSharp;
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
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
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

        // TODO motion and splitter sensitivity args not applicable / need updating

        static async Task Main(string[] args)
        {
            if(Environment.OSVersion.Platform != PlatformID.Unix)
            {
                Console.WriteLine("\n\n\nHey dumbass, run this on yer Pi!\n\n\n");
                return;
            }

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
                        case "-snapshotsharpen":
                            showHelp = false;
                            await snapshotSharpen();
                            break;

                        case "-snapshotblur3":
                            showHelp = false;
                            await snapshotBlur3();
                            break;

                        case "-snapshotblur5":
                            showHelp = false;
                            await snapshotBlur5();
                            break;

                        case "-snapshotedge":
                            showHelp = false;
                            await snapshotEdge();
                            break;

                        case "-jpg":
                            showHelp = false;
                            await jpg();
                            break;

                        case "-bmp":
                            showHelp = false;
                            await bmp();
                            break;

                        case "-visfile":
                            if (args.Length < 2) break;
                            showHelp = false;
                            await visualizeFile(args[1]);
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

                        case "-visstream":
                            if (hasSeconds)
                            {
                                showHelp = false;
                                await visualizeStream(seconds);
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
                    Console.WriteLine("pi-cam-test -visfile [raw_filename]");
                    Console.WriteLine("pi-cam-test -rawtomp4 [raw_filename]");
                    Console.WriteLine("pi-cam-test -h264tomp4 [h264_pathname] [mp4_pathname]");
                    Console.WriteLine("pi-cam-test -h264 [seconds]");
                    Console.WriteLine("pi-cam-test -stream [seconds]");
                    Console.WriteLine("pi-cam-test -visstream [seconds]");
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
                var msg = $"Exception: {ex.Message}\n{ex.StackTrace}";
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
            File.Delete(pathname);
            using var handler = new ImageStreamCaptureHandler(pathname);
            Console.WriteLine($"Capturing JPG: {pathname}");
            await cam.TakePicture(handler, MMALEncoding.JPEG, MMALEncoding.I420).ConfigureAwait(false);
            cam.Cleanup();
            Console.WriteLine("Exiting.");
        }

        static async Task bmp()
        {
            var cam = GetConfiguredCamera();

            //MMALCameraConfig.ImageFx = MMAL_PARAM_IMAGEFX_T.MMAL_PARAM_IMAGEFX_EMBOSS;
            
            //MMALCameraConfig.Resolution = new Resolution(640, 480);
            //MMALCameraConfig.SensorMode = MMALSensorMode.Mode7; // for some reason mode 6 has a pinkish tinge
            //MMALCameraConfig.Framerate = new MMAL_RATIONAL_T(20, 1);

            var pathname = ramdiskPath + "snapshot.bmp";
            File.Delete(pathname);
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

                await cameraWarmupDelay(cam);

                Console.WriteLine($"Capturing MP4: {pathname}");
                var timerToken = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
                await Task.WhenAll(new Task[]{
                    ffmpeg.ProcessExternalAsync(timerToken.Token),
                    cam.ProcessAsync(cam.Camera.VideoPort, timerToken.Token)
                }).ConfigureAwait(false);
            }

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
                    EchoOutput = false,
                    DrainOutputDelayMs = 500, // default
                    TerminationSignals = new[] { Signum.SIGINT, Signum.SIGQUIT }, // not the supposedly-correct SIGINT+SIGINT but this produces some exit output
                }))
            {
                // quality arg-help says set bitrate zero to use quality for VBR
                var portCfg = new MMALPortConfig(MMALEncoding.H264, MMALEncoding.I420, quality: 10, bitrate: 0, timeout: null);
                using var encoder = new MMALVideoEncoder();
                encoder.ConfigureOutputPort(portCfg, ffmpeg);
                cam.Camera.VideoPort.ConnectTo(encoder);

                await cameraWarmupDelay(cam);

                Console.WriteLine($"Capturing MP4: {pathname}");
                var timerToken = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
                await Task.WhenAll(new Task[]{
                    ffmpeg.ProcessExternalAsync(timerToken.Token),
                    cam.ProcessAsync(cam.Camera.VideoPort, timerToken.Token),
                }).ConfigureAwait(false);
            }

            cam.Cleanup();

            Console.WriteLine("Exiting. Remember, video.mp4 is not valid.");
        }

        static async Task stream(int seconds)
        {
            var cam = GetConfiguredCamera();

            MMALCameraConfig.Resolution = new Resolution(640, 480);
            MMALCameraConfig.SensorMode = MMALSensorMode.Mode7; // for some reason mode 6 has a pinkish tinge
            MMALCameraConfig.Framerate = 20;

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
                encoder.ConfigureOutputPort(portCfg, vlc);
                cam.Camera.VideoPort.ConnectTo(encoder);

                await cameraWarmupDelay(cam);

                Console.WriteLine($"Streaming MJPEG for {seconds} sec to:");
                Console.WriteLine($"http://{Environment.MachineName}.local:8554/");
                var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
                await Task.WhenAll(new Task[]{
                    vlc.ProcessExternalAsync(timeout.Token),
                    cam.ProcessAsync(cam.Camera.VideoPort, timeout.Token),
                }).ConfigureAwait(false);
            }

            cam.Cleanup();

            Console.WriteLine("Exiting.");
        }

        static async Task visualizeStream(int seconds)
        {
            var cam = GetConfiguredCamera();

            MMALCameraConfig.Resolution = new Resolution(640, 480);
            MMALCameraConfig.SensorMode = MMALSensorMode.Mode7; // for some reason mode 6 has a pinkish tinge
            MMALCameraConfig.Framerate = 20;

            Console.WriteLine("Preparing pipeline...");
            cam.ConfigureCameraSettings();

            var motionAlgorithm = new MotionAlgorithmRGBDiff(
                    rgbThreshold: 200,          // default = 200
                    cellPixelPercentage: 50,    // default = 50
                    cellCountThreshold: 20      // default = 20
                );

            var motionConfig = new MotionConfig(
                    algorithm: motionAlgorithm,
                    testFrameInterval: TimeSpan.FromSeconds(3), // default = 3
                    testFrameCooldown: TimeSpan.FromSeconds(3)  // default = 3
                );

            var raw_to_mjpeg_stream = new ExternalProcessCaptureHandlerOptions
            {
                Filename = "/bin/bash",
                EchoOutput = true,
                Arguments = "-c \"ffmpeg -hide_banner -f rawvideo -c:v rawvideo -pix_fmt rgb24 -s:v 640x480 -r 24 -i - -f h264 -c:v libx264 -preset ultrafast -tune zerolatency -vf format=yuv420p - | cvlc stream:///dev/stdin --sout '#transcode{vcodec=mjpg,vb=2500,fps=20,acodec=none}:standard{access=http{mime=multipart/x-mixed-replace;boundary=7b3cc56e5f51db803f790dad720ed50a},mux=mpjpeg,dst=:8554/}' :demux=h264\"",
                DrainOutputDelayMs = 500, // default = 500
                TerminationSignals = ExternalProcessCaptureHandlerOptions.SignalsFFmpeg
            };

            using (var shell = new ExternalProcessCaptureHandler(raw_to_mjpeg_stream))
            using (var motion = new FrameBufferCaptureHandler(motionConfig, null))
            using (var resizer = new MMALIspComponent())
            {

                motionAlgorithm.EnableAnalysis(shell);

                resizer.ConfigureOutputPort<VideoPort>(0, new MMALPortConfig(MMALEncoding.RGB24, MMALEncoding.RGB24, width: 640, height: 480), motion);
                cam.Camera.VideoPort.ConnectTo(resizer);

                await cameraWarmupDelay(cam);

                Console.WriteLine($"Streaming MJPEG with motion detection analysis for {seconds} sec to:");
                Console.WriteLine($"http://{Environment.MachineName}.local:8554/");
                var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
                await Task.WhenAll(new Task[]{
                        shell.ProcessExternalAsync(timeout.Token),
                        cam.ProcessAsync(cam.Camera.VideoPort, timeout.Token),
                    }).ConfigureAwait(false);
            }

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
                using var motionCaptureHandler = new FrameBufferCaptureHandler();
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

                await cameraWarmupDelay(cam);

                var cts = new CancellationTokenSource(TimeSpan.FromSeconds(totalSeconds));

                var motionAlgorithm = new MotionAlgorithmRGBDiff(
                        rgbThreshold: 200,          // default = 200
                        cellPixelPercentage: 50,    // default = 50
                        cellCountThreshold: 20      // default = 20
                    );

                var motionConfig = new MotionConfig(
                        algorithm: motionAlgorithm, 
                        testFrameInterval: TimeSpan.FromSeconds(3), 
                        testFrameCooldown: TimeSpan.FromSeconds(3)
                    );

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

                await cameraWarmupDelay(cam);

                var cts = new CancellationTokenSource(TimeSpan.FromSeconds(totalSeconds));

                Console.WriteLine($"Detecting motion for {totalSeconds} seconds with sensitivity threshold {sensitivity}...");

                var motionConfig = new MotionConfig(algorithm: new MotionAlgorithmRGBDiff(), testFrameInterval: TimeSpan.FromSeconds(3), motionMaskPathname: motionMaskPath);

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
            using (var resizer = new MMALIspComponent())
            {
                resizer.ConfigureInputPort(new MMALPortConfig(MMALEncoding.OPAQUE, MMALEncoding.I420), cam.Camera.VideoPort, null);
                resizer.ConfigureOutputPort<VideoPort>(0, new MMALPortConfig(MMALEncoding.RGB24, MMALEncoding.RGB24, width: 640, height: 480), capture);
                cam.Camera.VideoPort.ConnectTo(resizer);

                await cameraWarmupDelay(cam);

                Console.WriteLine($"Capturing {totalSeconds} seconds of raw RGB24 video...");
                var cts = new CancellationTokenSource(TimeSpan.FromSeconds(totalSeconds));
                await cam.ProcessAsync(cam.Camera.VideoPort, cts.Token);
            }
            cam.Cleanup();
            Console.WriteLine("Exiting.");
        }


        //-----------------------------------------------------------------------------------------
        // Still-image operations follow

        static async Task snapshotSharpen()
        {
            DeleteFiles(ramdiskPath, "*.jpg");

            //var context = ReadImageFile(ramdiskPath + "snapshot.bmp");
            //var fx = new SharpenProcessor();
            //fx.Apply(context);
            //WriteImageFile(context, ramdiskPath + "snapshot.jpg", ImageFormat.Jpeg);

            var cam = GetConfiguredCamera();
            // Test with a native 32-byte res until Ian replies with his thoughts:
            MMALCameraConfig.Resolution = new Resolution(640, 480);
            MMALCameraConfig.SensorMode = MMALSensorMode.Mode7;

            MMALCameraConfig.Encoding = MMALEncoding.RGB24; // for raw
            MMALCameraConfig.EncodingSubFormat = MMALEncoding.RGB24; // for raw
            cam.ConfigureCameraSettings();
            using (var imgCaptureHandler = new ImageStreamCaptureHandler(ramdiskPath + "snapshot.jpg"))
            {
                imgCaptureHandler.Manipulate(context =>
                {
                    context.Apply(new SharpenProcessor());
                }, ImageFormat.Jpeg);
                //await cam.TakeRawPicture(imgCaptureHandler);
                await cam.TakePicture(imgCaptureHandler, MMALEncoding.JPEG, MMALEncoding.I420);
            }
            cam.Cleanup();
        }

        static async Task snapshotBlur3()
        {
            DeleteFiles(ramdiskPath, "*.jpg");

            var cam = GetConfiguredCamera();
            // Test with a native 32-byte res until Ian replies with his thoughts:
            MMALCameraConfig.Resolution = new Resolution(640, 480);
            MMALCameraConfig.SensorMode = MMALSensorMode.Mode7;

            MMALCameraConfig.Encoding = MMALEncoding.RGB24;
            MMALCameraConfig.EncodingSubFormat = MMALEncoding.RGB24;
            cam.ConfigureCameraSettings();
            using (var imgCaptureHandler = new ImageStreamCaptureHandler(ramdiskPath + "snapshot.jpg"))
            {
                imgCaptureHandler.Manipulate(context =>
                {
                    context.Apply(new GaussianProcessor(GaussianMatrix.Matrix3x3));
                }, ImageFormat.Jpeg);
                //await cam.TakeRawPicture(imgCaptureHandler);
                await cam.TakePicture(imgCaptureHandler, MMALEncoding.JPEG, MMALEncoding.I420);
            }
            cam.Cleanup();
        }

        static async Task snapshotBlur5()
        {
            DeleteFiles(ramdiskPath, "*.jpg");

            var cam = GetConfiguredCamera();
            // Test with a native 32-byte res until Ian replies with his thoughts:
            MMALCameraConfig.Resolution = new Resolution(640, 480);
            MMALCameraConfig.SensorMode = MMALSensorMode.Mode7;

            MMALCameraConfig.Encoding = MMALEncoding.RGB24;
            MMALCameraConfig.EncodingSubFormat = MMALEncoding.RGB24;
            cam.ConfigureCameraSettings();
            using (var imgCaptureHandler = new ImageStreamCaptureHandler(ramdiskPath + "snapshot.jpg"))
            {
                imgCaptureHandler.Manipulate(context =>
                {
                    context.Apply(new GaussianProcessor(GaussianMatrix.Matrix5x5));
                }, ImageFormat.Jpeg);
                //await cam.TakeRawPicture(imgCaptureHandler);
                await cam.TakePicture(imgCaptureHandler, MMALEncoding.JPEG, MMALEncoding.I420);
            }
            cam.Cleanup();
        }

        static async Task snapshotEdge()
        {
            DeleteFiles(ramdiskPath, "*.jpg");

            var cam = GetConfiguredCamera();
            // Test with a native 32-byte res until Ian replies with his thoughts:
            MMALCameraConfig.Resolution = new Resolution(640, 480);
            MMALCameraConfig.SensorMode = MMALSensorMode.Mode7;

            MMALCameraConfig.Encoding = MMALEncoding.RGB24;
            MMALCameraConfig.EncodingSubFormat = MMALEncoding.RGB24;
            cam.ConfigureCameraSettings();
            using (var imgCaptureHandler = new ImageStreamCaptureHandler(ramdiskPath + "snapshot.jpg"))
            {
                imgCaptureHandler.Manipulate(context =>
                {
                    context.Apply(new EdgeDetection(EDStrength.High));
                }, ImageFormat.Jpeg);
                //await cam.TakeRawPicture(imgCaptureHandler);
                await cam.TakePicture(imgCaptureHandler, MMALEncoding.JPEG, MMALEncoding.I420);
            }
            cam.Cleanup();
        }


        //-----------------------------------------------------------------------------------------
        // File-based operations follow

        static async Task visualizeFile(string rawFilename)
        {
            Console.WriteLine("Preparing pipeline...");
            MMALStandalone standalone = MMALStandalone.Instance;

            var rawPathname = ramdiskPath + rawFilename;
            var analysisPathname = ramdiskPath + "analysis.raw";
            File.Delete(analysisPathname);

            var motionAlgorithm = new MotionAlgorithmRGBDiff(
                    rgbThreshold: 200,          // default = 200
                    cellPixelPercentage: 50,    // default = 50
                    cellCountThreshold: 20      // default = 20
                );

            var motionConfig = new MotionConfig(
                    algorithm: motionAlgorithm,
                    testFrameInterval: TimeSpan.FromSeconds(999), // disable for visualization purposes
                    testFrameCooldown: TimeSpan.FromSeconds(3)
                );

            using (var stream = File.OpenRead(rawPathname))
            using (var input = new InputCaptureHandler(stream))
            using (var output = new VideoStreamCaptureHandler(analysisPathname))
            using (var motion = new FrameBufferCaptureHandler(motionConfig, null))
            using (var resizer = new MMALIspComponent())
            {
                var cfg = new MMALPortConfig(MMALEncoding.RGB24, MMALEncoding.RGB24, width: 640, height: 480, framerate: 24, zeroCopy: true);
                resizer.ConfigureInputPort(cfg, null, input);
                resizer.ConfigureOutputPort<FileEncodeOutputPort>(0, cfg, output);

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


        //-----------------------------------------------------------------------------------------
        // Performance operations follow

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

        //-----------------------------------------------------------------------------------------
        // Utilities follow

        static async Task cameraWarmupDelay(MMALCamera cam)
        {
            Console.WriteLine("Camera warmup...");
            await Task.Delay(2000);

            if (useDebug)
            {
                Console.WriteLine("Dumping pipeline to debug log");
                cam.PrintPipeline();
            }
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

            // V1 Camera (mode 0 = auto)
            // Mode Size        Aspect Ratio    Frame rates     FOV Binning         32-byte-aligned buffer width
            // 1    1920x1080   16:9            1 - 30fps       Partial none        no padding
            // 2    2592x1944   4:3             1 - 15fps       Full none           no padding
            // 3    2592x1944   4:3             0.1666 - 1fps   Full none           no padding
            // 4    1296x972    4:3             1 - 42fps       Full 2x2            1312
            // 5    1296x730    16:9            1 - 49fps       Full 2x2            1312
            // 6    640x480     4:3             42.1 - 60fps    Full 2x2 plus skip  no padding
            // 7    640x480     4:3             60.1 - 90fps    Full 2x2 plus skip  no padding

            // V2 Camera (mode 0 = auto)
            // Mode Size        Aspect Ratio    Frame rates     FOV Binning         32-byte-aligned buffer width
            // 1    1920x1080   16:9            1 - 30fps       Partial none        no padding
            // 2    3280x2464   4:3             0.1 - 15fps     Full none           3296
            // 3    3280x2464   4:3             0.1 - 15fps     Full none           3296
            // 4    1640x1232   4:3             0.1 - 40fps     Full 2x2            1664
            // 5    1640x922    16:9            0.1 - 40fps     Full 2x2            1664
            // 6    1280x720    16:9            40 - 90fps      Partial 2x2         no padding
            // 7    640x480     4:3             60.1 - 90fps    Full 2x2 plus skip  no padding

            // HQ Camera (mode 0 = auto)
            // Mode Size        Aspect Ratio    Frame rates     FOV Binning         32-byte-aligned buffer width
            // 1    2028x1080   169:90          0.1 - 50fps     Partial 2x2 binned  2048
            // 2    2028x1520   4:3             0.1 - 50fps     Full 2x2 binned     2048
            // 3    4056x3040   4:3             0.005 - 10fps   Full none           4064
            // 4    1012x760    4:3             50.1 - 120fps   Full 4x4 scaled     1024

            MMALCameraConfig.Resolution = new Resolution(1296, 972);
            MMALCameraConfig.SensorMode = MMALSensorMode.Mode4;

            MMALCameraConfig.Framerate = 20;

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

        static ImageContext ReadRawImageFile(string pathname)
        {
            var ctx = new ImageContext();
            using (var fs = new FileStream(pathname, FileMode.Open, FileAccess.Read))
            using (var bitmap = new Bitmap(fs))
            {
                BitmapData bmpData = null;
                try
                {
                    bmpData = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, bitmap.PixelFormat);
                    var ptr = bmpData.Scan0;
                    int size = bmpData.Stride * bitmap.Height;
                    var data = new byte[size];
                    Marshal.Copy(ptr, data, 0, size);

                    ctx.Data = data;
                    ctx.Resolution = new Resolution(bmpData.Width, bmpData.Height);
                    ctx.Stride = bmpData.Stride;
                    ctx.Eos = true;
                    ctx.Raw = true;

                    ctx.PixelFormat = bmpData.PixelFormat switch
                    {
                        PixelFormat.Format24bppRgb => MMALEncoding.RGB24,
                        PixelFormat.Format32bppRgb => MMALEncoding.RGB32,
                        PixelFormat.Format32bppArgb => MMALEncoding.RGBA,
                        _ => throw new Exception("Unsupported pixel format for raw file")
                    };
                }
                finally
                {
                    bitmap.UnlockBits(bmpData);
                }
            }
            return ctx;
        }

        static void WriteFormattedImageFile(ImageContext ctx, string pathname, ImageFormat format)
        {
            PixelFormat pixfmt = default;
            if (ctx.PixelFormat == MMALEncoding.RGB24) pixfmt = PixelFormat.Format24bppRgb;
            if (ctx.PixelFormat == MMALEncoding.RGB32) pixfmt = PixelFormat.Format32bppRgb;
            if (ctx.PixelFormat == MMALEncoding.RGBA) pixfmt = PixelFormat.Format32bppArgb;
            if (pixfmt == default) throw new Exception("Goddammit");

            using (var bitmap = new Bitmap(ctx.Resolution.Width, ctx.Resolution.Height, pixfmt))
            {
                BitmapData bmpData = null;
                try
                {
                    bmpData = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.WriteOnly, bitmap.PixelFormat);
                    var ptr = bmpData.Scan0;
                    int size = bmpData.Stride * bitmap.Height;
                    var data = ctx.Data;
                    Marshal.Copy(data, 0, ptr, size);
                }
                finally
                {
                    bitmap.UnlockBits(bmpData);
                }
                bitmap.Save(pathname, format);
            }
        }
    }
}
