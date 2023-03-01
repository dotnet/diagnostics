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
        protected readonly IHost _host;
        protected readonly ServiceContainer _serviceContainer;
        private ITarget _currentTarget;
        private IThread _currentThread;
        private IRuntime _currentRuntime;

        public ContextService(IHost host)
        {
            _host = host;
            var parent = new ContextServiceProvider(this);
            _serviceContainer = host.Services.GetService<IServiceManager>().CreateServiceContainer(ServiceScope.Context, parent);

            // Clear the current context when a target is flushed or destroyed
            host.OnTargetCreate.Register((target) => {
                target.OnFlushEvent.Register(() => ClearCurrentTarget(target));
                target.OnDestroyEvent.Register(() => ClearCurrentTarget(target));
            });
        }

        #region IContextService

        /// <summary>
        /// Current context service provider. Contains the current ITarget, IThread
        /// and IRuntime instances along with all per target and global services.
        /// </summary>
        public IServiceProvider Services => _serviceContainer;

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
            ITarget target = _host.EnumerateTargets().SingleOrDefault((target) => target.Id == targetId);
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
            IRuntime runtime = RuntimeService?.EnumerateRuntimes().SingleOrDefault((runtime) => runtime.Id == runtimeId);
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
        protected virtual ITarget GetCurrentTarget() => _currentTarget ??= _host.EnumerateTargets().FirstOrDefault();

        /// <summary>
        /// Clears the context service state if the target is current
        /// </summary>
        /// <param name="target"></param>
        private void ClearCurrentTarget(ITarget target)
        {
            if (IsTargetEqual(target, _currentTarget))
            {
                SetCurrentTarget(null);
            }
        }

        /// <summary>
        /// Allows hosts to set the current target. Fires the context change event if the current target has changed.
        /// </summary>
        /// <param name="target"></param>
        public virtual void SetCurrentTarget(ITarget target)
        {
            if (!IsTargetEqual(target, _currentTarget))
            {
                _currentTarget = target;
                _currentThread = null;
                _currentRuntime = null;
                _serviceContainer.DisposeServices();
                OnContextChange.Fire();
            }
        }

        /// <summary>
        /// Returns the current thread.
        /// </summary>
        protected virtual IThread GetCurrentThread() => _currentThread ??= ThreadService?.EnumerateThreads().FirstOrDefault();

        /// <summary>
        /// Allows hosts to set the current thread. Fires the context change event if the current thread has changed.
        /// </summary>
        /// <param name="thread"></param>
        public virtual void SetCurrentThread(IThread thread)
        {
            if (!IsThreadEqual(thread, _currentThread))
            {
                _currentThread = thread;
                _serviceContainer.DisposeServices();
                OnContextChange.Fire();
            }
        }

        /// <summary>
        /// Find the current runtime.
        /// </summary>
        protected virtual IRuntime GetCurrentRuntime()
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
                    _currentRuntime ??= runtimes.FirstOrDefault();
                }
            }
            return _currentRuntime;
        }

        /// <summary>
        /// Allows hosts to set the current runtime. Fires the context change event if the current thread has changed.
        /// </summary>
        public virtual void SetCurrentRuntime(IRuntime runtime)
        {
            if (!IsRuntimeEqual(runtime, _currentRuntime))
            {
                _currentRuntime = runtime;
                _serviceContainer.DisposeServices();
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

        /// <summary>
        /// Special context service parent forwarding wrapper
        /// </summary>
        private sealed class ContextServiceProvider : IServiceProvider
        {
            private readonly ContextService _contextService;

            /// <summary>
            /// Create a special context service provider parent that forwards to the current runtime, target or host
            /// </summary>
            public ContextServiceProvider(ContextService contextService)
            {
                _contextService = contextService;
            }

            /// <summary>
            /// Returns the instance of the service or returns null if service doesn't exist
            /// </summary>
            /// <param name="type">service type</param>
            /// <returns>service instance or null</returns>
            public object GetService(Type type)
            {
                if (type == typeof(IRuntime))
                {
                    return _contextService.GetCurrentRuntime();
                }
                else if (type == typeof(IThread))
                {
                    return _contextService.GetCurrentThread();
                }
                else if (type == typeof(ITarget))
                {
                    return _contextService.GetCurrentTarget();
                }
                // Check the current runtime (if exists) for the service.
                IRuntime currentRuntime = _contextService.GetCurrentRuntime();
                if (currentRuntime is not null)
                {
                    // This will chain to the target then the global services if not found in the current runtime
                    object service = currentRuntime.Services.GetService(type);
                    if (service is not null)
                    {
                        return service;
                    }
                }
                // Check the current thread (if exists) for the service.
                IThread currentThread = _contextService.GetCurrentThread();
                if (currentThread is not null)
                {
                    // This will chain to the target then the global services if not found in the current thread
                    object service = currentThread.Services.GetService(type);
                    if (service is not null)
                    {
                        return service;
                    }
                }
                // Check the current target (if exists) for the service.
                ITarget currentTarget = _contextService.GetCurrentTarget();
                if (currentTarget is not null)
                {
                    // This will chain to the global services if not found in the current target
                    object service = currentTarget.Services.GetService(type);
                    if (service is not null)
                    {
                        return service;
                    }
                }
                // Check with the global host services.
                return _contextService._host.Services.GetService(type);
            }
        }
    }
}
