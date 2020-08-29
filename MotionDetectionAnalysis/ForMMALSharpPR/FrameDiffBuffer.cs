// <copyright file="FrameDiffBuffer.cs" company="Techyian">
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
    public class FrameDiffBuffer : FrameAnalyser
    {
        // add to MotionConfig
        private TimeSpan TestFrameRefreshCooldown = TimeSpan.FromSeconds(3);


        // Prefer fields over properties for parallel processing performance reasons.
        // Parallel processing references unique array indices, so arrays do not need
        // to be stored in the passed-by-value FrameDiffMetrics struct.

        /// <summary>
        /// When true, <see cref="PrepareTestFrame"/> initializes frame metrics fields.
        /// </summary>
        private bool FirstFrame = true;

        /// <summary>
        /// Various properties of the frame collected when the first full frame is available.
        /// A copy of this struct is passed into the parallel processing algorithm. It is private
        /// rather than internal to prevent accidental parallel reads against this copy.
        /// </summary>
        private FrameDiffMetrics FrameMetrics;

        /// <summary>
        /// The motion configuration object.
        /// </summary>
        private MotionConfig MotionConfig;

        /// <summary>
        /// Indicates whether we have a full test frame.
        /// </summary>
        private bool FullTestFrame;

        /// <summary>
        /// The image metadata.
        /// </summary>
        private ImageContext ImageContext;

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
        /// Creates a new instance of <see cref="FrameDiffBuffer"/>.
        /// </summary>
        /// <param name="config">The motion configuration object.</param>
        /// <param name="onDetect">A callback when changes are detected.</param>
        public FrameDiffBuffer(MotionConfig config, IFrameDiffAlgorithm algorithm)
        {
            this.MotionConfig = config;

            FrameMetrics = new FrameDiffMetrics
            {
                Threshold = config.Threshold // copy object member to thread safe struct member
            };

            _frameDiffAlgorithm = algorithm;
            _testFrameAge = new Stopwatch();
            _lastMotionAge = new Stopwatch();
        }

        /// <inheritdoc />
        public override void Apply(ImageContext context)
        {
            this.ImageContext = context;

            base.Apply(context);

            if (context.Eos)
            {
                // if zero bytes buffered, EOS is the end of a physical input video filestream
                if (this.WorkingData.Count > 0)
                {
                    if (!this.FullTestFrame)
                    {
                        MMALLog.Logger.LogDebug("EOS reached for test frame.");

                        this.FullTestFrame = true;
                        this.PrepareTestFrame();
                    }
                    else
                    {
                        MMALLog.Logger.LogDebug("Have full frame, checking for changes.");

                        CurrentFrame = this.WorkingData.ToArray();
                        var detected = _frameDiffAlgorithm.AnalyseFrames(this, this.FrameMetrics);
                        
                        if(detected)
                        {
                            _lastMotionAge.Restart();
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

            if (FirstFrame)
            {
                FirstFrame = false;

                // one-time collection of basic frame dimensions
                FrameMetrics.FrameWidth = this.ImageContext.Resolution.Width;
                FrameMetrics.FrameHeight = this.ImageContext.Resolution.Height;
                FrameMetrics.FrameBpp = this.GetBpp() / 8;
                FrameMetrics.FrameStride = this.ImageContext.Stride;

                // one-time setup of the diff cell parameters and arrays
                int indices = (int)Math.Pow(CellDivisor, 2);
                int cellWidth = FrameMetrics.FrameWidth / CellDivisor;
                int cellHeight = FrameMetrics.FrameHeight / CellDivisor;
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

                _frameDiffAlgorithm.FirstFrameCompleted(this, this.FrameMetrics);
            }

            if (this.MotionConfig.TestFrameInterval != TimeSpan.Zero)
            {
                _testFrameAge.Restart();
            }
        }

        /// <inheritdoc />
        public virtual void ResetAnalyser()
        {
            this.FullFrame = false;
            this.FullTestFrame = false;
            this.WorkingData = new List<byte>();
            this.TestFrame = null;
            this.CurrentFrame = null;

            _testFrameAge.Reset();

            _frameDiffAlgorithm.ResetAnalyser(this, this.FrameMetrics);
        }

        /// <summary>
        /// Periodically replaces the test frame with the current frame, which helps when a scene
        /// changes over time (such as changing shadows throughout the day).
        /// </summary>
        protected void TryUpdateTestFrame()
        {
            // Exit if the update interval has not elapsed, or if there was recent motion
            if (this.MotionConfig.TestFrameInterval == TimeSpan.Zero 
                || _testFrameAge.Elapsed < this.MotionConfig.TestFrameInterval
                || (TestFrameRefreshCooldown != TimeSpan.Zero 
                && _lastMotionAge.Elapsed < TestFrameRefreshCooldown))
            {
                return;
            }

            MMALLog.Logger.LogDebug($"Updating test frame after {_testFrameAge.ElapsedMilliseconds} ms");
            this.PrepareTestFrame();
        }

        private int GetBpp()
        {
            PixelFormat format = default;

            // RGB16 doesn't appear to be supported by GDI?
            if (this.ImageContext.PixelFormat == MMALEncoding.RGB24)
            {
                return 24;
            }

            if (this.ImageContext.PixelFormat == MMALEncoding.RGB32 || this.ImageContext.PixelFormat == MMALEncoding.RGBA)
            {
                return 32;
            }

            if (format == default)
            {
                throw new Exception($"Unsupported pixel format: {this.ImageContext.PixelFormat}");
            }

            return 0;
        }

        private void PrepareMask()
        {
            if (string.IsNullOrWhiteSpace(this.MotionConfig.MotionMaskPathname))
            {
                return;
            }

            using (var fs = new FileStream(this.MotionConfig.MotionMaskPathname, FileMode.Open, FileAccess.Read))
            using (var mask = new Bitmap(fs))
            {
                // Verify it matches our frame dimensions
                var maskBpp = Image.GetPixelFormatSize(mask.PixelFormat) / 8;
                if (mask.Width != FrameMetrics.FrameWidth || mask.Height != FrameMetrics.FrameHeight || maskBpp != FrameMetrics.FrameBpp)
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
