using Microsoft.Extensions.Logging;
using MMALSharp;
using MMALSharp.Common;
using MMALSharp.Common.Utility;
using MMALSharp.Components;
using MMALSharp.Config;
using MMALSharp.Handlers;
using MMALSharp.Native;
using MMALSharp.Ports;
using MMALSharp.Ports.Outputs;
using MMALSharp.Processors.Motion;
using Serilog;
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace pi_cam_test
{
    // TODO
    // verify defaults like MMAL FPS vs FFMPEG command line
    // implement splitter system
    // investigate ffmpeg internal MMAL support (noticed in a forum post)

    class Program
    {
        private static readonly string outputPath = "/media/ramdisk/";

        private static bool useDebug = false;

        static async Task Main(string[] args)
        {
            var stopwatch = new Stopwatch();
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

                        case "-mp4":
                            if (hasSeconds)
                            {
                                showHelp = false;
                                await mp4(seconds);
                            }
                            break;

                        case "-stream":
                            if (hasSeconds)
                            {
                                showHelp = false;
                                await stream(seconds);
                            }
                            break;

                        case "-motion":
                            if (hasSeconds)
                            {
                                showHelp = false;
                                await motion(seconds);
                            }
                            break;
                    }
                }

                if(showHelp)
                {
                    Console.WriteLine("Usage:\npi-cam-test -jpg\npi-cam-test -mp4 [seconds]\npi-cam-test -stream [seconds]\npi-cam-test -motion [seconds]");
                    Console.WriteLine("\nAdd -debug to activate MMALSharp verbose debug logging.\nAll files are output to /media/ramdisk.\n*** Motion detection deletes all RAW and H264 files from the ramdisk.\n");
                }
            }
            catch(Exception ex)
            {
                var msg = $"Exception: {ex.Message}";
                Console.WriteLine($"\n\n{msg}");
                if (MMALLog.Logger != null) MMALLog.Logger.LogError(msg);
            }
            stopwatch.Stop();
            int min = (int)stopwatch.Elapsed.TotalMinutes;
            int sec = (int)stopwatch.Elapsed.TotalSeconds - (min * 60);
            Console.WriteLine($"\nElapsed: {min}:{sec:D2}\n\n");
            if (MMALLog.Logger != null) MMALLog.Logger.LogDebug("Application exit");
        }

        static async Task jpg()
        {
            var cam = GetConfiguredCamera();
            var pathname = outputPath + "snapshot.jpg";
            using var handler = new ImageStreamCaptureHandler(pathname);
            Console.WriteLine($"Capturing JPG: {pathname}");
            await cam.TakePicture(handler, MMALEncoding.JPEG, MMALEncoding.I420).ConfigureAwait(false);

            Console.WriteLine("cam.Cleanup");
            cam.Cleanup(); // no exception here, only in mp4 and stream demos...?

            Console.WriteLine("Exiting.");
        }

        static async Task mp4(int seconds)
        {
            var cam = GetConfiguredCamera();
            var pathname = outputPath + "video.mp4";
            Directory.CreateDirectory(outputPath);
            File.Delete(pathname);

            Console.WriteLine("Preparing pipeline...");
            cam.ConfigureCameraSettings();
            var stdout = ExternalProcessCaptureHandler.CreateStdOutBuffer();
            using var ffmpeg = new ExternalProcessCaptureHandler("ffmpeg", $"-framerate 24 -i - -b:v 2500k -c copy {pathname}", stdout);
            // quality arg-help says set bitrate zero to use quality (for VBR), versus setting MMALVideoEncoder.MaxBitrateLevel4 (25MBps)
            var portCfg = new MMALPortConfig(MMALEncoding.H264, MMALEncoding.I420, quality: 10, bitrate: 0, timeout: null);
            using var encoder = new MMALVideoEncoder();
            using var renderer = new MMALVideoRenderer();
            encoder.ConfigureOutputPort(portCfg, ffmpeg);
            cam.Camera.VideoPort.ConnectTo(encoder);
            cam.Camera.PreviewPort.ConnectTo(renderer);

            Console.WriteLine("Camera warmup...");
            await Task.Delay(2000);

            Console.WriteLine($"Capturing MP4: {pathname}");
            var token = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
            await Task.WhenAll(new Task[]{
                cam.ProcessAsync(cam.Camera.VideoPort, token.Token),
                ExternalProcessCaptureHandler.EmitStdOutBuffer(stdout, token.Token)
            }).ConfigureAwait(false);

            //Console.WriteLine("cam.Cleanup disabled until exception is explained");
            Console.WriteLine("cam.Cleanup");
            cam.Cleanup(); // throws: Argument is invalid. Unable to destroy component

            Console.WriteLine("Exiting.");
        }

        static async Task stream(int seconds)
        {
            // currently only works once, subsequent runs require reboot; errors:
            // mmal: mmal_vc_component_enable: failed to enable component: ENOSPC
            // Out of resources. Unable to enable component

            var cam = GetConfiguredCamera();

            MMALCameraConfig.VideoResolution = new MMALSharp.Common.Utility.Resolution(640, 480);
            MMALCameraConfig.SensorMode = MMALSensorMode.Mode7; // for some reason mode 6 has a pinkish tinge
            MMALCameraConfig.VideoFramerate = new MMAL_RATIONAL_T(20, 1);

            Console.WriteLine("Preparing pipeline...");
            cam.ConfigureCameraSettings();
            var stdout = ExternalProcessCaptureHandler.CreateStdOutBuffer();
            // note cvlc requires real quotes even though we used apostrophes for the command line equivalent
            using var vlc = new ExternalProcessCaptureHandler("cvlc", @"stream:///dev/stdin --sout ""#transcode{vcodec=mjpg,vb=2500,fps=20,acodec=none}:standard{access=http{mime=multipart/x-mixed-replace;boundary=--7b3cc56e5f51db803f790dad720ed50a},mux=mpjpeg,dst=:8554/}"" :demux=h264", stdout);
            // MMALVideoEncoder.MaxBitrateMJPEG = 25MBps
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
            var token = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
            await Task.WhenAll(new Task[]{
                cam.ProcessAsync(cam.Camera.VideoPort, token.Token),
                ExternalProcessCaptureHandler.EmitStdOutBuffer(stdout, token.Token)
            }).ConfigureAwait(false);

            Console.WriteLine("cam.Cleanup disabled until exception is explained");
            //cam.Cleanup(); // throws: Argument is invalid. Unable to destroy component

            Console.WriteLine("Exiting.");
        }

        // https://github.com/techyian/MMALSharp/wiki/Advanced-Examples/93b717ebbf4502f3a6c1ef99137a6b416dd0c3e6#motion-detection---frame-difference
        static async Task motion(int seconds)
        {
            var h264files = outputPath + "*.h264";
            var rawfiles = outputPath + "*.raw";
            File.Delete(h264files);
            File.Delete(rawfiles);
            
            var cam = GetConfiguredCamera();

            // When using H.264 encoding we require key frames to be generated for the Circular buffer capture handler.
            MMALCameraConfig.InlineHeaders = true;

            Console.WriteLine("Preparing pipeline...");
            // Two capture handlers are being used here, one for motion detection and the other to record a H.264 stream.
            using var vidCaptureHandler = new CircularBufferCaptureHandler(4000000, "/media/ramdisk", "h264");
            using var motionCircularBufferCaptureHandler = new CircularBufferCaptureHandler(4000000, "/media/ramdisk", "raw");
            using var splitter = new MMALSplitterComponent();
            using var resizer = new MMALIspComponent();
            using var vidEncoder = new MMALVideoEncoder();
            using var renderer = new MMALVideoRenderer();
            cam.ConfigureCameraSettings();

            var splitterPortConfig = new MMALPortConfig(MMALEncoding.OPAQUE, MMALEncoding.I420, 0, 0, null);
            var vidEncoderPortConfig = new MMALPortConfig(MMALEncoding.H264, MMALEncoding.I420, 0, MMALVideoEncoder.MaxBitrateLevel4, null);

            // The ISP resizer is being used for better performance. Frame difference motion detection will only work if using raw video data. Do not encode to H.264/MJPEG.
            // Resizing to a smaller image may improve performance, but ensure that the width/height are multiples of 32 and 16 respectively to avoid cropping.
            var resizerPortConfig = new MMALPortConfig(MMALEncoding.RGB24, MMALEncoding.RGB24, 640, 480, 0, 0, 0, false, null);

            splitter.ConfigureInputPort(new MMALPortConfig(MMALEncoding.OPAQUE, MMALEncoding.I420), cam.Camera.VideoPort, null);
            splitter.ConfigureOutputPort(0, splitterPortConfig, null);
            splitter.ConfigureOutputPort(1, splitterPortConfig, null);

            resizer.ConfigureOutputPort<VideoPort>(0, resizerPortConfig, motionCircularBufferCaptureHandler);

            vidEncoder.ConfigureInputPort(new MMALPortConfig(MMALEncoding.OPAQUE, MMALEncoding.I420), splitter.Outputs[1], null);
            vidEncoder.ConfigureOutputPort(vidEncoderPortConfig, vidCaptureHandler);

            cam.Camera.VideoPort.ConnectTo(splitter);
            cam.Camera.PreviewPort.ConnectTo(renderer);

            splitter.Outputs[0].ConnectTo(resizer);
            splitter.Outputs[1].ConnectTo(vidEncoder);

            Console.WriteLine("Camera warmup...");
            await Task.Delay(2000);

            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));

            // Here we are instructing the capture handler to record for 10 seconds once motion has been detected. A threshold of 130 is used. Lower 
            // values indicate higher sensitivity. Suitable range for indoor detection between 120-150 with stable lighting conditions.
            var motionConfig = new MotionConfig(TimeSpan.FromSeconds(10), 130);

            Console.WriteLine($"Detecting and recording motion for {seconds} seconds...");

            await cam.WithMotionDetection(
                motionCircularBufferCaptureHandler, 
                motionConfig, 
                () =>
                {
                    // This callback will be invoked when motion has been detected.
                    Console.WriteLine("Motion detected, recording.");

                    // Stop motion detection while we are recording.
                    motionCircularBufferCaptureHandler.DisableMotionDetection();

                    // Optional, this will begin recording the raw video frames. Produces large video files which will need encoding afterwards.
                    motionCircularBufferCaptureHandler.StartRecording();

                    // Start recording our H.264 video.
                    vidCaptureHandler.StartRecording();

                    // (Optionally) Request a key frame to be immediately generated by the video encoder.
                    vidEncoder.RequestIFrame();
                }, 
                () =>
                {
                    // This callback will be invoked when the record duration has passed.
                    Console.WriteLine("...recording stopped.");

                    // We want to re-enable the motion detection.
                    motionCircularBufferCaptureHandler.EnableMotionDetection();

                    // Stop recording on our capture handlers.
                    motionCircularBufferCaptureHandler.StopRecording();
                    vidCaptureHandler.StopRecording();

                    // Optionally create two new files for our next recording run.
                    vidCaptureHandler.Split();
                    motionCircularBufferCaptureHandler.Split();
                })
                .ProcessAsync(cam.Camera.VideoPort, cts.Token);

            Console.WriteLine("cam.Cleanup disabled until exception is explained");
            //cam.Cleanup(); // throws: Argument is invalid. Unable to destroy component

            Console.WriteLine("Exiting.");
        }

        static MMALCamera GetConfiguredCamera()
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
            MMALCameraConfig.StillResolution = new MMALSharp.Common.Utility.Resolution(1296, 972);
            MMALCameraConfig.VideoResolution = new MMALSharp.Common.Utility.Resolution(1296, 972);
            MMALCameraConfig.SensorMode = MMALSensorMode.Mode4;
            MMALCameraConfig.VideoFramerate = new MMAL_RATIONAL_T(24, 1); // numerator & denominator

            // overlay text
            var overlay = new AnnotateImage(Environment.MachineName, 30, Color.White)
            {
                ShowDateText = true,
                ShowTimeText = true,
            };
            MMALCameraConfig.Annotate = overlay;
            cam.EnableAnnotation();

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
    }
}
