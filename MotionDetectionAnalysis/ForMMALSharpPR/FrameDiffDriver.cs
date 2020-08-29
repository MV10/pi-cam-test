// <copyright file="FrameDiffDriver.cs" company="Techyian">
// Copyright (c) Ian Auty and contributors. All rights reserved.
// Licensed under the MIT License. Please see LICENSE.txt for License info.
// </copyright>

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using MMALSharp.Common;
using MMALSharp.Common.Utility;

namespace MMALSharp.Processors.Motion
{
    /// <summary>
    /// A frame difference motion detection base class which buffers a test frame and a current frame,
    /// stores frame metrics, and invokes an <see cref="IFrameDiffAlgorithm"/> to analyse full frames.
    /// </summary>
    public class FrameDiffDriver : FrameAnalyser
    {
        // add to MotionConfig
        private TimeSpan TestFrameRefreshCooldown = TimeSpan.FromSeconds(3);


        // Prefer fields over properties for parallel processing performance reasons.
        // Parallel processing references unique array indices, so arrays do not need
        // to be stored in the passed-by-value FrameDiffMetrics struct.

        // Various properties of the frame collected when the first full frame is available.
        // A copy of this struct is passed into the parallel processing algorithm. It is private
        // rather than internal to prevent accidental parallel reads against this copy.
        private FrameDiffMetrics _frameMetrics;

        private Action _onDetect;
        private bool _firstFrame = true;
        private MotionConfig _motionConfig;
        private bool _fullTestFrame;
        private ImageContext _imageContext;

        /// <summary>
        /// Fully are skipped when comparing the test frame to the current frame.
        /// </summary>
        internal byte[] FrameMask;

        /// <summary>
        ///  This is the image we are comparing against new incoming frames.
        /// </summary>
        internal byte[] TestFrame;

        /// <summary>
        /// A byte array representation of the FrameAnalyser's own WorkingData object. Required
        /// to provide fast thread-safe access for parallel analysis.
        /// </summary>
        internal byte[] CurrentFrame;

        /// <summary>
        /// The number of pixels that differ in each cell between the test frame and current frame.
        /// </summary>
        internal int[] CellDiff;

        /// <summary>
        /// Represents the coordinates of each test cell for parallel analysis.
        /// </summary>
        internal Rectangle[] CellRect;

        private Stopwatch _testFrameAge;
        private Stopwatch _lastMotionAge;

        private IFrameDiffAlgorithm _frameDiffAlgorithm;

        /// <summary>
        /// Controls how many cells the frames are divided into. The result is a power of two of this
        /// value (so the default of 32 yields 1024 cells). These cells are processed in parallel. This
        /// should be a value that divides evenly into the X and Y resolutions of the motion stream.
        /// </summary>
        public int CellDivisor { get; set; } = 32;

        /// <summary>
        /// Creates a new instance of <see cref="FrameDiffDriver"/>.
        /// </summary>
        /// <param name="config">The motion configuration object.</param>
        /// <param name="onDetect">A callback when changes are detected.</param>
        public FrameDiffDriver(MotionConfig config, IFrameDiffAlgorithm algorithm, Action onDetect)
        {
            _motionConfig = config;
            _frameDiffAlgorithm = algorithm;
            _onDetect = onDetect;

            _frameMetrics = new FrameDiffMetrics
            {
                Threshold = _motionConfig.Threshold // copy object member to thread safe struct member
            };

            _testFrameAge = new Stopwatch();
            _lastMotionAge = new Stopwatch();
        }

        /// <inheritdoc />
        public override void Apply(ImageContext context)
        {
            _imageContext = context;

            base.Apply(context);

            if (context.Eos)
            {
                // if zero bytes buffered, EOS is the end of a physical input video filestream
                if (this.WorkingData.Count > 0)
                {
                    if (!_fullTestFrame)
                    {
                        MMALLog.Logger.LogDebug("EOS reached for test frame.");

                        _fullTestFrame = true;
                        this.PrepareTestFrame();
                    }
                    else
                    {
                        MMALLog.Logger.LogDebug("Have full frame, checking for changes.");

                        CurrentFrame = this.WorkingData.ToArray();
                        var detected = _frameDiffAlgorithm.AnalyseFrames(this, _frameMetrics);
                        
                        if(detected)
                        {
                            _lastMotionAge.Restart();
                            _onDetect?.Invoke();
                        }

                        this.TryUpdateTestFrame();
                    }
                }
                else
                {
                    MMALLog.Logger.LogDebug("EOS reached, no working data buffered");
                }
            }
        }

        /// <inheritdoc />
        public virtual void PrepareTestFrame()
        {
            this.TestFrame = this.WorkingData.ToArray();

            if (_firstFrame)
            {
                _firstFrame = false;

                // one-time collection of basic frame dimensions
                _frameMetrics.FrameWidth = _imageContext.Resolution.Width;
                _frameMetrics.FrameHeight = _imageContext.Resolution.Height;
                _frameMetrics.FrameBpp = this.GetBpp() / 8;
                _frameMetrics.FrameStride = _imageContext.Stride;

                // one-time setup of the diff cell parameters and arrays
                int indices = (int)Math.Pow(CellDivisor, 2);
                int cellWidth = _frameMetrics.FrameWidth / CellDivisor;
                int cellHeight = _frameMetrics.FrameHeight / CellDivisor;
                int i = 0;

                CellRect = new Rectangle[indices];
                CellDiff = new int[indices];

                for (int row = 0; row < CellDivisor; row++)
                {
                    int y = row * cellHeight;
                    for (int col = 0; col < CellDivisor; col++)
                    {
                        int x = col * cellWidth;
                        CellRect[i] = new Rectangle(x, y, cellWidth, cellHeight);
                        i++;
                    }
                }

                this.PrepareMask();

                _frameDiffAlgorithm.FirstFrameCompleted(this, this._frameMetrics);
            }

            if (_motionConfig.TestFrameInterval != TimeSpan.Zero)
            {
                _testFrameAge.Restart();
            }
        }

        /// <inheritdoc />
        public virtual void ResetAnalyser()
        {
            this.FullFrame = false;
            _fullTestFrame = false;
            this.WorkingData = new List<byte>();
            this.TestFrame = null;
            this.CurrentFrame = null;

            _testFrameAge.Reset();

            _frameDiffAlgorithm.ResetAnalyser(this, this._frameMetrics);
        }

        /// <summary>
        /// Periodically replaces the test frame with the current frame, which helps when a scene
        /// changes over time (such as changing shadows throughout the day).
        /// </summary>
        protected void TryUpdateTestFrame()
        {
            // Exit if the update interval has not elapsed, or if there was recent motion
            if (_motionConfig.TestFrameInterval == TimeSpan.Zero 
                || _testFrameAge.Elapsed < _motionConfig.TestFrameInterval
                || (TestFrameRefreshCooldown != TimeSpan.Zero 
                && _lastMotionAge.Elapsed < TestFrameRefreshCooldown))
            {
                return;
            }

            MMALLog.Logger.LogDebug($"Updating test frame after {_testFrameAge.ElapsedMilliseconds} ms");
            this.PrepareTestFrame();
            
            // Toggle so motion analysis can show an indicator
            _frameMetrics.Analysis_TestFrameUpdated = !_frameMetrics.Analysis_TestFrameUpdated;
        }

        private int GetBpp()
        {
            PixelFormat format = default;

            // RGB16 doesn't appear to be supported by GDI?
            if (_imageContext.PixelFormat == MMALEncoding.RGB24)
            {
                return 24;
            }

            if (_imageContext.PixelFormat == MMALEncoding.RGB32 || _imageContext.PixelFormat == MMALEncoding.RGBA)
            {
                return 32;
            }

            if (format == default)
            {
                throw new Exception($"Unsupported pixel format: {_imageContext.PixelFormat}");
            }

            return 0;
        }

        private void PrepareMask()
        {
            if (string.IsNullOrWhiteSpace(_motionConfig.MotionMaskPathname))
            {
                return;
            }

            using (var fs = new FileStream(_motionConfig.MotionMaskPathname, FileMode.Open, FileAccess.Read))
            using (var mask = new Bitmap(fs))
            {
                // Verify it matches our frame dimensions
                var maskBpp = Image.GetPixelFormatSize(mask.PixelFormat) / 8;
                if (mask.Width != _frameMetrics.FrameWidth || mask.Height != _frameMetrics.FrameHeight || maskBpp != _frameMetrics.FrameBpp)
                {
                    throw new Exception("Motion-detection mask must match raw stream width, height, and format (bits per pixel)");
                }

                // Store the byte array
                BitmapData bmpData = null;
                try
                {
                    bmpData = mask.LockBits(new Rectangle(0, 0, mask.Width, mask.Height), ImageLockMode.ReadOnly, mask.PixelFormat);
                    var pNative = bmpData.Scan0;
                    int size = bmpData.Stride * mask.Height;
                    FrameMask = new byte[size];
                    Marshal.Copy(pNative, FrameMask, 0, size);
                }
                finally
                {
                    mask.UnlockBits(bmpData);
                }
            }
        }
    }
}
