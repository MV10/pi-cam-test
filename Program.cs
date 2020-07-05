﻿using MMALSharp;
using MMALSharp.Common;
using MMALSharp.Components;
using MMALSharp.Config;
using MMALSharp.Handlers;
using MMALSharp.Native;
using MMALSharp.Ports;
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
    // text overlay
    // implement splitter system
    // investigate ffmpeg internal MMAL support (noticed in a forum post)

    class Program
    {
        private static readonly string outputPath = "/media/ramdisk/";

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

                    switch (args[0].ToLower())
                    {
                        case "-jpg":
                            showHelp = false;
                            await jpg();
                            break;

                        case "-mp4":
                            if (args.Length == 2 && int.TryParse(args[1], out var duration))
                            {
                                showHelp = false;
                                await mp4(duration);
                            }
                            break;

                        case "-stream":
                            if (args.Length == 2 && int.TryParse(args[1], out var seconds))
                            {
                                showHelp = false;
                                await stream(seconds);
                            }
                            break;

                        case "-motion":
                            Console.WriteLine("Motion detection not yet implemented.");
                            return;
                    }
                }

                if(showHelp)
                {
                    Console.WriteLine("Usage:\npi-cam-test -jpg\npi-cam-test -mp4 [seconds]\npi-cam-test -stream [seconds]\npi-cam-test -stream\npi-cam-test -motion");
                    Console.WriteLine("\nAll files are output to /media/ramdisk.\n\n");
                }
            }
            catch(Exception ex)
            {
                Console.WriteLine($"\n\nException:\n{ex.Message}");
            }
            stopwatch.Stop();
            int min = (int)stopwatch.Elapsed.TotalMinutes;
            int sec = (int)stopwatch.Elapsed.TotalSeconds - (min * 60);
            Console.WriteLine($"\nElapsed: {min}:{sec:D2}\n\n");
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
            var ffmpeg = new ExternalProcessCaptureHandler("ffmpeg", $"-framerate 24 -i - -b:v 2500k -c copy {pathname}", stdout);
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

            Console.WriteLine("cam.Cleanup disabled until exception is explained");
            //cam.Cleanup(); // throws: Argument is invalid. Unable to destroy component

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
            var vlc = new ExternalProcessCaptureHandler("cvlc", @"stream:///dev/stdin --sout ""#transcode{vcodec=mjpg,vb=2500,fps=20,acodec=none}:standard{access=http{mime=multipart/x-mixed-replace;boundary=--7b3cc56e5f51db803f790dad720ed50a},mux=mpjpeg,dst=:8554/}"" :demux=h264", stdout);
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

        static async Task motion()
        {
            throw new NotImplementedException();
        }

        static MMALCamera GetConfiguredCamera()
        {
            Console.WriteLine("Initializing...");
            var cam = MMALCamera.Instance;

            // 1296 x 972 with 2x2 binning (full-frame 4:3 capture)
            MMALCameraConfig.StillResolution = new MMALSharp.Common.Utility.Resolution(1296, 972);
            MMALCameraConfig.VideoResolution = new MMALSharp.Common.Utility.Resolution(1296, 972);
            MMALCameraConfig.SensorMode = MMALSensorMode.Mode4;
            MMALCameraConfig.VideoFramerate = new MMAL_RATIONAL_T(24, 1); // numerator & denominator

            // overlay text
            var overlay = new AnnotateImage(" " + Environment.MachineName, 30, Color.White)
            {
                AllowCustomBackgroundColour = true,
                BgColour = Color.FromArgb(80, 0, 80),
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
