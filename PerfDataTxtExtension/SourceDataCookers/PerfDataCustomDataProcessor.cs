// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Tracing.Stacks;
using Microsoft.Diagnostics.Tracing.StackSources;
using Microsoft.Performance.SDK.Processing;
using PerfDataExtensions.Tables;
using PerfDataExtensions.DataOutputTypes;
using Microsoft.Diagnostics.Symbols;
using System.Reflection;

namespace PerfDataProcessingSource
{
    public class PerfDataStackCache
    {
        PerfDataStackFrame root;

        public PerfDataStackCache() 
        {
            Microsoft.Diagnostics.Tracing.StackSources.StackFrame dummyFrame = new Microsoft.Diagnostics.Tracing.StackSources.StackFrame("0", "unknown", "unknown");
            root = new PerfDataStackFrame(dummyFrame, new string[] { "unknown!unknown" });
        }

        public PerfDataStackFrame LookupStack(IEnumerable<Frame> stack)
        {
            PerfDataStackFrame curFrame = root;
            IEnumerable<Frame> reverseStack = stack.Reverse();
            List<string> stackFrames = new List<string>();
            string prevModule = null;

            //
            // We implement a stack cache to avoid memory bloat
            //

            foreach (Frame frame in reverseStack)
            {
                if (frame.Kind == FrameKind.StackFrame)
                {
                    Microsoft.Diagnostics.Tracing.StackSources.StackFrame stackFrame = (Microsoft.Diagnostics.Tracing.StackSources.StackFrame)frame;
                    PerfDataStackFrame child;

                    //
                    // Add the frame to the stack, fixing up inlined functions
                    //

                    if (stackFrame.Module == "inlined" && prevModule != null)
                    {
                        stackFrame = new Microsoft.Diagnostics.Tracing.StackSources.StackFrame(stackFrame.Address, prevModule, stackFrame.Symbol);
                    }

                    stackFrames.Add(stackFrame.DisplayName);
                    prevModule = stackFrame.Module;

                    if (curFrame.children.TryGetValue(stackFrame, out child))
                    {
                        curFrame = child;
                    }
                    else
                    {
                        child = new PerfDataStackFrame(stackFrame, stackFrames.ToArray());
                        curFrame.children.Add(stackFrame, child);
                        curFrame = child;
                    }
                }
            }

            return curFrame;
        }
    }

    public sealed class PerfDataCustomDataProcessor
        : CustomDataProcessor
    {
        private readonly string[] filePaths;
        private IReadOnlyDictionary<string, List<PerfDataLinuxEvent>> fileContent;
        private DataSourceInfo dataSourceInfo;
        private PerfDataStackCache stackCache;

        public PerfDataCustomDataProcessor(
           string[] filePaths,
           ProcessorOptions options,
           IApplicationEnvironment applicationEnvironment,
           IProcessorEnvironment processorEnvironment)
            : base(options, applicationEnvironment, processorEnvironment)
        {
            //
            // Assign the files array to a readonly backing field.
            //

            this.filePaths = filePaths;
            this.stackCache = new PerfDataStackCache();
        }

        public override DataSourceInfo GetDataSourceInfo()
        {
            // The DataSourceInfo is used to tell analzyer the time range of the data(if applicable) and any other relevant data for rendering / synchronizing.

            return this.dataSourceInfo;

        }

        protected override Task ProcessAsyncCore(
           IProgress<int> progress,
           CancellationToken cancellationToken)
        {
            var contentDictionary = new Dictionary<string, List<PerfDataLinuxEvent>>();

            foreach (var path in this.filePaths)
            {
                // Hack because perf.data.txt and parsing only includes relative offsets, not absolute time
                // Look for timestamp.txt in the path of the trace and use that as trace start UTC time
                // If it doesn't exist, just use today
                var traceStartTime = DateTime.UtcNow.Date;
                var traceTimeStampStartFile = Path.Combine(Path.GetDirectoryName(path), "timestamp.txt");
                if (File.Exists(traceTimeStampStartFile))
                {
                    string time = File.ReadAllText(traceTimeStampStartFile).Trim();

                    if (!DateTime.TryParse(time, out traceStartTime))
                    {
                        traceStartTime = DateTime.UtcNow.Date; // traceStartTime got overwritten

                        try
                        {
                            traceStartTime = DateTime.ParseExact(time, "ddd MMM d HH:mm:ss yyyy", CultureInfo.InvariantCulture); // "Thu Oct 17 15:37:51 2019" See if this "captured on" date format from "sudo perf report --header-only -i perf.data.merged"
                        }
                        catch (FormatException)
                        {
                            Logger.Error("Could not parse time {0} in file {1}. Format expected is: ddd MMM d HH:mm:ss yyyy", time, traceTimeStampStartFile);
                        }
                    }
                    traceStartTime = DateTime.FromFileTimeUtc(traceStartTime.ToFileTimeUtc());
                }

                LinuxPerfScriptEventParser parser = new LinuxPerfScriptEventParser();
                var events = new List<PerfDataLinuxEvent>();
                foreach (var linuxEvent in parser.ParseSkippingPreamble(path))
                {
                    PerfDataStackFrame stackFrame = stackCache.LookupStack(linuxEvent.CallerStacks);
                    PerfDataLinuxEvent perfDataLinuxEvent = new PerfDataLinuxEvent(linuxEvent, stackFrame);
                    events.Add(perfDataLinuxEvent);
                }

                double duration = 0;
                if (events.Count > 1)
                {
                    duration = (events.Last().TimeMSec - events.First().TimeMSec) * 1000000;
                }

                contentDictionary[path] = events;
                this.dataSourceInfo = new DataSourceInfo(0, (long)duration, traceStartTime);
            }

            this.fileContent = new ReadOnlyDictionary<string, List<PerfDataLinuxEvent>>(contentDictionary);

            return Task.CompletedTask;
        }

        protected override void BuildTableCore(
            TableDescriptor tableDescriptor,
            ITableBuilder tableBuilder)
        {
            //
            // Instantiate the table, and pass the tableBuilder to it.
            //

            var table = this.InstantiateTable(tableDescriptor.Type);
            table.Build(tableBuilder);
        }

        private LinuxPerfScriptTableBase InstantiateTable(Type tableType)
        {
            //
            // This private method is added to activate the given table type and pass in the file content.
            //

            var instance = Activator.CreateInstance(tableType, new[] { this.fileContent, });
            return (LinuxPerfScriptTableBase)instance;
        }
    }
}
