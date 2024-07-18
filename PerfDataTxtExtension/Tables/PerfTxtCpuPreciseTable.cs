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
        public PerfDataLinuxEvent swapOutEvent { get; set; }
        public PerfDataLinuxEvent nextSwapOutEvent { get; set; }
        public PerfDataLinuxEvent prevSwapOutThreadEvent { get; set; }
        public PerfDataLinuxEvent readyEvent { get; set; }
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
                // The scheduler event has both swap in and swap out information, but the context-switch events only have swap out
                // Limit ourselves to just the swap out information as we need to handle this case anyways
                if (linuxEvent.Kind == EventKind.Scheduler || linuxEvent.EventName == "context-switches" || linuxEvent.EventName == "cs")
                {
                    Tuple<int, int> threadId;
                    ContextSwapEvent cswap;

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

                        cswap.nextSwapOutEvent = linuxEvent;

                        // If this thread has swapped out before, track the previous swap out
                        if (lastSwapOutThread.TryGetValue(threadId, out prevSwapOut) && prevSwapOut.TimeMSec < cswap.swapOutEvent.TimeMSec)
                        {
                            cswap.prevSwapOutThreadEvent = prevSwapOut;
                        }
                    }

                    if (lastReady.TryGetValue(linuxEvent.CpuNumber, out cswap) && cswap != null)
                    {
                        lastReady[linuxEvent.CpuNumber] = null;
                    }
                    else 
                    {
                        cswap = new ContextSwapEvent();
                    }

                    cswap.swapOutEvent = linuxEvent;
                    lastSwapOut[linuxEvent.CpuNumber] = cswap;
                    lastSwapOutThread[threadId] = linuxEvent;
                    contextSwaps.Add(cswap);
                }
                else if (linuxEvent.Kind == EventKind.Wakeup)
                {
                    ContextSwapEvent cswap = new ContextSwapEvent();
                    cswap.readyEvent = linuxEvent;
                    lastReady[linuxEvent.schedWakeup.TargetCpu] = cswap;
                }
            }

            var baseProjection = Projection.CreateUsingFuncAdaptor(new Func<int,int>(i => i));

            // Constant columns
            var swapInTimeProjection = baseProjection.Compose(s => new Timestamp(Convert.ToInt64((contextSwaps[s].swapOutEvent.TimeMSec - firstTimeStamp) * 1000000)));
            var readyTimeProjection = baseProjection.Compose(s => new Timestamp(Convert.ToInt64(((contextSwaps[s].readyEvent != null ? contextSwaps[s].readyEvent.TimeMSec : contextSwaps[s].swapOutEvent.TimeMSec) - firstTimeStamp) * 1000000)));
            var prevSwapOutTimeProjection = baseProjection.Compose(s => new Timestamp(Convert.ToInt64((contextSwaps[s].prevSwapOutThreadEvent != null ? contextSwaps[s].prevSwapOutThreadEvent.TimeMSec - firstTimeStamp : 0) * 1000000)));
            var countProjection = baseProjection.Compose(s => 1);
            var waitProjection = baseProjection.Compose(s => (contextSwaps[s].swapOutEvent.TimeMSec - (contextSwaps[s].prevSwapOutThreadEvent != null ? contextSwaps[s].prevSwapOutThreadEvent.TimeMSec : firstTimeStamp)) * 1000);
            var readyProjection = baseProjection.Compose(s => (contextSwaps[s].readyEvent != null ? (contextSwaps[s].swapOutEvent.TimeMSec - contextSwaps[s].readyEvent.TimeMSec) : 0) * 1000);
            var runProjection = baseProjection.Compose(s => ((contextSwaps[s].nextSwapOutEvent != null ? contextSwaps[s].nextSwapOutEvent.TimeMSec : lastTimeStamp) - contextSwaps[s].swapOutEvent.TimeMSec) * 1000);
            var newThreadIdProjection = baseProjection.Compose(s => contextSwaps[s].nextSwapOutEvent != null ? contextSwaps[s].nextSwapOutEvent.ThreadID : -1);
            var newProcessProjection = baseProjection.Compose(s => contextSwaps[s].nextSwapOutEvent != null ? string.Format("{0} ({1})", contextSwaps[s].nextSwapOutEvent.Command, contextSwaps[s].nextSwapOutEvent.ProcessID) : "Unknown (-1)");
            var cpuProjection = baseProjection.Compose(s => contextSwaps[s].swapOutEvent.CpuNumber);
            var readyThreadIdProjection = baseProjection.Compose(s => contextSwaps[s].readyEvent != null ? contextSwaps[s].readyEvent.ThreadID : -1);
            var readyProcessProjection = baseProjection.Compose(s => contextSwaps[s].readyEvent != null ? string.Format("{0} ({1})", contextSwaps[s].readyEvent.Command, contextSwaps[s].readyEvent.ProcessID) : "Unknown (-1)");

            IProjection<int, int> countProj = SequentialGenerator.Create(
                contextSwaps.Count,
                Projection.Constant(1),
                Projection.Constant(0));

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
            ;

            table.AddHierarchicalColumn(newThreadStackColumn, baseProjection.Compose((i) => contextSwaps[i].prevSwapOutThreadEvent != null ? contextSwaps[i].prevSwapOutThreadEvent.stackFrame.stack : new string[] { }), new ArrayAccessProvider<string>());
            table.AddHierarchicalColumn(readyThreadStackColumn, baseProjection.Compose((i) => contextSwaps[i].readyEvent != null ? contextSwaps[i].readyEvent.stackFrame.stack : new string[] { }), new ArrayAccessProvider<string>());

        }
    }
}
