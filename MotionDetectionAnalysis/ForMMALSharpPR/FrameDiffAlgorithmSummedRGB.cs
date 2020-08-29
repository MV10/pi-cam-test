// <copyright file="FrameDiffAlgorithmSummedRGB.cs" company="Techyian">
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
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MMALSharp.Common;
using MMALSharp.Common.Utility;

namespace MMALSharp.Processors.Motion
{
    public class FrameDiffAlgorithmSummedRGB : IFrameDiffAlgorithm
    {
        private Action _onDetect;

        public FrameDiffAlgorithmSummedRGB(Action onDetect)
        {
            _onDetect = onDetect;
        }

        public virtual void FirstFrameCompleted(FrameDiffBuffer buffer, FrameDiffMetrics metrics)
        { }

        public virtual bool AnalyseFrames(FrameDiffBuffer buffer, FrameDiffMetrics metrics)
        {
            var result = 
                Parallel.ForEach(buffer.CellDiff, (cell, loopState, loopIndex) 
                => CheckDiff(loopIndex, buffer, metrics, loopState));

            int diff = 0;

            // How Parallel Stop works: https://docs.microsoft.com/en-us/previous-versions/msp-n-p/ff963552(v=pandp.10)#parallel-stop
            if (!result.IsCompleted && !result.LowestBreakIteration.HasValue)
            {
                diff = int.MaxValue; // loop was stopped, so return a large diff
            }
            else
            {
                foreach (var cellDiff in buffer.CellDiff)
                {
                    diff += cellDiff;
                }
            }

            if (diff >= metrics.Threshold)
            {
                MMALLog.Logger.LogInformation($"Motion detected! Frame difference {diff}.");
                _onDetect?.Invoke();
                return true;
            }

            return false;
        }

        public virtual void ResetAnalyser(FrameDiffBuffer buffer, FrameDiffMetrics metrics)
        { } // this algorithm is stateless

        // FrameDiffMetrics is a structure; it is a by-value copy and all fields are value types which makes it thread safe
        protected virtual void CheckDiff(long cellIndex, FrameDiffBuffer buffer, FrameDiffMetrics metrics, ParallelLoopState loopState)
        {
            int diff = 0;
            var rect = buffer.CellRect[cellIndex];

            for (int col = rect.X; col < rect.X + rect.Width; col++)
            {
                for (int row = rect.Y; row < rect.Y + rect.Height; row++)
                {
                    var index = (col * metrics.FrameBpp) + (row * metrics.FrameStride);

                    if (buffer.FrameMask != null)
                    {
                        var rgbMask = buffer.FrameMask[index] + buffer.FrameMask[index + 1] + buffer.FrameMask[index + 2];

                        if (rgbMask == 0)
                        {
                            continue;
                        }
                    }

                    var rgb1 = buffer.TestFrame[index] + buffer.TestFrame[index + 1] + buffer.TestFrame[index + 2];
                    var rgb2 = buffer.CurrentFrame[index] + buffer.CurrentFrame[index + 1] + buffer.CurrentFrame[index + 2];

                    if (rgb2 - rgb1 > metrics.Threshold)
                    {
                        diff++;
                    }

                    // If the threshold has been exceeded, exit immediately and preempt any CheckDiff calls not yet started.
                    if (diff > metrics.Threshold)
                    {
                        buffer.CellDiff[cellIndex] = diff;
                        loopState.Stop();
                        return;
                    }
                }
            }

            buffer.CellDiff[cellIndex] = diff;
        }
    }
}
