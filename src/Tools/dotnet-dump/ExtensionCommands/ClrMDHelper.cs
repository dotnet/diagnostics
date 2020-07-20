﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Runtime;
using System;
using System.Collections.Generic;
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


#if ClrMD2
    public IEnumerable<TimerInfo> EnumerateTimers()
    {
        // the implementation is different between .NET Framework/.NET Core 2.0 and .NET Core 2.1+
        // - the former is relying on a single static TimerQueue.s_queue 
        // - the latter uses an array of TimerQueue (static TimerQueue.Instances field)
        // each queue refers to TimerQueueTimer linked list via its m_timers field
        //
        var timerQueueType = GetMscorlib().GetTypeByName("System.Threading.TimerQueue");
        if (timerQueueType == null)
            yield break;

        // .NET Core 2.1+ case
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

                // m_timers is the start of the linked list of TimerQueueTimer
                var currentTimerQueueTimer = obj.ReadObjectField("m_timers");
                
                // iterate on each TimerQueueTimer in the linked list
                while (currentTimerQueueTimer.Address != 0)
                {
                    var ti = GetTimerInfo(currentTimerQueueTimer);

                    if (ti == null)
                        continue;

                    yield return ti;

                    currentTimerQueueTimer = currentTimerQueueTimer.ReadObjectField("m_next");
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
                
                while (currentTimerQueueTimer.IsValid)
                {
                    var ti = GetTimerInfo(currentTimerQueueTimer);
                    if (ti == null)
                        continue;

                    yield return ti;

                    currentTimerQueueTimer = currentTimerQueueTimer.ReadObjectField("m_next");
                }
            }
        }
    }

    private TimerInfo GetTimerInfo(ClrObject currentTimerQueueTimer)
    {
        var ti = new TimerInfo()
        {
            TimerQueueTimerAddress = currentTimerQueueTimer.Address
        };

        ti.DueTime = currentTimerQueueTimer.ReadField<uint>("m_dueTime");
        ti.Period = currentTimerQueueTimer.ReadField<uint>("m_period");
        ti.Cancelled = currentTimerQueueTimer.ReadField<bool>("m_canceled");

        var state = currentTimerQueueTimer.ReadObjectField("m_state");
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
        var timerCallback = currentTimerQueueTimer.ReadObjectField("m_timerCallback");
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
            // NOTE: can't find a replacement for ClrMD 1.1 GetMethodByAddress
            // GetMethodByInstructionPointer always returns null
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

#else
        public IEnumerable<TimerInfo> EnumerateTimers()
        {
            // the implementation is different between .NET Framework/.NET Core 2.0 and .NET Core 2.1+
            // - the former is relying on a single static TimerQueue.s_queue 
            // - the latter uses an array of TimerQueue (static TimerQueue.Instances field)
            // each queue refers to TimerQueueTimer linked list via its m_timers field
            //
            var timerQueueType = GetMscorlib().GetTypeByName("System.Threading.TimerQueue");
            if (timerQueueType == null)
                yield break;

            // .NET Core 2.1+ case
            ClrStaticField instancesField = timerQueueType.GetStaticFieldByName("<Instances>k__BackingField");
            if (instancesField != null)
            {
                // until the ClrMD bug to get static field value is fixed, iterate on each object of the heap
                // to find each TimerQueue and iterate on (slower but it works)
                foreach (var address in _heap.EnumerateObjectAddresses())
                {
                    var objType = _heap.GetObjectType(address);
                    if (objType == null) continue;
                    if (objType == _heap.Free) continue;
                    if (string.Compare(objType.Name, "System.Threading.TimerQueue", StringComparison.Ordinal) != 0)
                        continue;

                    // m_timers is the start of the linked list of TimerQueueTimer
                    var currentTimerQueueTimer = GetFieldValue(address, "m_timers");

                    // iterate on each TimerQueueTimer in the linked list
                    while ((currentTimerQueueTimer != null) && (((ulong)currentTimerQueueTimer) != 0))
                    {
                        ulong currentTimerQueueTimerAddress = (ulong)currentTimerQueueTimer;
                        var ti = GetTimerInfo(currentTimerQueueTimerAddress);

                        if (ti == null)
                            continue;

                        yield return ti;

                        currentTimerQueueTimer = GetFieldValue(currentTimerQueueTimerAddress, "m_next");
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
                    ulong? timerQueue = (ulong?)instanceField.GetValue(domain);
                    if (!timerQueue.HasValue || timerQueue.Value == 0)
                        continue;

                    var t = _heap.GetObjectType(timerQueue.Value);
                    if (t == null)
                        continue;

                    // m_timers is the start of the linked list of TimerQueueTimer
                    var currentTimerQueueTimer = GetFieldValue(timerQueue.Value, "m_timers");

                    while ((currentTimerQueueTimer != null) && (((ulong)currentTimerQueueTimer) != 0))
                    {
                        ulong currentTimerQueueTimerAddress = (ulong)currentTimerQueueTimer;

                        var ti = GetTimerInfo(currentTimerQueueTimerAddress);
                        if (ti == null)
                            continue;

                        yield return ti;

                        currentTimerQueueTimer = GetFieldValue(currentTimerQueueTimerAddress, "m_next");
                    }
                }
            }
        }

        private TimerInfo GetTimerInfo(ulong currentTimerQueueTimerRef)
        {
            var ti = new TimerInfo()
            {
                TimerQueueTimerAddress = currentTimerQueueTimerRef
            };

            var val = GetFieldValue(currentTimerQueueTimerRef, "m_dueTime");
            ti.DueTime = (uint)val;
            val = GetFieldValue(currentTimerQueueTimerRef, "m_period");
            ti.Period = (uint)val;
            val = GetFieldValue(currentTimerQueueTimerRef, "m_canceled");
            ti.Cancelled = (bool)val;
            val = GetFieldValue(currentTimerQueueTimerRef, "m_state");
            ti.StateTypeName = "";
            if (val != null)
            {
                ti.StateAddress = (ulong)val;
                var stateType = _heap.GetObjectType(ti.StateAddress);
                if (stateType != null)
                {
                    ti.StateTypeName = stateType.Name;
                }
            }
            else
            {
                ti.StateAddress = 0;
            }

            // decipher the callback details
            val = GetFieldValue(currentTimerQueueTimerRef, "m_timerCallback");
            if (val != null)
            {
                ulong elementAddress = (ulong)val;
                if (elementAddress == 0)
                    return null;

                var elementType = _heap.GetObjectType(elementAddress);
                if (elementType != null)
                {
                    if (elementType.Name == "System.Threading.TimerCallback")
                    {
                        ti.MethodName = BuildTimerCallbackMethodName(elementAddress);
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

        private string BuildTimerCallbackMethodName(ulong timerCallbackRef)
        {
            var methodPtr = GetFieldValue(timerCallbackRef, "_methodPtr");
            if (methodPtr != null)
            {
                var method = _clr.GetMethodByAddress((ulong)(long)methodPtr);
                if (method != null)
                {
                    // look for "this" to figure out the real callback implementor type
                    var thisTypeName = "?";
                    var thisPtr = GetFieldValue(timerCallbackRef, "_target");
                    if ((thisPtr != null) && ((ulong)thisPtr) != 0)
                    {
                        ulong thisRef = (ulong)thisPtr;
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

#endif

        static ClrModule CoreLibModule;
        public ClrModule GetMscorlib()
        {
#if ClrMD2
            var bclModule = _clr.BaseClassLibrary;
            return bclModule;
#else
            if (CoreLibModule != null) return CoreLibModule;

            foreach (var module in _clr.Modules)
            {
                if (string.IsNullOrEmpty(module.AssemblyName))
                        continue;

                var name = module.AssemblyName.ToLower();

                // in .NET Framework
                if (name.Contains("mscorlib", StringComparison.Ordinal))
                {
                    CoreLibModule = module;
                    break;
                }

                // in .NET Core
                if (name.Contains("corelib", StringComparison.Ordinal))
                {
                    CoreLibModule = module;
                    break;
                }
            }

            return CoreLibModule;
#endif
        }

#if ClrMD2
#else
        private object GetFieldValue(ulong address, string fieldName)
    {
        var type = _heap.GetObjectType(address);
        if (type == null) return null;

        var field = type.GetFieldByName(fieldName);
        if (field == null) return null;

        return field.GetValue(address);
    }
#endif
    }
}
