# pi-cam-test

Getting to know the Raspberry Pi camera and the [MMALSharp](https://github.com/techyian/MMALSharp) .NET library.

Mostly these are variations on the samples in MMALSharp's wiki. Probably the most interesting part is `ExternalProcessCaptureHandler` which is a generic process-management overhaul based on MMALSharp's FFmpegCaptureHandler.

```
Usage:
pi-cam-test -jpg
pi-cam-test -h264 [seconds]
pi-cam-test -stream [seconds]
pi-cam-test -motion [seconds]
pi-cam-test -copyperf
pi-cam-test -transcodeperf [local|lan]
pi-cam-test -badmp4 [seconds]

Add "-debug" for verbose logging (from MMALSharp).
```

Assumptions, defaults, hard-coded paths, etc:
* the older 5MP V1 camera
* files are output to `/media/ramdisk`
* still/capture is 1296 x 972 at 24 FPS (2x2 binning mode 4)
* MJPEG streaming is 640 x 480 (4x4 binning mode 7)
* requires ffmpeg and cvlc
* copyperf times network file copy to `/media/nas_dvr/smartcam/`

Motion-detection requires the GDI+ library:

`sudo apt-get install libgdiplus` 

Since this writes to ramdisk, keep in mind motion-detection can quickly produce very large files. I'm using a 1GB ramdisk and the .raw files fill that up after just 60 seconds or so with just four motion-detection events (saving the .raw files is now disabled).

The transcode perf tests were sort of interesting. 60 seconds of h.264 video is just under 214MB (and transcoding to MP4 doesn't change it much). ffmpeg transcodes that locally (using copy mode) in just 3 seconds, and can move the file to the NAS over wifi in 22 seconds (my wifi is very busy/noisy). That's acceptable (and I'm still not entirely sure I'll bother with the MP4 step).

MP4 generation is broken (hence the switch name -badmp4) -- it's an old, well-known ffmpeg problem, it won't do a clean shutdown when running as a child process so it fails to output the trailing-header (a structure called the MOOV atom, described as the "table of contents" of the file). This led to adding "correct" process termination support by sending Unix sigint signals. More details in comments in the code.


