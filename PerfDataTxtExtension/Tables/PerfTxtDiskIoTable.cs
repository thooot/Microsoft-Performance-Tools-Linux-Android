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
    public class DiskIoEvent
    {
        public static readonly string IoTypeRead = "Read";
        public static readonly string IoTypeWrite = "Write";
        public static readonly string IoTypeFlush = "Flush";
        public static readonly string IoTypeTrim = "Trim";
        public static readonly string IoTypeUnknown = "Unknown";
        public PerfDataLinuxEvent diskIoInit { get; set; }
        public PerfDataLinuxEvent diskIoComplete { get; set; }
        public int InitQueueDepth { get; set; } = 0;
        public int CompleteQueueDepth { get; set; } = 0;
        public uint Device() { return diskIoInit != null ? diskIoInit.blockReqIssue.Device : diskIoComplete.blockReqComplete.Device; }
        public uint DeviceMinor() { return diskIoInit != null ? diskIoInit.blockReqIssue.DeviceMinor : diskIoComplete.blockReqComplete.DeviceMinor; }
        public string Flags() { return diskIoInit != null ? diskIoInit.blockReqIssue.Flags : diskIoComplete.blockReqComplete.Flags; }
        public ulong Sector() { return diskIoInit != null ? diskIoInit.blockReqIssue.Sector : diskIoComplete.blockReqComplete.Sector; }
        public uint SectorLength() { return diskIoInit != null ? diskIoInit.blockReqIssue.SectorLength : diskIoComplete.blockReqComplete.SectorLength; }
        public ulong Offset() 
        {
            if (diskIoInit != null)
            {
                if (diskIoInit.blockReqIssue.SectorLength != 0)
                {
                    return diskIoInit.blockReqIssue.Sector * (diskIoInit.blockReqIssue.Length / diskIoInit.blockReqIssue.SectorLength);
                }
                else
                {
                    return diskIoInit.blockReqIssue.Sector * 512;
                }
            }
            else
            {
                return diskIoComplete.blockReqComplete.Sector * 512;
            }
        }
        public uint Length() { return diskIoInit != null ? diskIoInit.blockReqIssue.Length : 0; }
        public double StartTime() { return diskIoInit != null ? diskIoInit.TimeMSec: diskIoComplete.TimeMSec; }
        public double EndTime() { return diskIoComplete != null ? diskIoComplete.TimeMSec : diskIoInit.TimeMSec; }
        public double Duration() { return (diskIoInit != null && diskIoComplete != null) ? (diskIoComplete.TimeMSec - diskIoInit.TimeMSec) : 0; }
        public string IoType()
        {
            string flags = Flags();
            // R = read, W = write, F = flush, D = discard, M = metadata, S = synchronous, N = ???
            if (flags.Contains('R')) { return IoTypeRead; }
            if (flags.Contains('W')) { return IoTypeWrite; }
            if (flags.Contains('F')) { return IoTypeFlush; }
            if (flags.Contains('D')) { return IoTypeTrim; }
            return IoTypeUnknown;
        }
    }

    public class DiskIoEventComparer : Comparer<DiskIoEvent>
    {
        public override int Compare(DiskIoEvent e1, DiskIoEvent e2)
        {
            if (e1.Device() < e2.Device()) { return -1; }
            if (e1.Device() > e2.Device()) { return 1; }
            if (e1.DeviceMinor() < e2.DeviceMinor()) { return -1; }
            if (e1.DeviceMinor() > e2.DeviceMinor()) { return 1; }
            if (e1.Sector() < e2.Sector()) { return -1; }
            if (e1.Sector() > e2.Sector()) { return 1; }
            if (e1.SectorLength() < e2.SectorLength()) { return -1; }
            if (e1.SectorLength() > e2.SectorLength()) { return 1; }
            return 0;
        }
    }

    //
    // Add a Table attribute in order for the ProcessingSource to understand your table.
    // 

    [Table]              // A category is optional. It useful for grouping different types of tables

    //
    // Have the MetadataTable inherit the TableBase class
    //

    public sealed class PerfTxtDiskIoTable
        : LinuxPerfScriptTableBase
    {
        public static readonly TableDescriptor TableDescriptor = new TableDescriptor(
            Guid.Parse("{dc48b515-d2f6-4c97-8a27-3892a8e3d5ba}"),
            "Disk IO",
            "Disk IO Tables",
            category: "Linux");

        public PerfTxtDiskIoTable(IReadOnlyDictionary<string, List<PerfDataLinuxEvent>> parallelLinuxPerfScriptStackSource)
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

        private static readonly ColumnConfiguration startTimeColumn =
            new ColumnConfiguration(
                new ColumnMetadata(new Guid("{19eeec9f-067a-4d70-b797-7723fa357127}"), "Start Time"),
                new UIHints { Width = 80, });

        private static readonly ColumnConfiguration endTimeColumn =
            new ColumnConfiguration(
                new ColumnMetadata(new Guid("{0a75cadf-599e-489b-8d7e-5eb3f21eed5e}"), "End Time"),
                new UIHints { Width = 80, });

        private static readonly ColumnConfiguration durationColumn =
            new ColumnConfiguration(
                new ColumnMetadata(new Guid("{49a67dd7-3f75-48a4-b56d-d0fe50ae1337}"), "Duration (us)"),
                new UIHints { Width = 80, AggregationMode = AggregationMode.Sum });

        private static readonly ColumnConfiguration countColumn = new ColumnConfiguration(
            new ColumnMetadata(new Guid("{4f923a5d-e6bb-495c-8b7a-8a4feb31c93a}"), "Count", "The count of samples"),
            new UIHints { Width = 130, AggregationMode = AggregationMode.Count });

        private static readonly ColumnConfiguration initStackColumn =
            new ColumnConfiguration(
                new ColumnMetadata(new Guid("{900077a4-2151-49ee-b8b4-6e7a857f3ec0}"), "Init Callstack"),
                new UIHints { Width = 80, });

        private static readonly ColumnConfiguration threadIdColumn =
            new ColumnConfiguration(
                new ColumnMetadata(new Guid("{9ac82193-74e3-4b9c-9d82-b641dea3b292}"), "Thread ID"),
                new UIHints { Width = 80, });

        private static readonly ColumnConfiguration processIdColumn =
            new ColumnConfiguration(
                new ColumnMetadata(new Guid("{3b6a6fca-88a3-4736-b1f2-325989e60d3f}"), "Process ID"),
                new UIHints { Width = 80, });

        private static readonly ColumnConfiguration processColumn =
            new ColumnConfiguration(
                new ColumnMetadata(new Guid("{06d35463-22ad-4571-9576-a5c0e294a584}"), "Process"),
                new UIHints { Width = 80, });

        private static readonly ColumnConfiguration processNameColumn =
            new ColumnConfiguration(
                new ColumnMetadata(new Guid("{0f7a83f2-2312-4533-8fff-7aa70ae1e0f5}"), "Process Name"),
                new UIHints { Width = 80, });

        private static readonly ColumnConfiguration cpuColumn =
            new ColumnConfiguration(
                new ColumnMetadata(new Guid("{8f571410-1057-4821-89c2-5262bf2d94a5}"), "CPU"),
                new UIHints { Width = 80, });

        private static readonly ColumnConfiguration deviceColumn =
            new ColumnConfiguration(
                new ColumnMetadata(new Guid("{0ab19e95-f913-4350-94b6-1b70feb95deb}"), "Device"),
                new UIHints { Width = 80, });

        private static readonly ColumnConfiguration ioTypeColumn =
            new ColumnConfiguration(
                new ColumnMetadata(new Guid("{473df4bc-ba48-4481-813a-9247daeac215}"), "IO Type"),
                new UIHints { Width = 80, });

        private static readonly ColumnConfiguration offsetColumn =
            new ColumnConfiguration(
                new ColumnMetadata(new Guid("{771c59fb-1432-42da-9aa5-ff690b736d8a}"), "Offset"),
                new UIHints { Width = 80, });

        private static readonly ColumnConfiguration lengthColumn =
            new ColumnConfiguration(
                new ColumnMetadata(new Guid("{9362748c-82d9-46eb-abe6-075380d6640b}"), "Length"),
                new UIHints { Width = 80, AggregationMode = AggregationMode.Sum });

        private static readonly ColumnConfiguration flagsColumn =
            new ColumnConfiguration(
                new ColumnMetadata(new Guid("{af519eeb-3ba9-4ec0-8301-250e7d966822}"), "Flags"),
                new UIHints { Width = 80, });

        private static readonly ColumnConfiguration queueDepthInitColumn =
            new ColumnConfiguration(
                new ColumnMetadata(new Guid("{5108fecd-37db-4e66-8a19-5302c8418d28}"), "QD Init"),
                new UIHints { Width = 80, });

        private static readonly ColumnConfiguration queueDepthCompleteColumn =
            new ColumnConfiguration(
                new ColumnMetadata(new Guid("{9ec3d701-2158-4223-b04a-5775756f448f}"), "QD Complete"),
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
            List<DiskIoEvent> diskIoEvents = new List<DiskIoEvent>();
            List<DiskIoEvent> linuxEventToDiskIo = new List<DiskIoEvent>();
            var outstandingIos = new SortedSet<DiskIoEvent>(new DiskIoEventComparer());

            // Match IO init with IO complete and add to disk IO list
            foreach (PerfDataLinuxEvent linuxEvent in firstPerfDataTxtLogParsed)
            {
                if (linuxEvent.Kind == EventKind.BlockRequestIssue)
                {
                    DiskIoEvent diskIoEvent = new DiskIoEvent();
                    diskIoEvent.diskIoInit = linuxEvent;

                    if (outstandingIos.Contains(diskIoEvent))
                    {
                        outstandingIos.Remove(diskIoEvent);
                    }
                    linuxEventToDiskIo.Add(diskIoEvent);
                    outstandingIos.Add(diskIoEvent);
                    diskIoEvents.Add(diskIoEvent);
                }
                else if (linuxEvent.Kind == EventKind.BlockRequestComplete)
                {
                    DiskIoEvent diskIoEvent = new DiskIoEvent();
                    DiskIoEvent initEvent;
                    diskIoEvent.diskIoComplete = linuxEvent;

                    if (outstandingIos.TryGetValue(diskIoEvent, out initEvent))
                    {
                        initEvent.diskIoComplete = linuxEvent;
                        diskIoEvent = initEvent;
                        outstandingIos.Remove(initEvent);
                    }
                    else
                    {
                        diskIoEvents.Add(diskIoEvent);
                    }
                    linuxEventToDiskIo.Add(diskIoEvent);
                }
            }

            // Calculate queue depth at init/complete for IOs that we matched
            Dictionary<Tuple<uint, uint>, int> deviceQueueDepth = new Dictionary<Tuple<uint, uint>, int>();
            int index = 0;
            foreach (PerfDataLinuxEvent linuxEvent in firstPerfDataTxtLogParsed)
            {
                if (linuxEvent.Kind == EventKind.BlockRequestIssue)
                {
                    DiskIoEvent diskIoEvent = linuxEventToDiskIo[index];

                    if (diskIoEvent.diskIoInit != null && diskIoEvent.diskIoComplete != null)
                    {
                        Tuple<uint, uint> device = new Tuple<uint, uint>(linuxEvent.blockReqIssue.Device, linuxEvent.blockReqIssue.DeviceMinor);
                        int depth = 0;

                        deviceQueueDepth.TryGetValue(device, out depth);
                        diskIoEvent.InitQueueDepth = depth;
                        depth += 1;
                        deviceQueueDepth[device] = depth;
                    }
                    index++;
                }
                else if (linuxEvent.Kind == EventKind.BlockRequestComplete)
                {
                    DiskIoEvent diskIoEvent = linuxEventToDiskIo[index];

                    if (diskIoEvent.diskIoInit != null && diskIoEvent.diskIoComplete != null)
                    {
                        Tuple<uint, uint> device = new Tuple<uint, uint>(linuxEvent.blockReqComplete.Device, linuxEvent.blockReqComplete.DeviceMinor);
                        int depth = 0;

                        if (deviceQueueDepth.TryGetValue(device, out depth))
                        {
                            diskIoEvent.CompleteQueueDepth = depth;
                            if (depth > 0) { depth -= 1; }
                            deviceQueueDepth[device] = depth;
                        }
                    }
                    index++;
                }
            }

            var baseProjection = Projection.CreateUsingFuncAdaptor(new Func<int,int>(i => i));

            // Constant columns
            var startTimeProjection = baseProjection.Compose(s => new Timestamp(Convert.ToInt64(((diskIoEvents[s].diskIoInit != null ? diskIoEvents[s].diskIoInit.TimeMSec : diskIoEvents[s].diskIoComplete.TimeMSec) - firstTimeStamp) * 1000000)));
            var endTimeProjection = baseProjection.Compose(s => new Timestamp(Convert.ToInt64(((diskIoEvents[s].diskIoComplete != null ? diskIoEvents[s].diskIoComplete.TimeMSec : diskIoEvents[s].diskIoInit.TimeMSec) - firstTimeStamp) * 1000000)));
            var durationProjection = baseProjection.Compose(s => ((diskIoEvents[s].diskIoComplete != null ? diskIoEvents[s].diskIoComplete.TimeMSec : diskIoEvents[s].diskIoInit.TimeMSec) - (diskIoEvents[s].diskIoInit != null ? diskIoEvents[s].diskIoInit.TimeMSec : diskIoEvents[s].diskIoComplete.TimeMSec)) * 1000);
            var cpuProjection = baseProjection.Compose(s => diskIoEvents[s].diskIoInit != null ? diskIoEvents[s].diskIoInit.CpuNumber : diskIoEvents[s].diskIoComplete.CpuNumber);
            var countProjection = baseProjection.Compose(s => 1);
            var threadIdProjection = baseProjection.Compose(s => diskIoEvents[s].diskIoInit != null ? diskIoEvents[s].diskIoInit.ThreadID : diskIoEvents[s].diskIoComplete.ThreadID);
            var processIdProjection = baseProjection.Compose(s => diskIoEvents[s].diskIoInit != null ? diskIoEvents[s].diskIoInit.ProcessID : diskIoEvents[s].diskIoInit.ProcessID);
            var processProjection = baseProjection.Compose(s => diskIoEvents[s].diskIoInit != null ? string.Format("{0} ({1})", diskIoEvents[s].diskIoInit.Command, diskIoEvents[s].diskIoInit.ProcessID) : string.Format("{0} ({1})", diskIoEvents[s].diskIoComplete.Command, diskIoEvents[s].diskIoComplete.ProcessID));
            var processNameProjection = baseProjection.Compose(s => diskIoEvents[s].diskIoInit != null ? diskIoEvents[s].diskIoInit.Command : diskIoEvents[s].diskIoComplete.Command);
            var deviceProjection = baseProjection.Compose(s => string.Format("{0}:{1}", diskIoEvents[s].Device(), diskIoEvents[s].DeviceMinor()));
            var ioTypeProjection = baseProjection.Compose(s => diskIoEvents[s].IoType());
            var offsetProjection = baseProjection.Compose(s => diskIoEvents[s].Offset());
            var lengthProjection = baseProjection.Compose(s => diskIoEvents[s].Length());
            var flagsProjection = baseProjection.Compose(s => diskIoEvents[s].Flags());
            var queueDepthInitProjection = baseProjection.Compose(s => diskIoEvents[s].InitQueueDepth);
            var queueDepthCompleteProjection = baseProjection.Compose(s => diskIoEvents[s].CompleteQueueDepth);

            IProjection<int, int> countProj = SequentialGenerator.Create(
                diskIoEvents.Count,
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

            var diskIosByProcessDiskTypeConfig = new TableConfiguration("Disk IOs by Process, Disk, Type")
            {
              Columns = new[]
              {
                  processColumn,
                  deviceColumn,
                  ioTypeColumn,
                  TableConfiguration.PivotColumn,
                  startTimeColumn,
                  endTimeColumn,
                  offsetColumn,
                  lengthColumn,
                  durationColumn,
                  TableConfiguration.GraphColumn,
                  countColumn
                },
            };
            diskIosByProcessDiskTypeConfig.AddColumnRole(ColumnRole.StartTime, startTimeColumn);
            diskIosByProcessDiskTypeConfig.AddColumnRole(ColumnRole.EndTime, endTimeColumn);
            diskIosByProcessDiskTypeConfig.AddColumnRole(ColumnRole.Duration, durationColumn);
            diskIosByProcessDiskTypeConfig.AddColumnRole(ColumnRole.ResourceId, deviceColumn);

            //
            //
            //  Use the table builder to build the table. 
            //  Add and set table configuration if applicable.
            //  Then set the row count (we have one row per file) and then add the columns using AddColumn.
            //
            var table = tableBuilder
                .AddTableConfiguration(diskIosByProcessDiskTypeConfig)
                .SetDefaultTableConfiguration(diskIosByProcessDiskTypeConfig)
                .SetRowCount(diskIoEvents.Count)
                .AddColumn(startTimeColumn, startTimeProjection)
                .AddColumn(endTimeColumn, endTimeProjection)
                .AddColumn(durationColumn, durationProjection)
                .AddColumn(cpuColumn, cpuProjection)
                .AddColumn(countColumn, countProjection)
                .AddColumn(threadIdColumn, threadIdProjection)
                .AddColumn(processIdColumn, processIdProjection)
                .AddColumn(processColumn, processProjection)
                .AddColumn(processNameColumn, processNameProjection)
                .AddColumn(deviceColumn, deviceProjection)
                .AddColumn(ioTypeColumn, ioTypeProjection)
                .AddColumn(offsetColumn, offsetProjection)
                .AddColumn(lengthColumn, lengthProjection)
                .AddColumn(flagsColumn, flagsProjection)
                .AddColumn(queueDepthInitColumn, queueDepthInitProjection)
                .AddColumn(queueDepthCompleteColumn, queueDepthCompleteProjection)
            ;

            table.AddHierarchicalColumn(initStackColumn, baseProjection.Compose((i) => diskIoEvents[i].diskIoInit != null ? diskIoEvents[i].diskIoInit.stackFrame.stack : new string[] { }), new ArrayAccessProvider<string>());

        }
    }
}
