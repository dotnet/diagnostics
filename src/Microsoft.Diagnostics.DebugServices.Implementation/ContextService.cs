// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.Diagnostics.DebugServices.Implementation
{
    /// <summary>
    /// Manages the current target, thread and runtime contexts
    /// </summary>
    public class ContextService : IContextService
    {
        protected readonly IHost Host;
        private ITarget _currentTarget;
        private IThread _currentThread;
        private IRuntime _currentRuntime;

        public readonly ServiceProvider ServiceProvider;

        public ContextService(IHost host)
        {
            Host = host;

            ServiceProvider = new ServiceProvider(new Func<IServiceProvider>[] {
                // First check the current runtime for the service
                () => GetCurrentRuntime()?.Services,
                // If there is no target, then provide just the global services
                () => GetCurrentTarget()?.Services ?? host.Services
            });

            // These services depend on no caching
            ServiceProvider.AddServiceFactoryWithNoCaching<ITarget>(GetCurrentTarget);
            ServiceProvider.AddServiceFactoryWithNoCaching<IThread>(GetCurrentThread);
            ServiceProvider.AddServiceFactoryWithNoCaching<IRuntime>(GetCurrentRuntime);
        }

        #region IContextService

        /// <summary>
        /// Current context service provider. Contains the current ITarget, IThread
        /// and IRuntime instances along with all per target and global services.
        /// </summary>
        public IServiceProvider Services => ServiceProvider;

        /// <summary>
        /// Fires anytime the current context changes.
        /// </summary>
        public IServiceEvent OnContextChange { get; } = new ServiceEvent();

        /// <summary>
        /// Sets the current target.
        /// </summary>
        /// <param name="targetId">target id</param>
        /// <exception cref="DiagnosticsException">invalid target id</exception>
        public void SetCurrentTarget(int targetId)
        {
            ITarget target = Host.EnumerateTargets().FirstOrDefault((target) => target.Id == targetId);
            if (target is null)
            {
                throw new DiagnosticsException($"Invalid target id {targetId}");
            }
            SetCurrentTarget(target);
        }

        /// <summary>
        /// Clears (nulls) the current target
        /// </summary>
        public void ClearCurrentTarget() => SetCurrentTarget(null);

        /// <summary>
        /// Set the current thread.
        /// </summary>
        /// <param name="threadId">thread id</param>
        /// <exception cref="DiagnosticsException">invalid thread id</exception>
        public void SetCurrentThread(uint threadId) => SetCurrentThread(ThreadService?.GetThreadFromId(threadId));

        /// <summary>
        /// Clears (nulls) the current thread
        /// </summary>
        public void ClearCurrentThread() => SetCurrentThread(null);

        /// <summary>
        /// Set the current runtime
        /// </summary>
        /// <param name="runtimeId">runtime id</param>
        /// <exception cref="DiagnosticsException">invalid runtime id</exception>
        public void SetCurrentRuntime(int runtimeId)
        {
            IRuntime runtime = RuntimeService?.EnumerateRuntimes().FirstOrDefault((runtime) => runtime.Id == runtimeId);
            if (runtime is null)
            {
                throw new DiagnosticsException($"Invalid runtime id {runtimeId}");
            }
            SetCurrentRuntime(runtime);
        }

        /// <summary>
        /// Clears (nulls) the current runtime
        /// </summary>
        public void ClearCurrentRuntime() => SetCurrentRuntime(null);

        #endregion

        /// <summary>
        /// Returns the current target.
        /// </summary>
        public ITarget GetCurrentTarget() => _currentTarget ??= Host.EnumerateTargets().FirstOrDefault();

        /// <summary>
        /// Allows hosts to set the initial current target
        /// </summary>
        /// <param name="target"></param>
        public void SetCurrentTarget(ITarget target)
        {
            if (!IsTargetEqual(target, _currentTarget))
            {
                _currentTarget = target;
                _currentThread = null;
                _currentRuntime = null;
                ServiceProvider.FlushServices();
                OnContextChange.Fire();
            }
        }

        /// <summary>
        /// Returns the current thread.
        /// </summary>
        public virtual IThread GetCurrentThread() => _currentThread ??= ThreadService?.EnumerateThreads().FirstOrDefault();

        /// <summary>
        /// Allows hosts to set the initial current thread
        /// </summary>
        /// <param name="thread"></param>
        public virtual void SetCurrentThread(IThread thread)
        {
            if (!IsThreadEqual(thread, _currentThread))
            {
                _currentThread = thread;
                ServiceProvider.FlushServices();
                OnContextChange.Fire();
            }
        }

        /// <summary>
        /// Find the current runtime.
        /// </summary>
        public IRuntime GetCurrentRuntime()
        {
            if (_currentRuntime is null)
            {
                IEnumerable<IRuntime> runtimes = RuntimeService?.EnumerateRuntimes();
                if (runtimes is not null)
                {
                    // First check if there is a .NET Core runtime loaded
                    foreach (IRuntime runtime in runtimes)
                    {
                        if (runtime.RuntimeType == RuntimeType.NetCore || runtime.RuntimeType == RuntimeType.SingleFile)
                        {
                            _currentRuntime = runtime;
                            break;
                        }
                    }
                    // If no .NET Core runtime, then check for desktop runtime
                    if (_currentRuntime is null)
                    {
                        foreach (IRuntime runtime in runtimes)
                        {
                            if (runtime.RuntimeType == RuntimeType.Desktop)
                            {
                                _currentRuntime = runtime;
                                break;
                            }
                        }
                    }
                    // If no core or desktop runtime, get the first one if any
                    if (_currentRuntime is null)
                    {
                        _currentRuntime = runtimes.FirstOrDefault();
                    }
                }
            }
            return _currentRuntime;
        }

        /// <summary>
        /// Allows hosts to set the initial current runtime
        /// </summary>
        public void SetCurrentRuntime(IRuntime runtime)
        {
            if (!IsRuntimeEqual(runtime, _currentRuntime))
            {
                _currentRuntime = runtime;
                ServiceProvider.FlushServices();
                OnContextChange.Fire();
            }
        }

        protected bool IsTargetEqual(ITarget left, ITarget right)
        {
            if (left is null || right is null)
            {
                return left == right;
            }
            return left == right;
        }

        protected bool IsThreadEqual(IThread left, IThread right)
        {
            if (left is null || right is null)
            {
                return left == right;
            }
            return left == right;
        }

        protected bool IsRuntimeEqual(IRuntime left, IRuntime right)
        {
            if (left is null || right is null)
            {
                return left == right;
            }
            return left == right;
        }

        protected IThreadService ThreadService => GetCurrentTarget()?.Services.GetService<IThreadService>();

        protected IRuntimeService RuntimeService => GetCurrentTarget()?.Services.GetService<IRuntimeService>();
    }
}
