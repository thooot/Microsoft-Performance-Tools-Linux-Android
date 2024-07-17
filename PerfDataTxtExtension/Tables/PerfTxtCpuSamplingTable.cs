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
    //
    // Add a Table attribute in order for the ProcessingSource to understand your table.
    // 

    [Table]              // A category is optional. It useful for grouping different types of tables

    //
    // Have the MetadataTable inherit the TableBase class
    //

    public sealed class PerfTxtCpuSamplingTable
        : LinuxPerfScriptTableBase
    {
        public static readonly TableDescriptor TableDescriptor = new TableDescriptor(
            Guid.Parse("{6F0C68C9-7CB6-4BE0-8440-42746476F158}"),
            "Perf",
            "Perf.data.txt",
            category: "Linux");

        public PerfTxtCpuSamplingTable(IReadOnlyDictionary<string, List<PerfDataLinuxEvent>> parallelLinuxPerfScriptStackSource)
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

        private static readonly ColumnConfiguration sampleNumberColumn = new ColumnConfiguration(
         new ColumnMetadata(new Guid("{43AB800D-1CCC-4D15-AB83-9582372B43B3}"), "SampleNumber", "Sample number"),
         new UIHints { Width = 80 });

        private static readonly ColumnConfiguration timestampColumn = new ColumnConfiguration(
            new ColumnMetadata(new Guid("{F6ABD26E-5F4D-4ABC-9A7B-DA935C1AF216}"), "Timestamp", "The timestamp of the sample"),
            new UIHints { Width = 80 });

        // todo: needs to be changed by user manually to DateTime UTC format. SDK doesn't yet support specifying this <DateTimeTimestampOptionsParameter DateTimeEnabled="true" />
        private static readonly ColumnConfiguration timestampDateTimeColumn = new ColumnConfiguration(
            new ColumnMetadata(new Guid("{F6ABD26E-5F4D-4ABC-9A7B-DA935C1AF216}"), "Event Time", "The timestamp of the sample"),
            new UIHints { Width = 130 });

        private static readonly ColumnConfiguration categoryColumn = new ColumnConfiguration(
            new ColumnMetadata(new Guid("{e3d3c53f-ea0b-449a-ad75-70c0d051ad12}"), "Category", "The type of CPU sample (Regular/Idle/ISR)"),
            new UIHints { Width = 130 });

        private static readonly ColumnConfiguration addressColumn = new ColumnConfiguration(
            new ColumnMetadata(new Guid("{96499680-C05D-46C6-B12D-12D3C1D851AF}"), "Address", "The address of the Instruction Pointer(IP)"),
            new UIHints { Width = 130 });

        private static readonly ColumnConfiguration functionColumn = new ColumnConfiguration(
            new ColumnMetadata(new Guid("{d95e62c7-057e-4d4d-9cbe-1c298198cfaa}"), "Function", "The function of the Instruction Pointer(IP)"),
            new UIHints { Width = 130 });

        private static readonly ColumnConfiguration moduleColumn = new ColumnConfiguration(
            new ColumnMetadata(new Guid("{2DEFEC0D-B004-4285-A824-E82CFC6DE87D}"), "Module", "The module of the Instruction Pointer(IP)"),
            new UIHints { Width = 130 });

        private static readonly ColumnConfiguration countColumn = new ColumnConfiguration(
            new ColumnMetadata(new Guid("{0F25285B-E01B-4A70-807B-F6E37C279AA4}"), "Count", "The count of samples"),
            new UIHints { Width = 130, AggregationMode = AggregationMode.Count });

        private static readonly ColumnConfiguration callStackColumn =
            new ColumnConfiguration(
                new ColumnMetadata(new Guid("{DC2F5EBC-430A-4455-B156-C03A01A5EFD1}"), "Callstack"),
                new UIHints { Width = 80, });

        private static readonly ColumnConfiguration threadIdColumn =
            new ColumnConfiguration(
                new ColumnMetadata(new Guid("{4CDF8D15-0538-433C-BBF1-1F961107C79E}"), "Thread ID"),
                new UIHints { Width = 80, });

        private static readonly ColumnConfiguration processIdColumn =
            new ColumnConfiguration(
                new ColumnMetadata(new Guid("{6c53ad65-ec92-490f-8e6c-156517fb6056}"), "Process ID"),
                new UIHints { Width = 80, });

        private static readonly ColumnConfiguration processColumn =
            new ColumnConfiguration(
                new ColumnMetadata(new Guid("{248F425D-2E91-4D2B-9E5D-98D4C873D810}"), "Process"),
                new UIHints { Width = 80, });

        private static readonly ColumnConfiguration processNameColumn =
            new ColumnConfiguration(
                new ColumnMetadata(new Guid("{1376986c-83c8-4be2-b97a-5f168daffea3}"), "Process Name"),
                new UIHints { Width = 80, });

        private static readonly ColumnConfiguration cpuColumn =
            new ColumnConfiguration(
                new ColumnMetadata(new Guid("{8C6ECE4F-ABE3-4FAD-811F-68FECB770572}"), "CPU"),
                new UIHints { Width = 80, });

        private static readonly ColumnConfiguration weightColumn = new ColumnConfiguration(
            new ColumnMetadata(new Guid("{E4533C53-A84B-4D2F-B674-AD6A6476F431}"), "Weight", "The weight of the sample(s)"),
            new UIHints { Width = 130, CellFormat = TimestampFormatter.FormatMillisecondsGrouped });

        private static readonly ColumnConfiguration weightPctColumn =
            new ColumnConfiguration(
                new ColumnMetadata(new Guid("{1300E2AD-EDB7-4582-BA34-2932F41F574A}"), "Weight %"),
                new UIHints
                {
                    Width = 80,
                    AggregationMode = AggregationMode.Sum,
                    SortPriority = 0,
                    SortOrder = SortOrder.Descending,
                });

        private static readonly ColumnConfiguration startTimeCol =
            new ColumnConfiguration(
                new ColumnMetadata(new Guid("{45B447BF-CD3D-4845-A10B-8742D56671DB}"), "Start Time"),
                new UIHints { Width = 80, });

        private static readonly ColumnConfiguration viewportClippedStartTimeCol =
            new ColumnConfiguration(
                new ColumnMetadata(new Guid("{4EEAF0A7-6D45-449A-ACAC-2DF36DF20910}"), "Clipped Start Time"),
                new UIHints { Width = 80, });

        private static readonly ColumnConfiguration viewportClippedEndTimeCol =
            new ColumnConfiguration(
                new ColumnMetadata(new Guid("{C07C5CD5-0AB5-47C0-AA8C-6545C779087F}"), "Clipped End Time"),
                new UIHints { Width = 80, });

        private static readonly ColumnConfiguration clippedWeightCol =
            new ColumnConfiguration(
                new ColumnMetadata(new Guid("{221F9DDA-8CB2-4647-94F4-654EC6D1AE6D}"), "Clipped Weight"),
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
            List<PerfDataLinuxEvent> profileEvents = new List<PerfDataLinuxEvent>();
            List<string> category = new List<string>();

            const string categoryRegular = "Regular CPU";
            const string categoryISR = "ISR";
            const string categoryIdle = "Idle";

            foreach (PerfDataLinuxEvent linuxEvent in firstPerfDataTxtLogParsed)
            {
                if (linuxEvent.EventName == "cpu-clock")
                {
                    profileEvents.Add(linuxEvent);
                    if (linuxEvent.stackFrame.stack.Contains("kernel.kallsyms!irq_exit"))
                    {
                        category.Add(categoryISR);
                    }
                    else if (linuxEvent.stackFrame.stackFrame.Symbol == "native_safe_halt")
                    {
                        category.Add(categoryIdle);
                    }
                    else
                    {
                        category.Add(categoryRegular);
                    }
                }
            }

            var baseProjection = Projection.CreateUsingFuncAdaptor(new Func<int,int>(i => i));

            // Calculate sample weights
            var sampleWeights = CalculateSampleWeights(profileEvents);

            var oneNs = new TimestampDelta(1);
            var weightProj = baseProjection.Compose(s => new TimestampDelta(Convert.ToInt64(sampleWeights[s] * 1000000)));

            // Constant columns
            var sampleIndex = baseProjection.Compose(s => (long)s);
            var timeStampProjection = baseProjection.Compose(s => new Timestamp(Convert.ToInt64((profileEvents[s].TimeMSec - firstTimeStamp) * 1000000)));
            var cpuProjection = baseProjection.Compose(s => profileEvents[s].CpuNumber);
            var countProjection = baseProjection.Compose(s => 1);
            var categoryProjection = baseProjection.Compose(s => (category[s]));
            var ipAddressProjection = baseProjection.Compose(s => (profileEvents[s]).stackFrame.stackFrame.Address);
            var ipFunctionProjection = baseProjection.Compose(s => (profileEvents[s]).stackFrame.stackFrame.Symbol);
            var ipModuleProjection = baseProjection.Compose(s => (profileEvents[s]).stackFrame.stackFrame.Module);
            var threadIdProjection = baseProjection.Compose(s => profileEvents[s].ThreadID);
            var processIdProjection = baseProjection.Compose(s => profileEvents[s].ProcessID);
            var processProjection = baseProjection.Compose(s => string.Format("{0} ({1})", profileEvents[s].Command, profileEvents[s].ProcessID));
            var processNameProjection = baseProjection.Compose(s => profileEvents[s].Command);


            // For calculating cpu %
            var timeStampStartProjection = baseProjection.Compose(s => new Timestamp(Convert.ToInt64(profileEvents[s].TimeMSec - firstTimeStamp) * 1000000) - new TimestampDelta(Convert.ToInt64(sampleWeights[s] * 1000000)));
            IProjection<int, Timestamp> viewportClippedStartTimeProj = Projection.ClipTimeToVisibleDomain.Create(timeStampStartProjection);
            IProjection<int, Timestamp> viewportClippedEndTimeProj = Projection.ClipTimeToVisibleDomain.Create(timeStampProjection);

            IProjection<int, TimestampDelta> clippedWeightProj = Projection.Select(
                viewportClippedEndTimeProj,
                viewportClippedStartTimeProj,
                new ReduceTimeSinceLastDiff());

            IProjection<int, double> weightPercentProj = Projection.VisibleDomainRelativePercent.Create(clippedWeightProj);

            IProjection<int, int> countProj = SequentialGenerator.Create(
                profileEvents.Count,
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

            const string filterIdleSamplesQuery = "[Function]:=\"native_safe_halt\"";

            var utilByCpuStackConfig = new TableConfiguration("Utilization by CPU, Stack")
            {
                Columns = new[]
              {
                    cpuColumn,
                    callStackColumn,
                    TableConfiguration.PivotColumn,
                    processColumn,
                    threadIdColumn,
                    functionColumn,
                    moduleColumn,
                    sampleNumberColumn,
                    timestampDateTimeColumn,
                    timestampColumn,
                    weightColumn,
                    countColumn,
                    TableConfiguration.GraphColumn,
                    weightPctColumn

                },
                InitialFilterShouldKeep = false,
                InitialFilterQuery = filterIdleSamplesQuery,
            };
            utilByCpuStackConfig.AddColumnRole(ColumnRole.EndTime, timestampColumn);
            utilByCpuStackConfig.AddColumnRole(ColumnRole.Duration, weightColumn);
            utilByCpuStackConfig.AddColumnRole(ColumnRole.ResourceId, cpuColumn);

            var utilByCpuConfig = new TableConfiguration("Utilization by CPU")
            {
                Columns = new[]
              {
                    cpuColumn,
                    TableConfiguration.PivotColumn,
                    countColumn,
                    callStackColumn,
                    processColumn,
                    threadIdColumn,
                    functionColumn,
                    moduleColumn,
                    sampleNumberColumn,
                    timestampDateTimeColumn,
                    timestampColumn,
                    weightColumn,
                    TableConfiguration.GraphColumn,
                    weightPctColumn

                },
                InitialFilterShouldKeep = false,
                InitialFilterQuery = filterIdleSamplesQuery,
            };
            utilByCpuConfig.AddColumnRole(ColumnRole.EndTime, timestampColumn);
            utilByCpuConfig.AddColumnRole(ColumnRole.Duration, weightColumn);
            utilByCpuConfig.AddColumnRole(ColumnRole.ResourceId, cpuColumn);

            var utilByProcessConfig = new TableConfiguration("Utilization by Process")
            {
                Columns = new[]
              {
                    processColumn,
                    TableConfiguration.PivotColumn,
                    countColumn,
                    callStackColumn,
                    cpuColumn,
                    threadIdColumn,
                    functionColumn,
                    moduleColumn,
                    sampleNumberColumn,
                    timestampDateTimeColumn,
                    timestampColumn,
                    weightColumn,
                    TableConfiguration.GraphColumn,
                    weightPctColumn

                },
                InitialFilterShouldKeep = false,
                InitialFilterQuery = filterIdleSamplesQuery,
            };
            utilByProcessConfig.AddColumnRole(ColumnRole.EndTime, timestampColumn);
            utilByProcessConfig.AddColumnRole(ColumnRole.Duration, weightColumn);
            utilByProcessConfig.AddColumnRole(ColumnRole.ResourceId, cpuColumn);

            var utilByProcessStackConfig = new TableConfiguration("Utilization by Process, Stack")
            {
                Columns = new[]
              {
                    processColumn,
                    callStackColumn,
                    TableConfiguration.PivotColumn,
                    countColumn,
                    cpuColumn,
                    threadIdColumn,
                    functionColumn,
                    moduleColumn,
                    sampleNumberColumn,
                    timestampDateTimeColumn,
                    timestampColumn,
                    weightColumn,
                    TableConfiguration.GraphColumn,
                    weightPctColumn

                },
                InitialFilterShouldKeep = false,
                InitialFilterQuery = filterIdleSamplesQuery,
            };
            utilByProcessStackConfig.AddColumnRole(ColumnRole.EndTime, timestampColumn);
            utilByProcessStackConfig.AddColumnRole(ColumnRole.Duration, weightColumn);
            utilByProcessStackConfig.AddColumnRole(ColumnRole.ResourceId, cpuColumn);

            var flameByProcessStackConfig = new TableConfiguration("Flame by Process, Stack")
            {
                Columns = new[]
              {
                    processColumn,
                    callStackColumn,
                    TableConfiguration.PivotColumn,
                    countColumn,
                    cpuColumn,
                    threadIdColumn,
                    functionColumn,
                    moduleColumn,
                    sampleNumberColumn,
                    timestampDateTimeColumn,
                    timestampColumn,
                    weightColumn,
                    TableConfiguration.GraphColumn,
                    weightPctColumn

                },
                ChartType = ChartType.Flame,
                InitialFilterShouldKeep = false,
                InitialFilterQuery = filterIdleSamplesQuery,
            };
            flameByProcessStackConfig.AddColumnRole(ColumnRole.EndTime, timestampColumn);
            flameByProcessStackConfig.AddColumnRole(ColumnRole.Duration, weightColumn);
            flameByProcessStackConfig.AddColumnRole(ColumnRole.ResourceId, cpuColumn);

            //
            //
            //  Use the table builder to build the table. 
            //  Add and set table configuration if applicable.
            //  Then set the row count (we have one row per file) and then add the columns using AddColumn.
            //
            var table = tableBuilder
            .AddTableConfiguration(utilByCpuStackConfig)
                .SetDefaultTableConfiguration(utilByCpuStackConfig)
                .AddTableConfiguration(utilByCpuConfig)
                .AddTableConfiguration(utilByProcessConfig)
                .AddTableConfiguration(utilByProcessStackConfig)
                .AddTableConfiguration(flameByProcessStackConfig)
                .SetRowCount(profileEvents.Count)
                .AddColumn(sampleNumberColumn, sampleIndex)
                .AddColumn(timestampColumn, timeStampProjection)
                .AddColumn(categoryColumn, categoryProjection)
                .AddColumn(functionColumn, ipFunctionProjection)
                .AddColumn(moduleColumn, ipModuleProjection)
                .AddColumn(addressColumn, ipAddressProjection)
                .AddColumn(countColumn, countProjection)
                .AddColumn(weightColumn, weightProj)
                .AddColumn(threadIdColumn, threadIdProjection)
                .AddColumn(processIdColumn, processIdProjection)
                .AddColumn(processColumn, processProjection)
                .AddColumn(processNameColumn, processNameProjection)
                .AddColumn(weightPctColumn, weightPercentProj)
                .AddColumn(startTimeCol, timeStampStartProjection)
                .AddColumn(viewportClippedStartTimeCol, viewportClippedStartTimeProj)
                .AddColumn(viewportClippedEndTimeCol, viewportClippedEndTimeProj)
                .AddColumn(clippedWeightCol, clippedWeightProj)
                .AddColumn(cpuColumn, cpuProjection)
            ;

            table.AddHierarchicalColumn(callStackColumn, baseProjection.Compose((i) => (profileEvents[i]).stackFrame.stack), new ArrayAccessProvider<string>());

        }

        private Dictionary<int, double> CalculateSampleWeights(List<PerfDataLinuxEvent> linuxEvents)
        {
            var sampleWeights = new Dictionary<int, double>();

            const int MaxCpus = 256;
            var lastPerCpuSampleWeight = new Tuple<int, double>[MaxCpus]; // Per CPU - Last sample #, TimeRelativeMSec

            for (var i = 0; i < linuxEvents.Count; i++)
            {
                var linuxEvent = linuxEvents[i];
                var prevSampleTimeRelativeMSec = lastPerCpuSampleWeight[linuxEvent.CpuNumber];

                if (prevSampleTimeRelativeMSec != null)
                {
                    var weightOfLastSampleOnCpu = linuxEvent.TimeMSec - prevSampleTimeRelativeMSec.Item2;
                    sampleWeights.Add(prevSampleTimeRelativeMSec.Item1, weightOfLastSampleOnCpu);    // Weight is duration for the prev samp
                }
                lastPerCpuSampleWeight[linuxEvent.CpuNumber] = new Tuple<int, double>(i, linuxEvent.TimeMSec);
            }


            var medSampleWeight = Median(sampleWeights);
            // Now there will be samples at the end that don't have a weight
            foreach (var cpuSample in lastPerCpuSampleWeight)
            {
                if (cpuSample != null)
                {
                    sampleWeights.Add(cpuSample.Item1, medSampleWeight);
                }
            }

            // Now the first samples weights won't be correct since there was no previous sample to compare to. Fix them up
            // Find samples that are 1/2 big as median OR 1.3x as big (being in a VM) and set to median
            var unusualSamples = sampleWeights.Where(f => f.Value <= medSampleWeight / 2 || f.Value >= medSampleWeight * 1.3).ToList();

            for (int i = 0; i < unusualSamples.Count(); i++)
            {
                sampleWeights[unusualSamples[i].Key] = medSampleWeight;
            }

            return sampleWeights;
        }

        private double Median(Dictionary<int, double> list)
        {
            int numberCount = list.Count;
            int halfIndex = numberCount / 2;
            var sortedNumbers = list.Values.OrderBy(n => n);
            double median;
            if ((numberCount % 2) == 0)
            {
                median = ((sortedNumbers.ElementAt(halfIndex) +
                    sortedNumbers.ElementAt((halfIndex - 1))) / 2);
            }
            else
            {
                median = sortedNumbers.ElementAt(halfIndex);
            }

            return median;
        }
    }
}
