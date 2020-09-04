// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Runtime;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Diagnostics.DebugServices;

namespace Microsoft.Diagnostic.Tools.Dump.ExtensionCommands
{
    public class ClrMDHelper
    {
        private readonly ClrRuntime _clr;
        private readonly ClrHeap _heap;

        public ClrMDHelper(ServiceProvider provider)
        {
            _clr = provider.GetService<ClrRuntime>();
            _heap = _clr.Heap;
        }

        public ulong GetTaskStateFromAddress(ulong address)
        {
            const string stateFieldName = "m_stateFlags";

            var type = _heap.GetObjectType(address);
            if ((type != null) && (type.Name.StartsWith("System.Threading.Tasks.Task")))
            {
                // could be other Task-prefixed types in the same namespace such as TaskCompletionSource
                if (type.GetFieldByName(stateFieldName) == null)
                    return 0;

                return (ulong)_heap.GetObject(address).ReadField<int>(stateFieldName);
            }

            return 0;
        }

        public static string GetTaskState(ulong flag)
        {
            TaskStatus tks;

            if ((flag & TASK_STATE_FAULTED) != 0) tks = TaskStatus.Faulted;
            else if ((flag & TASK_STATE_CANCELED) != 0) tks = TaskStatus.Canceled;
            else if ((flag & TASK_STATE_RAN_TO_COMPLETION) != 0) tks = TaskStatus.RanToCompletion;
            else if ((flag & TASK_STATE_WAITING_ON_CHILDREN) != 0) tks = TaskStatus.WaitingForChildrenToComplete;
            else if ((flag & TASK_STATE_DELEGATE_INVOKED) != 0) tks = TaskStatus.Running;
            else if ((flag & TASK_STATE_STARTED) != 0) tks = TaskStatus.WaitingToRun;
            else if ((flag & TASK_STATE_WAITINGFORACTIVATION) != 0) tks = TaskStatus.WaitingForActivation;
            else if (flag == 0) tks = TaskStatus.Created;
            else return null;

            return tks.ToString();
        }

        // from CLR implementation in https://github.com/dotnet/runtime/blob/master/src/libraries/System.Private.CoreLib/src/System/Threading/Tasks/Task.cs#L141
        internal const int TASK_STATE_STARTED                      = 0x00010000;
        internal const int TASK_STATE_DELEGATE_INVOKED             = 0x00020000;
        internal const int TASK_STATE_DISPOSED                     = 0x00040000;
        internal const int TASK_STATE_EXCEPTIONOBSERVEDBYPARENT    = 0x00080000;
        internal const int TASK_STATE_CANCELLATIONACKNOWLEDGED     = 0x00100000;
        internal const int TASK_STATE_FAULTED                      = 0x00200000;
        internal const int TASK_STATE_CANCELED                     = 0x00400000;
        internal const int TASK_STATE_WAITING_ON_CHILDREN          = 0x00800000;
        internal const int TASK_STATE_RAN_TO_COMPLETION            = 0x01000000;
        internal const int TASK_STATE_WAITINGFORACTIVATION         = 0x02000000;
        internal const int TASK_STATE_COMPLETION_RESERVED          = 0x04000000;
        internal const int TASK_STATE_WAIT_COMPLETION_NOTIFICATION = 0x10000000;
        internal const int TASK_STATE_EXECUTIONCONTEXT_IS_NULL     = 0x20000000;
        internal const int TASK_STATE_TASKSCHEDULED_WAS_FIRED      = 0x40000000;

        public IEnumerable<TimerInfo> EnumerateTimers()
        {
            // the implementation is different between .NET Framework/.NET Core 2.*, and .NET Core 3.0+
            // - the former is relying on a single static TimerQueue.s_queue 
            // - the latter uses an array of TimerQueue (static TimerQueue.Instances field)
            // each queue refers to TimerQueueTimer linked list via its m_timers or _shortTimers/_longTimers fields
            var timerQueueType = GetMscorlib().GetTypeByName("System.Threading.TimerQueue");
            if (timerQueueType == null)
                yield break;

            // .NET Core case
            ClrStaticField instancesField = timerQueueType.GetStaticFieldByName("<Instances>k__BackingField");
            if (instancesField != null)
            {
                // until the ClrMD bug to get static field value is fixed, iterate on each object of the heap
                // to find each TimerQueue and iterate on (slower but it works)
                foreach (var obj in _heap.EnumerateObjects())
                {
                    var objType = obj.Type;
                    if (objType == null) continue;
                    if (objType == _heap.FreeType) continue;
                    if (string.Compare(objType.Name, "System.Threading.TimerQueue", StringComparison.Ordinal) != 0)
                        continue;

                    // m_timers is the start of the linked list of TimerQueueTimer in pre 3.0
                    var timersField = objType.GetFieldByName("m_timers");
                    if (timersField != null)
                    {
                        var currentTimerQueueTimer = obj.ReadObjectField("m_timers");
                        foreach (var timer in GetTimers(currentTimerQueueTimer, false))
                        {
                            yield return timer;
                        }
                    }
                    else
                    {
                        // get short timers
                        timersField = objType.GetFieldByName("_shortTimers");
                        if (timersField == null)
                            throw new InvalidOperationException("Missing _shortTimers field. Check the .NET Core version implementation of TimerQueue.");

                        var currentTimerQueueTimer = obj.ReadObjectField("_shortTimers");
                        foreach (var timer in GetTimers(currentTimerQueueTimer, true))
                        {
                            timer.IsShort = true;
                            yield return timer;
                        }

                        // get long timers
                        currentTimerQueueTimer = obj.ReadObjectField("_longTimers");
                        foreach (var timer in GetTimers(currentTimerQueueTimer, true))
                        {
                            timer.IsShort = false;
                            yield return timer;
                        }
                    }
                }
            }
            else
            {
                // .NET Framework implementation
                var instanceField = timerQueueType.GetStaticFieldByName("s_queue");
                if (instanceField == null)
                    yield break;

                foreach (var domain in _clr.AppDomains)
                {
                    var timerQueue = instanceField.ReadObject(domain);
                    if ((timerQueue.IsNull) || (!timerQueue.IsValid))
                        continue;

                    // m_timers is the start of the list of TimerQueueTimer
                    var currentTimerQueueTimer = timerQueue.ReadObjectField("m_timers");
                    foreach (var timer in GetTimers(currentTimerQueueTimer, false))
                    {
                        yield return timer;
                    }
                }
            }
        }

        private IEnumerable<TimerInfo> GetTimers(ClrObject timerQueueTimer, bool is30Format)
        {
            while (timerQueueTimer.Address != 0)
            {
                var ti = GetTimerInfo(timerQueueTimer);
                if (ti == null)
                    continue;

                yield return ti;

                timerQueueTimer = is30Format ?
                    timerQueueTimer.ReadObjectField("_next") :
                    timerQueueTimer.ReadObjectField("m_next");
            }
        }

        private TimerInfo GetTimerInfo(ClrObject currentTimerQueueTimer)
        {
            var ti = new TimerInfo()
            {
                TimerQueueTimerAddress = currentTimerQueueTimer.Address
            };

            // field names prefix changes from "m_" to "_" in .NET Core 3.0
            var is30Format = currentTimerQueueTimer.Type.GetFieldByName("_dueTime") != null;
            ClrObject state;
            if (is30Format)
            {
                ti.DueTime = currentTimerQueueTimer.ReadField<uint>("_dueTime");
                ti.Period = currentTimerQueueTimer.ReadField<uint>("_period");
                ti.Cancelled = currentTimerQueueTimer.ReadField<bool>("_canceled");
                state = currentTimerQueueTimer.ReadObjectField("_state");
            }
            else
            {
                ti.DueTime = currentTimerQueueTimer.ReadField<uint>("m_dueTime");
                ti.Period = currentTimerQueueTimer.ReadField<uint>("m_period");
                ti.Cancelled = currentTimerQueueTimer.ReadField<bool>("m_canceled");
                state = currentTimerQueueTimer.ReadObjectField("m_state");
            }

            ti.StateAddress = 0;
            if (state.IsValid)
            {
                ti.StateAddress = state.Address;
                var stateType = _heap.GetObjectType(ti.StateAddress);
                if (stateType != null)
                {
                    ti.StateTypeName = stateType.Name;
                }
            }

            // decipher the callback details
            var timerCallback = is30Format ?
                currentTimerQueueTimer.ReadObjectField("_timerCallback") :
                currentTimerQueueTimer.ReadObjectField("m_timerCallback");
            if (timerCallback.IsValid)
            {
                var elementType = timerCallback.Type;
                if (elementType != null)
                {
                    if (elementType.Name == "System.Threading.TimerCallback")
                    {
                        ti.MethodName = BuildTimerCallbackMethodName(timerCallback);
                    }
                    else
                    {
                        ti.MethodName = "<" + elementType.Name + ">";
                    }
                }
                else
                {
                    ti.MethodName = "{no callback type?}";
                }
            }
            else
            {
                ti.MethodName = "???";
            }


            return ti;
        }

        private string BuildTimerCallbackMethodName(ClrObject timerCallback)
        {
            var methodPtr = timerCallback.ReadField<ulong>("_methodPtr");
            if (methodPtr != 0)
            {
                var method = _clr.GetMethodByInstructionPointer(methodPtr);
                if (method != null)
                {
                    // look for "this" to figure out the real callback implementor type
                    var thisTypeName = "?";
                    var thisPtr = timerCallback.ReadObjectField("_target");
                    if (thisPtr.IsValid)
                    {
                        var thisRef = thisPtr.Address;
                        var thisType = _heap.GetObjectType(thisRef);
                        if (thisType != null)
                        {
                            thisTypeName = thisType.Name;
                        }
                    }
                    else
                    {
                        thisTypeName = (method.Type != null) ? method.Type.Name : "?";
                    }
                    return $"{thisTypeName}.{method.Name}";
                }
                else
                {
                    return "";
                }
            }
            else
            {
                return "";
            }
        }

        public ClrModule GetMscorlib()
        {
            var bclModule = _clr.BaseClassLibrary;
            return bclModule;
        }
    }
}
