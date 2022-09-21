// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Runtime;
using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.Diagnostics.DebugServices.Implementation
{
    /// <summary>
    /// ClrMD runtime service implementation
    /// </summary>
    public class RuntimeService : IRuntimeService
    {
        private readonly ITarget _target;
        private readonly IDisposable _onFlushEvent;
        private DataTarget _dataTarget;
        private List<Runtime> _runtimes;
        private IContextService _contextService;

        public RuntimeService(ITarget target)
        {
            _target = target;
            _onFlushEvent = target.OnFlushEvent.Register(() => {
                if (_runtimes is not null && _runtimes.Count == 0)
                {
                    // If there are no runtimes, try find them again when the target stops
                    _runtimes = null;
                    _dataTarget?.Dispose();
                    _dataTarget = null;
                }
            });
            // Can't make RuntimeService IDisposable directly because _dataTarget.Dispose() disposes the IDataReader 
            // passed which is this RuntimeService instance which would call _dataTarget.Dispose again and causing a 
            // stack overflow.
            target.OnDestroyEvent.Register(() => {
                _dataTarget?.Dispose();
                _dataTarget = null;
                _onFlushEvent.Dispose();
            });
        }

        #region IRuntimeService

        /// <summary>
        /// Returns the list of runtimes in the target
        /// </summary>
        public IEnumerable<IRuntime> EnumerateRuntimes()
        {
            if (_runtimes is null)
            {
                _runtimes = new List<Runtime>();
                if (_dataTarget is null)
                {
                    _dataTarget = new DataTarget(new CustomDataTarget(_target.Services.GetService<DataReader>())) {
                        FileLocator = null
                    };
                }
                if (_dataTarget is not null)
                {
                    for (int i = 0; i < _dataTarget.ClrVersions.Length; i++)
                    {
                        _runtimes.Add(new Runtime(_target, i, _dataTarget.ClrVersions[i]));
                    }
                }
            }
            return _runtimes;
        }

        #endregion

        private IRuntime CurrentRuntime => ContextService.Services.GetService<IRuntime>();

        private IContextService ContextService => _contextService ??= _target.Services.GetService<IContextService>();

        public override string ToString()
        {
            var sb = new StringBuilder();
            if (_runtimes is not null)
            {
                foreach (IRuntime runtime in _runtimes)
                {
                    string current = _runtimes.Count > 1 ? runtime == CurrentRuntime ? "*" : " " : "";
                    sb.Append(current);
                    sb.AppendLine(runtime.ToString());
                }
            }
            return sb.ToString();
        }
    }
}
