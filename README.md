# pi-cam-test

Getting to know the Raspberry Pi camera and the [MMALSharp](https://github.com/techyian/MMALSharp) .NET library.

Mostly working demo-quality sample code. It has a bug I'm chasing down where the final call to `Cleanup` throws an exception. `ExternalProcessCaptureHandler` is a mild modification to MMALSharp's FFmpegCaptureHandler. Some details [here](https://github.com/techyian/MMALSharp/issues/154) if you're bored.

```
Usage:
pi-cam-test -jpg
pi-cam-test -mp4 [seconds]
pi-cam-test -stream [seconds]
pi-cam-test -motion [seconds]

Add "-debug" for verbose logging (from MMALSharp).
```

Assumptions/defaults/etc:
* the older 5MP V1 camera
* files are output to /media/ramdisk
* JPG and MP4 are in 1296 x 972 at 24 FPS (2x2 binning mode 4)
* MJPEG streaming is 640 x 480 (4x4 binning mode 7)
* requires ffmpeg and cvlc

Motion-detection requires the GDI+ library:

`sudo apt-get install libgdiplus` 

Since this writes to ramdisk, keep in mind motion-detection can quickly produce very large files. I'm using a 1GB ramdisk and the .raw files fill that up after just 60 seconds or so with just four motion-detection events.