// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Monitoring.Contracts
{
    /// <summary>
    /// A pipeline controls data which is flowing from some source to sink asynchronously.
    /// This interface is allows the flow to be started and stopped. The concrete class
    /// determines what data is being collected and where it will flow to.
    /// 
    /// The pipeline is logically in one of these states:
    /// Unstarted - After the object is constructed and prior to calling RunAsync or
    /// StopAsync. No data is flowing.
    /// Running - The pipeline is doing whatever asynchronous work is necessary to flow
    /// data. Unstarted transitions to Running with a call to RunAsync()
    /// Stopping - The pipeline is doing a graceful shutdown to stop receiving any new
    /// data and drain any in-flight data to the sink. Unstarted or Running transitions to
    /// Stopping with a call to StopAsync(). Pipelines may also automatically enter a Stopping
    /// state when there is no data left to receive from the source.
    /// Stopped - All asynchronous work has ceased and the pipeline can not be restarted. This
    /// transition happens asynchronously from the stopping state when there is no
    /// work left to be done. The only way to be certain you have reached this state is to
    /// observe that the Task returned by StopAsync() or RunAsync() is completed or cancelled,
    /// usually by awaiting it.
    /// </summary>
    public interface IPipeline : IAsyncDisposable
    {
        /// <summary>
        /// Causes an unstarted pipeline to start running, which makes data flow from source
        /// to sink. Calling this more than once doesn't have any additional effect and returns
        /// the same Task. Once the pipeline transitions to the Stopped state the returned Task
        /// will be complete or cancelled.
        /// </summary>
        /// <param name="token">If this token is cancelled, it signals the pipeline to abandon all data transfer
        /// operations as quickly as possible.
        /// </param>
        /// <exception cref="PipelineException">For any error that prevents all the requested
        /// data from being moved through the pipeline</exception>
        /// <remarks>Any exception other than PipelineException represents either
        /// a bug in the pipeline implementation because it was unanticipated or a failure in
        /// lower level runtime/OS/hardware to keep the process in a consistent state</remarks>
        Task RunAsync(CancellationToken token);

        /// <summary>
        /// Causes an unstarted or running pipeline to transition to the stopping state. In this
        /// state data flow from the source will be stopped and any in-flight data is gracefully
        /// drained. Calling this more than once doesn't have any additional effect and returns
        /// the same Task. Once the pipeline transitions to the Stopped state the returned Task
        /// will be complete or cancelled.
        /// </summary>
        /// <param name="cancelToken">If this token is cancelled it has the same effect as
        /// calling Abort()</param>
        /// <exception cref="PipelineException">For any error that prevents all the requested
        /// data from being moved through the pipeline</exception>
        /// <remarks>Any exception other than PipelineException represents either
        /// a bug in the pipeline implementation because it was unanticipated or a failure in
        /// lower level runtime/OS/hardware to keep the process in a consistent state</remarks>
        Task StopAsync(CancellationToken cancelToken = default);

    }

    public class PipelineException : Exception
    {
        public PipelineException(string message) : base(message) { }
        public PipelineException(string message, Exception inner) : base(message, inner) { }
    }

    public class PipelineAbortedException : PipelineException
    {
        public PipelineAbortedException() : base("Pipeline aborted") { }
    }
}
