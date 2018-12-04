// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading.Tasks;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Diagnostics.Runtime;

namespace Microsoft.Diagnostics.Tools.Analyze
{
    internal class AsyncHangAnalyzer
    {
        private const string AsyncStateMachineBoxTypeName = "System.Runtime.CompilerServices.AsyncTaskMethodBuilder+AsyncStateMachineBox<";
        private const string DebugFinalizableBoxTypeName = "System.Runtime.CompilerServices.AsyncTaskMethodBuilder+DebugFinalizableAsyncStateMachineBox<";

        internal const int TASK_STATE_STARTED = 0x10000;                                       //bin: 0000 0000 0000 0001 0000 0000 0000 0000
        internal const int TASK_STATE_DELEGATE_INVOKED = 0x20000;                              //bin: 0000 0000 0000 0010 0000 0000 0000 0000
        internal const int TASK_STATE_DISPOSED = 0x40000;                                      //bin: 0000 0000 0000 0100 0000 0000 0000 0000
        internal const int TASK_STATE_EXCEPTIONOBSERVEDBYPARENT = 0x80000;                     //bin: 0000 0000 0000 1000 0000 0000 0000 0000
        internal const int TASK_STATE_CANCELLATIONACKNOWLEDGED = 0x100000;                     //bin: 0000 0000 0001 0000 0000 0000 0000 0000
        internal const int TASK_STATE_FAULTED = 0x200000;                                      //bin: 0000 0000 0010 0000 0000 0000 0000 0000
        internal const int TASK_STATE_CANCELED = 0x400000;                                     //bin: 0000 0000 0100 0000 0000 0000 0000 0000
        internal const int TASK_STATE_WAITING_ON_CHILDREN = 0x800000;                          //bin: 0000 0000 1000 0000 0000 0000 0000 0000
        internal const int TASK_STATE_RAN_TO_COMPLETION = 0x1000000;                           //bin: 0000 0001 0000 0000 0000 0000 0000 0000
        internal const int TASK_STATE_WAITINGFORACTIVATION = 0x2000000;                        //bin: 0000 0010 0000 0000 0000 0000 0000 0000
        internal const int TASK_STATE_COMPLETION_RESERVED = 0x4000000;                         //bin: 0000 0100 0000 0000 0000 0000 0000 0000
        internal const int TASK_STATE_THREAD_WAS_ABORTED = 0x8000000;                          //bin: 0000 1000 0000 0000 0000 0000 0000 0000
        internal const int TASK_STATE_WAIT_COMPLETION_NOTIFICATION = 0x10000000;               //bin: 0001 0000 0000 0000 0000 0000 0000 0000
        internal const int TASK_STATE_EXECUTIONCONTEXT_IS_NULL = 0x20000000;                   //bin: 0010 0000 0000 0000 0000 0000 0000 0000
        internal const int TASK_STATE_TASKSCHEDULED_WAS_FIRED = 0x40000000;                    //bin: 0100 0000 0000 0000 0000 0000 0000 0000

        public static void Run(IConsole console, ClrRuntime runtime)
        {
            // Collect all state machines
            foreach (var obj in runtime.Heap.EnumerateObjects())
            {
                // Skip non-matching types
                if (!obj.Type.Name.StartsWith(AsyncStateMachineBoxTypeName) && !obj.Type.Name.StartsWith(DebugFinalizableBoxTypeName))
                {
                    continue;
                }

                // Get the status of the task
                var taskState = obj.GetField<int>("m_stateFlags");
                var taskStatus = ToTaskStatus(taskState);

                if(taskStatus == TaskStatus.Canceled || taskStatus == TaskStatus.Faulted || taskStatus == TaskStatus.RanToCompletion)
                {
                    continue;
                }

                // Get the state machine field
                var field = obj.Type.GetFieldByName("StateMachine");

                // Get address and method table
                if (field.ElementType == ClrElementType.Struct)
                {
                    var stateMachine = obj.GetValueClassField("StateMachine");

                    // Exclude Microsoft/System state machines
                    if (stateMachine.Type.Name.StartsWith("System") || stateMachine.Type.Name.StartsWith("Microsoft"))
                    {
                        continue;
                    }

                    console.WriteLine($"StateMachine: {stateMachine.Type.Name} struct 0x{stateMachine.Type.MethodTable:X}");
                    foreach (var stateMachineField in stateMachine.Type.Fields)
                    {
                        console.WriteLine($"  {stateMachineField.Name}: {stateMachineField.GetDisplayValue(stateMachine)}");
                    }
                }
                else
                {
                    var stateMachine = obj.GetObjectField("StateMachine");

                    // Exclude Microsoft/System state machines
                    if (stateMachine.Type.Name.StartsWith("System") || stateMachine.Type.Name.StartsWith("Microsoft"))
                    {
                        continue;
                    }

                    console.WriteLine($"StateMachine: {stateMachine.Type.Name} class 0x{stateMachine.Type.MethodTable:X}");
                    foreach (var stateMachineField in stateMachine.Type.Fields)
                    {
                        console.WriteLine($"  {stateMachineField.Name}: {stateMachineField.GetDisplayValue(stateMachine)}");
                    }
                }
            }
        }

        private static TaskStatus ToTaskStatus(int stateFlags)
        {
            TaskStatus rval;

            if ((stateFlags & TASK_STATE_FAULTED) != 0)
            {
                rval = TaskStatus.Faulted;
            }
            else if ((stateFlags & TASK_STATE_CANCELED) != 0)
            {
                rval = TaskStatus.Canceled;
            }
            else if ((stateFlags & TASK_STATE_RAN_TO_COMPLETION) != 0)
            {
                rval = TaskStatus.RanToCompletion;
            }
            else if ((stateFlags & TASK_STATE_WAITING_ON_CHILDREN) != 0)
            {
                rval = TaskStatus.WaitingForChildrenToComplete;
            }
            else if ((stateFlags & TASK_STATE_DELEGATE_INVOKED) != 0)
            {
                rval = TaskStatus.Running;
            }
            else if ((stateFlags & TASK_STATE_STARTED) != 0)
            {
                rval = TaskStatus.WaitingToRun;
            }
            else if ((stateFlags & TASK_STATE_WAITINGFORACTIVATION) != 0)
            {
                rval = TaskStatus.WaitingForActivation;
            }
            else
            {
                rval = TaskStatus.Created;
            }

            return rval;
        }
    }
}
