// <copyright file="IFrameDiffAlgorithm.cs" company="Techyian">
// Copyright (c) Ian Auty and contributors. All rights reserved.
// Licensed under the MIT License. Please see LICENSE.txt for License info.
// </copyright>

namespace MMALSharp.Processors.Motion
{
    /// <summary>
    /// Represents a frame-difference-based motion detection algorithm.
    /// </summary>
    public interface IFrameDiffAlgorithm
    {
        /// <summary>
        /// Invoked after the buffer's <see cref="FrameDiffBuffer.TestFrame"/> is available
        /// for the first time and frame metrics have been collected. Allows the algorithm
        /// to modify the test frame, prepare matching local buffers, etc.
        /// </summary>
        /// <param name="buffer">The <see cref="FrameDiffBuffer"/> invoking this method.</param>
        /// <param name="metrics">Motion configuration and properties of the frame data.</param>
        void FirstFrameCompleted(FrameDiffBuffer buffer, FrameDiffMetrics metrics);

        /// <summary>
        /// Invoked when <see cref="FrameDiffBuffer"/> has a full test frame and a
        /// new full comparison frame available.
        /// </summary>
        /// <param name="buffer">The <see cref="FrameDiffBuffer"/> invoking this method.</param>
        /// <param name="metrics">Motion configuration and properties of the frame data.</param>
        /// <returns>Indicates whether motion was detected.</returns>
        bool AnalyseFrames(FrameDiffBuffer buffer, FrameDiffMetrics metrics);

        /// <summary>
        /// Invoked when <see cref="FrameDiffBuffer"/> has been reset. The algorithm should also
        /// reset stateful data, if any.
        /// </summary>
        /// <param name="buffer">The <see cref="FrameDiffBuffer"/> invoking this method.</param>
        /// <param name="metrics">Motion configuration and properties of the frame data.</param>
        void ResetAnalyser(FrameDiffBuffer buffer, FrameDiffMetrics metrics);
    }
}
