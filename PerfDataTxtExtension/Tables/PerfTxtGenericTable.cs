// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Diagnostics.Tracing.Stacks;
using Microsoft.Diagnostics.Tracing.StackSources;
using Microsoft.Performance.SDK;
using Microsoft.Performance.SDK.Processing;
using PerfDataExtensions.DataOutputTypes;
using PerfDataExtensions.Tables.Generators;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Utilities.AccessProviders;
using static PerfDataExtensions.Tables.TimeHelper;

namespace PerfDataExtensions.Tables
{
    //
    // Add a Table attribute in order for the ProcessingSource to understand your table.
    // 

    [Table]              // A category is optional. It useful for grouping different types of tables

    //
    // Have the MetadataTable inherit the TableBase class
    //

    public sealed class PerfTxtGenericTable
        : LinuxPerfScriptTableBase
    {
        public static readonly TableDescriptor TableDescriptor = new TableDescriptor(
            Guid.Parse("{b6d7dfb9-eb10-42a0-9041-061625530b18}"),
            "Generic Events",
            "Generic Events Table",
            category: "Linux");

        public PerfTxtGenericTable(IReadOnlyDictionary<string, List<PerfDataLinuxEvent>> parallelLinuxPerfScriptStackSource)
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

        private static readonly ColumnConfiguration timestampColumn = new ColumnConfiguration(
            new ColumnMetadata(new Guid("{588d3488-3cb6-41ef-be87-ee5525edc571}"), "Timestamp", "The timestamp of the sample"),
            new UIHints { Width = 80 });

        private static readonly ColumnConfiguration addressColumn = new ColumnConfiguration(
            new ColumnMetadata(new Guid("{611886c7-9b67-4ccc-b3ac-98f103f33e0a}"), "Address", "The address of the Instruction Pointer(IP)"),
            new UIHints { Width = 130 });

        private static readonly ColumnConfiguration functionColumn = new ColumnConfiguration(
            new ColumnMetadata(new Guid("{379cc4e7-5838-40b6-a18d-2ccf1dd888dc}"), "Function", "The function of the Instruction Pointer(IP)"),
            new UIHints { Width = 130 });

        private static readonly ColumnConfiguration moduleColumn = new ColumnConfiguration(
            new ColumnMetadata(new Guid("{a4d9fc8f-a61b-4d45-9655-c7ea72b51dcf}"), "Module", "The module of the Instruction Pointer(IP)"),
            new UIHints { Width = 130 });

        private static readonly ColumnConfiguration countColumn = new ColumnConfiguration(
            new ColumnMetadata(new Guid("{ff1ca718-beef-4765-aa30-3d60bcf6ad9d}"), "Count", "The count of samples"),
            new UIHints { Width = 130, AggregationMode = AggregationMode.Count });

        private static readonly ColumnConfiguration callStackColumn =
            new ColumnConfiguration(
                new ColumnMetadata(new Guid("{3a6c93e3-0ec2-46b6-96b2-ff38779e007a}"), "Callstack"),
                new UIHints { Width = 80, });

        private static readonly ColumnConfiguration threadIdColumn =
            new ColumnConfiguration(
                new ColumnMetadata(new Guid("{cfef0f06-0175-473a-b118-69171aa1c1d8}"), "Thread ID"),
                new UIHints { Width = 80, });

        private static readonly ColumnConfiguration processIdColumn =
            new ColumnConfiguration(
                new ColumnMetadata(new Guid("{3e645875-6753-41e6-bd5f-b6042c2b19e1}"), "Process ID"),
                new UIHints { Width = 80, });

        private static readonly ColumnConfiguration processColumn =
            new ColumnConfiguration(
                new ColumnMetadata(new Guid("{da3ec4dc-e186-409f-9299-efa7007116ac}"), "Process"),
                new UIHints { Width = 80, });

        private static readonly ColumnConfiguration processNameColumn =
            new ColumnConfiguration(
                new ColumnMetadata(new Guid("{88550a63-f5d5-4345-8543-a45c8b2b10ad}"), "Process Name"),
                new UIHints { Width = 80, });

        private static readonly ColumnConfiguration cpuColumn =
            new ColumnConfiguration(
                new ColumnMetadata(new Guid("{8c391643-fd18-4e6e-9dcc-70e9ad2a8bda}"), "CPU"),
                new UIHints { Width = 80, });

        private static readonly ColumnConfiguration eventTypeColumn =
            new ColumnConfiguration(
                new ColumnMetadata(new Guid("{0d57265b-e460-485f-8096-387f2460db7a}"), "Event Type"),
                new UIHints { Width = 80, });

        private static readonly ColumnConfiguration detailsColumn =
            new ColumnConfiguration(
                new ColumnMetadata(new Guid("{5b287e87-9742-431b-9933-94639bc7e42a}"), "Details"),
                new UIHints { Width = 80, });

        public override void Build(ITableBuilder tableBuilder)
        {
            if (PerfDataTxtLogParsed == null || PerfDataTxtLogParsed.Count == 0)
            {
                return;
            }

            var firstPerfDataTxtLogParsed = PerfDataTxtLogParsed.First().Value;  // First Log
            double firstTimeStamp = 0;

            if (firstPerfDataTxtLogParsed.Count > 0)
            {
                firstTimeStamp = firstPerfDataTxtLogParsed[0].TimeMSec;
            }

            // Init
            var baseProjection = Projection.Index(firstPerfDataTxtLogParsed);

            // Constant columns
            var timeStampProjection = baseProjection.Compose(s => new Timestamp(Convert.ToInt64((s.TimeMSec - firstTimeStamp) * 1000000)));
            var cpuProjection = baseProjection.Compose(s => s.CpuNumber);
            var countProjection = baseProjection.Compose(s => 1);
            var ipAddressProjection = baseProjection.Compose(s => s.stackFrame.stackFrame.Address);
            var ipFunctionProjection = baseProjection.Compose(s => s.stackFrame.stackFrame.Symbol);
            var ipModuleProjection = baseProjection.Compose(s => s.stackFrame.stackFrame.Module);
            var threadIdProjection = baseProjection.Compose(s => s.ThreadID);
            var processIdProjection = baseProjection.Compose(s => s.ProcessID);
            var processProjection = baseProjection.Compose(s => string.Format("{0} ({1})", s.Command, s.ProcessID));
            var processNameProjection = baseProjection.Compose(s => s.Command);
            var eventTypeProjection = baseProjection.Compose(s => s.EventName);
            var detailProjection = baseProjection.Compose(s => s.EventProperty);

            //
            // Table Configurations describe how your table should be presented to the user: 
            // the columns to show, what order to show them, which columns to aggregate, and which columns to graph. 
            // You may provide a number of columns in your table, but only want to show a subset of them by default so as not to overwhelm the user. 
            // The user can still open the table properties to turn on or off columns.
            // The table configuration class also exposes four (4) columns UI explicitly recognizes: Pivot Column, Graph Column, Left Freeze Column, Right Freeze Column
            // For more information about what these columns do, go to "Advanced Topics" -> "Table Configuration" in our Wiki. Link can be found in README.md
            //

            var genericByTypeProcessThreadConfig = new TableConfiguration("Generic Events by Type, Process, Thread")
            {
                Columns = new[]
              {
                    eventTypeColumn,
                    processColumn,
                    threadIdColumn,
                    TableConfiguration.PivotColumn,
                    cpuColumn,
                    timestampColumn,
                    detailsColumn,
                    callStackColumn,
                    TableConfiguration.GraphColumn,
                    countColumn

                }
            };
            genericByTypeProcessThreadConfig.AddColumnRole(ColumnRole.StartTime, timestampColumn);
            genericByTypeProcessThreadConfig.AddColumnRole(ColumnRole.ResourceId, cpuColumn);

            //
            //
            //  Use the table builder to build the table. 
            //  Add and set table configuration if applicable.
            //  Then set the row count (we have one row per file) and then add the columns using AddColumn.
            //
            var table = tableBuilder
            .AddTableConfiguration(genericByTypeProcessThreadConfig)
                .SetDefaultTableConfiguration(genericByTypeProcessThreadConfig)
                .SetRowCount(firstPerfDataTxtLogParsed.Count)
                .AddColumn(timestampColumn, timeStampProjection)
                .AddColumn(functionColumn, ipFunctionProjection)
                .AddColumn(moduleColumn, ipModuleProjection)
                .AddColumn(addressColumn, ipAddressProjection)
                .AddColumn(countColumn, countProjection)
                .AddColumn(threadIdColumn, threadIdProjection)
                .AddColumn(processIdColumn, processIdProjection)
                .AddColumn(processColumn, processProjection)
                .AddColumn(processNameColumn, processNameProjection)
                .AddColumn(eventTypeColumn, eventTypeProjection)
                .AddColumn(detailsColumn, detailProjection)
                .AddColumn(cpuColumn, cpuProjection)
            ;

            table.AddHierarchicalColumn(callStackColumn, baseProjection.Compose(s => s.stackFrame.stack), new ArrayAccessProvider<string>());

        }
    }
}
