using Microsoft.Performance.SDK;
using Microsoft.Performance.SDK.Processing;
using Utilities.AccessProviders;
using PerfDataExtensions.DataOutputTypes;
using PerfDataExtensions.Tables;
using PerfDataExtensions.Tables.Generators;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Diagnostics.Tracing.StackSources;

namespace PerfDataTxtExtension.Tables
{
    public class IrqEvent
    {
        public PerfDataLinuxEvent irqEnter { get; set; }
        public PerfDataLinuxEvent irqExit { get; set; }
        public uint Vector() { return irqEnter != null ? irqEnter.irqEnter.Vector : irqExit.irqExit.Vector; }
        public string Name() { return irqEnter != null ? irqEnter.irqEnter.Name : "Unknown"; }
        public string Status() { return irqExit != null ? irqExit.irqExit.Status : "Unknown"; }
        public int CpuNumber() { return irqEnter != null ? irqEnter.CpuNumber : irqExit.CpuNumber; }
        public double StartTime() { return irqEnter != null ? irqEnter.TimeMSec : irqExit.TimeMSec; }
        public double EndTime() { return irqExit != null ? irqExit.TimeMSec : irqEnter.TimeMSec; }
        public double Duration() { return (irqEnter != null && irqExit != null) ? (irqExit.TimeMSec - irqEnter.TimeMSec) : 0; }
    }

    public class IrqEventComparer : Comparer<IrqEvent>
    {
        public override int Compare(IrqEvent e1, IrqEvent e2)
        {
            if (e1.Vector() < e2.Vector()) { return -1; }
            if (e1.Vector() > e2.Vector()) { return 1; }
            if (e1.CpuNumber() < e2.CpuNumber()) { return -1; }
            if (e1.CpuNumber() > e2.CpuNumber()) { return 1; }
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

    public sealed class PerfTxtIsrTable
        : LinuxPerfScriptTableBase
    {
        public static readonly TableDescriptor TableDescriptor = new TableDescriptor(
            Guid.Parse("{ed25d1f6-3406-473a-aec1-1011266af1f2}"),
            "ISR",
            "ISR Tables",
            category: "Linux");

        public PerfTxtIsrTable(IReadOnlyDictionary<string, List<PerfDataLinuxEvent>> parallelLinuxPerfScriptStackSource)
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
                new ColumnMetadata(new Guid("{b57bee14-4e90-40ef-8593-0da342261fbe}"), "Start Time"),
                new UIHints { Width = 80, });

        private static readonly ColumnConfiguration endTimeColumn =
            new ColumnConfiguration(
                new ColumnMetadata(new Guid("{701f2e0f-84f3-4280-8c0f-6187cf3adb7d}"), "End Time"),
                new UIHints { Width = 80, });

        private static readonly ColumnConfiguration durationColumn =
            new ColumnConfiguration(
                new ColumnMetadata(new Guid("{b47e5cd1-dbfd-4a46-834b-7ece679fc841}"), "Duration (us)"),
                new UIHints { Width = 80, AggregationMode = AggregationMode.Sum });

        private static readonly ColumnConfiguration countColumn = new ColumnConfiguration(
            new ColumnMetadata(new Guid("{50a82112-31e6-451e-8271-e943bcb28b4b}"), "Count", "The count of samples"),
            new UIHints { Width = 130, AggregationMode = AggregationMode.Count });

        private static readonly ColumnConfiguration initStackColumn =
            new ColumnConfiguration(
                new ColumnMetadata(new Guid("{c9d89fed-d615-4455-977c-f6309aa09e22}"), "Init Callstack"),
                new UIHints { Width = 80, });

        private static readonly ColumnConfiguration threadIdColumn =
            new ColumnConfiguration(
                new ColumnMetadata(new Guid("{93881cfc-99d1-4f68-90fb-f748677f703f}"), "Thread ID"),
                new UIHints { Width = 80, });

        private static readonly ColumnConfiguration processIdColumn =
            new ColumnConfiguration(
                new ColumnMetadata(new Guid("{7e0aa9a2-ce57-4d3a-a233-9babde17eb8c}"), "Process ID"),
                new UIHints { Width = 80, });

        private static readonly ColumnConfiguration processColumn =
            new ColumnConfiguration(
                new ColumnMetadata(new Guid("{0432e91e-de76-4a27-ae32-bffb78dcc199}"), "Process"),
                new UIHints { Width = 80, });

        private static readonly ColumnConfiguration processNameColumn =
            new ColumnConfiguration(
                new ColumnMetadata(new Guid("{5ae85865-61cf-4185-b036-7c1be5d8b9de}"), "Process Name"),
                new UIHints { Width = 80, });

        private static readonly ColumnConfiguration cpuColumn =
            new ColumnConfiguration(
                new ColumnMetadata(new Guid("{46d46087-e79d-44dc-aaa7-d52cbed13228}"), "CPU"),
                new UIHints { Width = 80, });

        private static readonly ColumnConfiguration vectorColumn =
            new ColumnConfiguration(
                new ColumnMetadata(new Guid("{5db40a21-dec3-44b4-a2dd-aa9758966d80}"), "Vector"),
                new UIHints { Width = 80, });

        private static readonly ColumnConfiguration nameColumn =
            new ColumnConfiguration(
                new ColumnMetadata(new Guid("{1d173511-5fcb-4b51-a0ff-ad68bdc9f25f}"), "Name"),
                new UIHints { Width = 80, });

        private static readonly ColumnConfiguration statusColumn =
            new ColumnConfiguration(
                new ColumnMetadata(new Guid("{e863c1d5-abff-43f9-9f88-aa1e371b420b}"), "Status"),
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
            List<IrqEvent> irqEvents = new List<IrqEvent>();
            var outstandingIrqs = new SortedSet<IrqEvent>(new IrqEventComparer());

            // Match IRQ enter with IRQ exit and add to IRQ list
            foreach (PerfDataLinuxEvent linuxEvent in firstPerfDataTxtLogParsed)
            {
                if (linuxEvent.Kind == EventKind.IrqEnter)
                {
                    IrqEvent irqEvent = new IrqEvent();
                    irqEvent.irqEnter = linuxEvent;

                    if (outstandingIrqs.Contains(irqEvent))
                    {
                        outstandingIrqs.Remove(irqEvent);
                    }
                    outstandingIrqs.Add(irqEvent);
                    irqEvents.Add(irqEvent);
                }
                else if (linuxEvent.Kind == EventKind.IrqExit)
                {
                    IrqEvent irqEvent = new IrqEvent();
                    irqEvent.irqExit = linuxEvent;
                    IrqEvent initEvent;

                    if (outstandingIrqs.TryGetValue(irqEvent, out initEvent))
                    {
                        initEvent.irqExit = linuxEvent;
                        outstandingIrqs.Remove(initEvent);
                    }
                    else
                    {
                        irqEvents.Add(irqEvent);
                    }
                }
            }

            if (irqEvents.Count == 0) { return; }

            var baseProjection = Projection.CreateUsingFuncAdaptor(new Func<int, int>(i => i));

            // Constant columns
            var startTimeProjection = baseProjection.Compose(s => new Timestamp(Convert.ToInt64((irqEvents[s].StartTime() - firstTimeStamp) * 1000000)));
            var endTimeProjection = baseProjection.Compose(s => new Timestamp(Convert.ToInt64((irqEvents[s].EndTime() - firstTimeStamp) * 1000000)));
            var durationProjection = baseProjection.Compose(s => irqEvents[s].Duration() * 1000);
            var cpuProjection = baseProjection.Compose(s => irqEvents[s].CpuNumber());
            var countProjection = baseProjection.Compose(s => 1);
            var threadIdProjection = baseProjection.Compose(s => irqEvents[s].irqEnter != null ? irqEvents[s].irqEnter.ThreadID : irqEvents[s].irqExit.ThreadID);
            var processIdProjection = baseProjection.Compose(s => irqEvents[s].irqEnter != null ? irqEvents[s].irqEnter.ProcessID : irqEvents[s].irqExit.ProcessID);
            var processProjection = baseProjection.Compose(s => irqEvents[s].irqEnter != null ? string.Format("{0} ({1})", irqEvents[s].irqEnter.Command, irqEvents[s].irqEnter.ProcessID) : string.Format("{0} ({1})", irqEvents[s].irqExit.Command, irqEvents[s].irqExit.ProcessID));
            var processNameProjection = baseProjection.Compose(s => irqEvents[s].irqEnter != null ? irqEvents[s].irqEnter.Command : irqEvents[s].irqExit.Command);
            var vectorProjection = baseProjection.Compose(s => irqEvents[s].Vector());
            var nameProjection = baseProjection.Compose(s => irqEvents[s].Name());
            var statusProjection = baseProjection.Compose(s => irqEvents[s].Status());

            IProjection<int, int> countProj = SequentialGenerator.Create(
                irqEvents.Count,
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

            var irqsByCpuVectorConfig = new TableConfiguration("IRQs by CPU, Vector")
            {
                Columns = new[]
                {
                    cpuColumn,
                    vectorColumn,
                    nameColumn,
                    TableConfiguration.PivotColumn,
                    startTimeColumn,
                    endTimeColumn,
                    durationColumn,
                    TableConfiguration.GraphColumn,
                    countColumn
                },
            };
            irqsByCpuVectorConfig.AddColumnRole(ColumnRole.StartTime, startTimeColumn);
            irqsByCpuVectorConfig.AddColumnRole(ColumnRole.EndTime, endTimeColumn);
            irqsByCpuVectorConfig.AddColumnRole(ColumnRole.Duration, durationColumn);
            irqsByCpuVectorConfig.AddColumnRole(ColumnRole.ResourceId, vectorColumn);

            //
            //
            //  Use the table builder to build the table. 
            //  Add and set table configuration if applicable.
            //  Then set the row count (we have one row per file) and then add the columns using AddColumn.
            //
            var table = tableBuilder
                .AddTableConfiguration(irqsByCpuVectorConfig)
                .SetDefaultTableConfiguration(irqsByCpuVectorConfig)
                .SetRowCount(irqEvents.Count)
                .AddColumn(startTimeColumn, startTimeProjection)
                .AddColumn(endTimeColumn, endTimeProjection)
                .AddColumn(durationColumn, durationProjection)
                .AddColumn(cpuColumn, cpuProjection)
                .AddColumn(countColumn, countProjection)
                .AddColumn(threadIdColumn, threadIdProjection)
                .AddColumn(processIdColumn, processIdProjection)
                .AddColumn(processColumn, processProjection)
                .AddColumn(processNameColumn, processNameProjection)
                .AddColumn(vectorColumn, vectorProjection)
                .AddColumn(nameColumn, nameProjection)
                .AddColumn(statusColumn, statusProjection)
            ;

            table.AddHierarchicalColumn(initStackColumn, baseProjection.Compose((i) => irqEvents[i].irqEnter != null ? irqEvents[i].irqEnter.stackFrame.stack : new string[] { }), new ArrayAccessProvider<string>());
        }
    }
}
