# pi-cam-test

| :warning: This branch depends on a _LOCALLY-STORED_ pre-release v0.7 build of MMALSharp packages. |
| --- |

Getting to know the Raspberry Pi camera and the [MMALSharp](https://github.com/techyian/MMALSharp) .NET library.

Mostly these are variations on the samples in MMALSharp's wiki. Probably the most interesting part is `ExternalProcessCaptureHandler` which is a generic process-management overhaul based on MMALSharp's FFmpegCaptureHandler.

```
Usage:
pi-cam-test -jpg
pi-cam-test -h264 [seconds]
pi-cam-test -stream [seconds]
pi-cam-test -motion [detect-seconds] [record-seconds] [sensitivity]
pi-cam-test -copyperf
pi-cam-test -transcodeperf [ram|sd|lan]
pi-cam-test -fragmp4 [seconds]
pi-cam-test -badmp4 [seconds]

Add "-debug" for verbose logging (from MMALSharp).
```

Assumptions, defaults, hard-coded paths, etc:
* tested on Rpi4B with the older 5MP V1 camera
* local output: `/media/ramdisk`
* network output: `/media/nas_dvr/smartcam/`
* transcoding SD card test: `/home/pi/`
* still/capture is 1296 x 972 at 24 FPS (2x2 binning mode 4)
* MJPEG streaming is 640 x 480 (4x4 binning mode 7)
* requires ffmpeg and cvlc
* currently targets .NET Core 3.1 plus some Mono bits

Motion-detection requires the GDI+ library:

`sudo apt-get install libgdiplus` 

Since this writes to ramdisk, keep in mind motion-detection can quickly produce very large files. I'm using a 1GB ramdisk and the .raw files fill that up after just 60 seconds or so with just four motion-detection events (saving the .raw files is now disabled).

MP4 generation is broken (hence the switch name -badmp4) -- it's an old, well-known ffmpeg problem, it won't do a clean shutdown when running as a child process so it fails to output the trailing-header (a structure called the MOOV atom, described as the "table of contents" of the file). This led to adding "correct" process termination support by sending Unix sigint signals, but that didn't fix it. More details in comments in the code.

The other MP4 option, -fragmp4, produces a "fragmented" MP4 which means it dumps keyframes a lot more often. Supposedly it should produce a larger file, but it does not -- I think because the final buffer isn't being written. It's actually a smaller file than the broken MP4, but also truncated -- a 10 second timeout produces 8 seconds of video. Regardless, it's not really correct either, but the code is hacked to add 2 seconds to the requested time to account for this. It seems independent of the requested duration.

Apart from just learning the MMALSharp library, this project was also used to test some ideas and scenarios in preparation for my [smartcam](https://github.com/MV10/smartcam) project.

The transcode perf tests in particular were interesting. 60 seconds of h.264 video can hit 300 MB depending on scene movement, and transcoding to MP4 doesn't change it much. ffmpeg transcodes that to ramdisk (using copy mode and the +faststart option) in just 4 seconds, and can move the file to the NAS over wifi in about 30 seconds (my wifi is very busy/noisy). That's acceptable for my purposes. To my surprise, transcoding to the SD card was slower than I expected at 51 seconds. Transcoding directly to the NAS was the worst (not surprisingly) at more than 2 minutes.

The +faststart option produces a lot of overhead, however (especially when writing to the network), because ffmpeg writes a whole new copy of the file to put that MOOV atom at the beginning of the file. Without that, transcoding the same h.264 file to ramdisk is 1 second faster, but SD card transcoding is a mere 9 seconds (5.5x faster!) and transcoding directly to the NAS is just 32 seconds -- virtually the same as transcoding to RAM then doing a file-copy.

I'll probably add a feature to transcode a given recording on demand after the camera system decides to record and store something, rather than transcoding everything. These were useful tests to help me plan my project. The code in the repository has the faststart option commented out.


