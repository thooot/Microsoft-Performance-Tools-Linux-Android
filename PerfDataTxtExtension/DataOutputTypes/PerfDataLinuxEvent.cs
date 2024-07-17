// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Diagnostics.Tracing.StackSources;
using System.Collections.Generic;

namespace PerfDataExtensions.DataOutputTypes
{
    public class PerfDataLinuxEvent : LinuxEvent
    {
        public ScheduleSwitch schedulerSwitch { get; }
        public ThreadExit threadExit { get; }
        public BlockReqIssue blockReqIssue { get; }
        public BlockReqComplete blockReqComplete { get; }
        public PerfDataStackFrame stackFrame { get; }

        public PerfDataLinuxEvent(
            LinuxEvent linuxEvent,
            PerfDataStackFrame stackFrame) :
            base(linuxEvent.Kind, linuxEvent.Command, linuxEvent.ThreadID, linuxEvent.ProcessID, 
                 linuxEvent.TimeMSec, linuxEvent.TimeProperty, linuxEvent.CpuNumber, linuxEvent.EventName, 
                 linuxEvent.EventProperty, null)
        {
            if (linuxEvent.Kind == EventKind.Scheduler)
            {
                schedulerSwitch = ((SchedulerEvent)linuxEvent).Switch;
            }
            else if (linuxEvent.Kind == EventKind.ThreadExit)
            {
                threadExit = ((ThreadExitEvent)linuxEvent).Exit;
            }
            else if (linuxEvent.Kind == EventKind.BlockRequestIssue)
            {
                blockReqIssue = ((BlockReqIssueEvent)linuxEvent).ReqIssue;
            }
            else if (linuxEvent.Kind == EventKind.BlockRequestComplete)
            {
                blockReqComplete = ((BlockReqCompleteEvent)linuxEvent).ReqComplete;
            }

            this.stackFrame = stackFrame;
        }
    }

    public class PerfDataStackFrame
    {
        public Microsoft.Diagnostics.Tracing.StackSources.StackFrame stackFrame { get; }
        public string[] stack { get; }
        public Dictionary<Microsoft.Diagnostics.Tracing.StackSources.StackFrame, PerfDataStackFrame> children { get; }

        public PerfDataStackFrame(Microsoft.Diagnostics.Tracing.StackSources.StackFrame stackFrame, string[] stack)
        {
            this.stackFrame = stackFrame;
            this.stack = stack;
            this.children = new Dictionary<Microsoft.Diagnostics.Tracing.StackSources.StackFrame, PerfDataStackFrame>();
        }
    }
}
