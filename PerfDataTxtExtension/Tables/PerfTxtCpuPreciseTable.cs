// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Diagnostics.Tracing.Stacks;
using Microsoft.Diagnostics.Tracing.StackSources;
using Microsoft.Performance.SDK;
using Microsoft.Performance.SDK.Processing;
using PerfDataExtensions.Tables.Generators;
using Utilities.AccessProviders;
using static PerfDataExtensions.Tables.TimeHelper;
using PerfDataExtensions.DataOutputTypes;

namespace PerfDataExtensions.Tables
{

    public class ContextSwapEvent
    {
        private static readonly string[] emptyStack = new string[] { };
        public double readyTime { get; set; }
        public double swapInTime { get; set; }
        public Timestamp swapInTimestamp { get; set; }
        public Timestamp readyTimestamp { get; set; }
        public Timestamp prevSwapOutTimestamp { get; set; }
        public double waitDuration { get; set; } = 0;
        public double readyDuration { get; set; } = 0;
        public double runDuration { get; set; } = 0;
        public int newThreadId { get; set; } = -1;
        public string newProcess { get; set; } = "Unknown (-1)";
        public int cpuNumber { get; set; }
        public int readyThreadId { get; set; } = -1;
        public string readyProcess { get; set; } = "Unknown (-1)";
        public string[] prevSwapOutStack { get; set; } = emptyStack;
        public string[] readyStack { get; set; } = emptyStack;
        public bool setNewProcess { get; set; } = false;
    }

    //
    // Add a Table attribute in order for the ProcessingSource to understand your table.
    // 

    [Table]              // A category is optional. It useful for grouping different types of tables

    //
    // Have the MetadataTable inherit the TableBase class
    //

    public sealed class PerfTxtCpuPreciseTable
        : LinuxPerfScriptTableBase
    {
        public static readonly TableDescriptor TableDescriptor = new TableDescriptor(
            Guid.Parse("{ea420d2c-1119-4437-ad76-3ddbd9c24b72}"),
            "CPU Precise",
            "CPU Precise Tables",
            category: "Linux");

        public PerfTxtCpuPreciseTable(IReadOnlyDictionary<string, List<PerfDataLinuxEvent>> parallelLinuxPerfScriptStackSource)
            : base(parallelLinuxPerfScriptStackSource)
        {
        }

        //
        // Declare columns here. You can do this using the ColumnConfiguration class. 
        // It is possible to declaratively describe the table configuration as well. Please refer to our Advanced Topics Wiki page for more information.
        //
        // The Column metadata describes each column in the table. 
        // Each column must have a unique GUID and a unique name. The GUID must be unique globally; the name only unique within the table.
        //
        // The UIHints provides some hints on how to render the column. 
        // In this sample, we are simply saying to allocate at least 80 units of width.
        //

        private static readonly ColumnConfiguration swapInTimeColumn =
            new ColumnConfiguration(
                new ColumnMetadata(new Guid("{e9167afc-f5e9-452b-8691-aafe23d56804}"), "Swap In Time"),
                new UIHints { Width = 80, });

        private static readonly ColumnConfiguration readyTimeColumn =
            new ColumnConfiguration(
                new ColumnMetadata(new Guid("{0d83ad27-3afc-47d8-9000-f20500d05b65}"), "Ready Time"),
                new UIHints { Width = 80, });

        private static readonly ColumnConfiguration prevSwapOutTimeColumn =
            new ColumnConfiguration(
                new ColumnMetadata(new Guid("{c35058d0-29fc-4661-a19c-ec6f9a25ff16}"), "Prev Swap Out Time"),
                new UIHints { Width = 80, });

        private static readonly ColumnConfiguration countColumn = new ColumnConfiguration(
            new ColumnMetadata(new Guid("{64ce7ed2-a45d-454c-b6e2-0cc07d70b252}"), "Count", "The count of samples"),
            new UIHints { Width = 130, AggregationMode = AggregationMode.Count });

        private static readonly ColumnConfiguration waitColumn = new ColumnConfiguration(
            new ColumnMetadata(new Guid("{fd74d82c-343e-4847-adce-14c36d490730}"), "Wait (us)", "Wait Duration"),
            new UIHints { Width = 130, AggregationMode = AggregationMode.Sum });

        private static readonly ColumnConfiguration readyColumn = new ColumnConfiguration(
            new ColumnMetadata(new Guid("{f89e6b87-86cd-4894-ae55-d7959e7244db}"), "Ready (us)", "Ready Duration"),
            new UIHints { Width = 130, AggregationMode = AggregationMode.Sum });

        private static readonly ColumnConfiguration runColumn = new ColumnConfiguration(
            new ColumnMetadata(new Guid("{07f832f6-7925-4e6a-bc45-088e9e12325c}"), "Run (us)", "Run Duration"),
            new UIHints { Width = 130, AggregationMode = AggregationMode.Sum });

        private static readonly ColumnConfiguration newThreadStackColumn =
            new ColumnConfiguration(
                new ColumnMetadata(new Guid("{8c24d04f-c84b-418a-b2e0-aa85b08903e1}"), "New Thread Stack"),
                new UIHints { Width = 80, });

        private static readonly ColumnConfiguration newThreadIdColumn =
            new ColumnConfiguration(
                new ColumnMetadata(new Guid("{71fdc1c4-cf56-4133-86bc-d06a8e2b14b1}"), "New Thread ID"),
                new UIHints { Width = 80, });

        private static readonly ColumnConfiguration newProcessColumn =
            new ColumnConfiguration(
                new ColumnMetadata(new Guid("{dfcc4d9e-3266-45ea-9c62-53973e50cc7f}"), "New Process"),
                new UIHints { Width = 80, });

        private static readonly ColumnConfiguration cpuColumn =
            new ColumnConfiguration(
                new ColumnMetadata(new Guid("{849374fe-7a04-40ee-ba5c-3383bb380629}"), "CPU"),
                new UIHints { Width = 80, });

        private static readonly ColumnConfiguration readyThreadStackColumn =
            new ColumnConfiguration(
                new ColumnMetadata(new Guid("{99fa314b-4804-4883-9fb8-380d37c811c3}"), "Ready Thread Stack"),
                new UIHints { Width = 80, });

        private static readonly ColumnConfiguration readyThreadIdColumn =
            new ColumnConfiguration(
                new ColumnMetadata(new Guid("{8152249c-a296-43b4-bf4c-cdc6ce5af8c8}"), "Ready Thread ID"),
                new UIHints { Width = 80, });

        private static readonly ColumnConfiguration readyProcessColumn =
            new ColumnConfiguration(
                new ColumnMetadata(new Guid("{10eb716f-2a0a-45c4-92ea-d4c05d3814bc}"), "Ready Process"),
                new UIHints { Width = 80, });

        private static readonly ColumnConfiguration cpuPctColumn =
            new ColumnConfiguration(
                new ColumnMetadata(new Guid("{d4b88997-1bf3-43a7-a19d-cfcba3cbc29c}"), "% CPU Usage"),
                new UIHints
                {
                    Width = 80,
                    AggregationMode = AggregationMode.Sum,
                    SortPriority = 0,
                    SortOrder = SortOrder.Descending,
                });

        public override void Build(ITableBuilder tableBuilder)
        {
            if (PerfDataTxtLogParsed == null || PerfDataTxtLogParsed.Count == 0)
            {
                return;
            }

            var firstPerfDataTxtLogParsed = PerfDataTxtLogParsed.First().Value;  // First Log
            double firstTimeStamp = 0;
            double lastTimeStamp = 0;

            if (firstPerfDataTxtLogParsed.Count > 0)
            {
                firstTimeStamp = firstPerfDataTxtLogParsed[0].TimeMSec;
                lastTimeStamp = firstPerfDataTxtLogParsed[firstPerfDataTxtLogParsed.Count - 1].TimeMSec;
            }

            // Init
            Dictionary<int, ContextSwapEvent> lastSwapOut = new Dictionary<int, ContextSwapEvent>();
            Dictionary<int, ContextSwapEvent> lastReady = new Dictionary<int, ContextSwapEvent>();
            Dictionary<Tuple<int, int>, PerfDataLinuxEvent> lastSwapOutThread = new Dictionary<Tuple<int, int>, PerfDataLinuxEvent>();
            List<ContextSwapEvent> contextSwaps = new List<ContextSwapEvent>();

            foreach (PerfDataLinuxEvent linuxEvent in firstPerfDataTxtLogParsed)
            {
                ContextSwapEvent cswap;

                if (lastSwapOut.TryGetValue(linuxEvent.CpuNumber, out cswap))
                {
                    // Fix up the new thread/process information for the cswap
                    if (!cswap.setNewProcess)
                    {
                        cswap.newThreadId = linuxEvent.ThreadID;
                        cswap.newProcess = string.Format("{0} ({1})", linuxEvent.Command, linuxEvent.ProcessID);
                        cswap.setNewProcess = true;
                    }
                }
                else
                {
                    // This is the first event on this CPU - create a dummy cswap at the trace start time
                    cswap = new ContextSwapEvent();
                    cswap.swapInTime = firstTimeStamp;
                    cswap.readyTime = firstTimeStamp;
                    cswap.swapInTimestamp = Timestamp.Zero;
                    cswap.readyTimestamp = Timestamp.Zero;
                    cswap.prevSwapOutTimestamp = Timestamp.Zero;
                    cswap.newThreadId = linuxEvent.ThreadID;
                    cswap.newProcess = string.Format("{0} ({1})", linuxEvent.Command, linuxEvent.ProcessID);
                    cswap.waitDuration = 0;
                    cswap.runDuration = lastTimeStamp - linuxEvent.TimeMSec;
                    cswap.cpuNumber = linuxEvent.CpuNumber;
                    cswap.setNewProcess = true;
                    lastSwapOut[linuxEvent.CpuNumber] = cswap;
                    contextSwaps.Add(cswap);
                }

                // The scheduler event has both swap in and swap out information, but the context-switch events only have swap out
                // Limit ourselves to just the swap out information as we need to handle this case anyways
                if (linuxEvent.Kind == EventKind.Scheduler || linuxEvent.EventName == "context-switches" || linuxEvent.EventName == "cs")
                {
                    Tuple<int, int> threadId;

                    if (linuxEvent.ThreadID == 0)
                    {
                        threadId = new Tuple<int, int>(linuxEvent.CpuNumber, linuxEvent.ThreadID);
                    }
                    else
                    {
                        threadId = new Tuple<int, int>(0, linuxEvent.ThreadID);
                    }

                    // There was a previous swap out for this CPU - the current event will have the thread that swapped in then
                    if (lastSwapOut.TryGetValue(linuxEvent.CpuNumber, out cswap))
                    {
                        PerfDataLinuxEvent prevSwapOut;

                        if (!cswap.setNewProcess)
                        {
                            cswap.newThreadId = linuxEvent.ThreadID;
                            cswap.newProcess = string.Format("{0} ({1})", linuxEvent.Command, linuxEvent.ProcessID);
                            cswap.setNewProcess = true;
                        }
                        cswap.runDuration = linuxEvent.TimeMSec - cswap.swapInTime;

                        // If this thread has swapped out before, track the previous swap out
                        if (lastSwapOutThread.TryGetValue(threadId, out prevSwapOut) && prevSwapOut.TimeMSec < cswap.swapInTime)
                        {
                            cswap.prevSwapOutTimestamp = new Timestamp(Convert.ToInt64((prevSwapOut.TimeMSec - firstTimeStamp) * 1000000));
                            cswap.waitDuration = cswap.swapInTime - prevSwapOut.TimeMSec;
                            cswap.prevSwapOutStack = prevSwapOut.stackFrame.stack;
                        }
                    }

                    if (lastReady.TryGetValue(linuxEvent.CpuNumber, out cswap) && cswap != null)
                    {
                        lastReady[linuxEvent.CpuNumber] = null;
                        cswap.readyDuration = linuxEvent.TimeMSec - cswap.readyTime;
                    }
                    else 
                    {
                        cswap = new ContextSwapEvent();
                        cswap.readyTimestamp = new Timestamp(Convert.ToInt64((linuxEvent.TimeMSec - firstTimeStamp) * 1000000));
                    }

                    cswap.swapInTime = linuxEvent.TimeMSec;
                    cswap.swapInTimestamp = new Timestamp(Convert.ToInt64((linuxEvent.TimeMSec - firstTimeStamp) * 1000000));
                    cswap.prevSwapOutTimestamp = Timestamp.Zero;
                    cswap.waitDuration = linuxEvent.TimeMSec - firstTimeStamp;
                    cswap.runDuration = lastTimeStamp - linuxEvent.TimeMSec;
                    cswap.cpuNumber = linuxEvent.CpuNumber;
                    lastSwapOut[linuxEvent.CpuNumber] = cswap;
                    lastSwapOutThread[threadId] = linuxEvent;
                    contextSwaps.Add(cswap);
                }
                else if (linuxEvent.Kind == EventKind.Wakeup)
                {
                    cswap = new ContextSwapEvent();
                    cswap.readyThreadId = linuxEvent.ThreadID;
                    cswap.readyProcess = string.Format("{0} ({1})", linuxEvent.Command, linuxEvent.ProcessID);
                    cswap.readyTime = linuxEvent.TimeMSec;
                    cswap.readyTimestamp = new Timestamp(Convert.ToInt64((linuxEvent.TimeMSec - firstTimeStamp) * 1000000));
                    cswap.readyStack = linuxEvent.stackFrame.stack;
                    cswap.newThreadId = linuxEvent.schedWakeup.ProcessId;
                    cswap.newProcess = string.Format("{0} ({1})", linuxEvent.schedWakeup.Comm, linuxEvent.schedWakeup.ProcessId);
                    lastReady[linuxEvent.schedWakeup.TargetCpu] = cswap;
                }
            }

            // For idle and unknown cswaps (often idle as well) clear out the wait and run times
            foreach (ContextSwapEvent cswap in contextSwaps)
            {
                if (cswap.newThreadId == 0 || cswap.newThreadId == -1)
                {
                    cswap.waitDuration = 0;
                    cswap.runDuration = 0;
                }
            }

            var baseProjection = Projection.Index(contextSwaps);

            // Constant columns
            var swapInTimeProjection = baseProjection.Compose(s => s.swapInTimestamp);
            var readyTimeProjection = baseProjection.Compose(s => s.readyTimestamp);
            var prevSwapOutTimeProjection = baseProjection.Compose(s => s.prevSwapOutTimestamp);
            var countProjection = baseProjection.Compose(s => 1);
            var waitProjection = baseProjection.Compose(s => s.waitDuration * 1000);
            var readyProjection = baseProjection.Compose(s => s.readyDuration * 1000);
            var runProjection = baseProjection.Compose(s => s.runDuration * 1000);
            var newThreadIdProjection = baseProjection.Compose(s => s.newThreadId);
            var newProcessProjection = baseProjection.Compose(s => s.newProcess);
            var newThreadStackProjection = baseProjection.Compose(s => s.prevSwapOutStack);
            var cpuProjection = baseProjection.Compose(s => s.cpuNumber);
            var readyThreadIdProjection = baseProjection.Compose(s => s.readyThreadId);
            var readyProcessProjection = baseProjection.Compose(s => s.readyProcess);
            var readyThreadStackProjection = baseProjection.Compose(s => s.readyStack);

            // For calculating %cpu
            var runTimeProjection = baseProjection.Compose(s => new TimeRange(s.swapInTimestamp, new TimestampDelta(Convert.ToInt64(s.runDuration * 1000000))));
            var cpuPercentProj = Projection.ClipTimeToVisibleDomain.CreatePercent(runTimeProjection);

            //
            // Table Configurations describe how your table should be presented to the user: 
            // the columns to show, what order to show them, which columns to aggregate, and which columns to graph. 
            // You may provide a number of columns in your table, but only want to show a subset of them by default so as not to overwhelm the user. 
            // The user can still open the table properties to turn on or off columns.
            // The table configuration class also exposes four (4) columns UI explicitly recognizes: Pivot Column, Graph Column, Left Freeze Column, Right Freeze Column
            // For more information about what these columns do, go to "Advanced Topics" -> "Table Configuration" in our Wiki. Link can be found in README.md
            //

            var contextSwapsByProcessNewStackReadyStackConfig = new TableConfiguration("CPU Precise by Process, New Stack, Ready Stack")
            {
              Columns = new[]
              {
                  newProcessColumn,
                  newThreadStackColumn,
                  readyThreadStackColumn,
                  TableConfiguration.PivotColumn,
                  swapInTimeColumn,
                  readyTimeColumn,
                  newThreadIdColumn,
                  readyThreadIdColumn,
                  readyColumn,
                  waitColumn,
                  runColumn,
                  cpuPctColumn,
                  TableConfiguration.GraphColumn,
                  countColumn
                },
            };
            contextSwapsByProcessNewStackReadyStackConfig.AddColumnRole(ColumnRole.EndTime, swapInTimeColumn);
            contextSwapsByProcessNewStackReadyStackConfig.AddColumnRole(ColumnRole.Duration, waitColumn);
            contextSwapsByProcessNewStackReadyStackConfig.AddColumnRole(ColumnRole.ResourceId, newThreadIdColumn);

            //
            //
            //  Use the table builder to build the table. 
            //  Add and set table configuration if applicable.
            //  Then set the row count (we have one row per file) and then add the columns using AddColumn.
            //
            var table = tableBuilder
                .AddTableConfiguration(contextSwapsByProcessNewStackReadyStackConfig)
                .SetDefaultTableConfiguration(contextSwapsByProcessNewStackReadyStackConfig)
                .SetRowCount(contextSwaps.Count)
                .AddColumn(swapInTimeColumn, swapInTimeProjection)
                .AddColumn(readyTimeColumn, readyTimeProjection)
                .AddColumn(prevSwapOutTimeColumn, prevSwapOutTimeProjection)
                .AddColumn(cpuColumn, cpuProjection)
                .AddColumn(countColumn, countProjection)
                .AddColumn(waitColumn, waitProjection)
                .AddColumn(readyColumn, readyProjection)
                .AddColumn(runColumn, runProjection)
                .AddColumn(newThreadIdColumn, newThreadIdProjection)
                .AddColumn(newProcessColumn, newProcessProjection)
                .AddColumn(readyThreadIdColumn, readyThreadIdProjection)
                .AddColumn(readyProcessColumn, readyProcessProjection)
                .AddColumn(cpuPctColumn, cpuPercentProj)
            ;

            table.AddHierarchicalColumn(newThreadStackColumn, newThreadStackProjection, new ArrayAccessProvider<string>());
            table.AddHierarchicalColumn(readyThreadStackColumn, readyThreadStackProjection, new ArrayAccessProvider<string>());

        }
    }
}
