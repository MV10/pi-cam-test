# pi-cam-test

Getting to know the Raspberry Pi camera and the [MMALSharp](https://github.com/techyian/MMALSharp) .NET library.

```
Usage:
pi-cam-test -jpg
pi-cam-test -mp4 [seconds]
pi-cam-test -stream [seconds]

TODO:
pi-cam-test -motion
```

Assumptions/defaults/etc:
* the older 5MP V1 camera
* files are output to /media/ramdisk
* JPG and MP4 are in 1296 x 972 at 24 FPS (2x2 binning mode 4)
* MJPEG streaming is 640 x 480 (4x4 binning mode 7)
* requires ffmpeg and cvlc

Has some bugs I'm chasing down. Running lots of MP4 captures or just one streaming call kills MMAL and requires reboot. `ExternalProcessCaptureHandler` is a mild modification to MMALSharp's FFmpegCaptureHandler. Some details [here](https://github.com/techyian/MMALSharp/issues/154) if you're bored.
