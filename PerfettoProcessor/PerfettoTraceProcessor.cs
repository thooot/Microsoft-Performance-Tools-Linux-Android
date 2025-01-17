﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using Google.Protobuf;
using Google.Protobuf.Collections;
using Perfetto.Protos;
using System;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;

namespace PerfettoProcessor
{
    /// <summary>
    /// Responsible for managing trace_processor_shell.exe. The shell executable loads the Perfetto trace and allows for SQL querying
    /// through HTTP on localhost. HTTP communication happens with protobuf objects/buffers.
    /// </summary>
    public class PerfettoTraceProcessor
    {
        private Process ShellProcess;

        // Default starting port
        private int HttpPort = 22222;

        // Highest ports can go
        private const int PortMax = 65535;

        // HTTP request denied takes about 3 seconds so time out after about 5 minutes
        private const int MaxRetryLimit = 100;

        /// <summary>
        /// Sends an RPC request to trace_processor_shell. This utilizies the bidirectional pipe that exists with the /rpc endpoint.
        /// TraceProcessorRpcStream objects get passed back and forth
        /// </summary>
        /// <param name="rpc">RPC stream object that contains the request</param>
        /// <returns>RPC stream object that contains the response</returns>
        private TraceProcessorRpcStream SendRpcRequest(TraceProcessorRpc rpc)
        {
            TraceProcessorRpcStream rpcStream = new TraceProcessorRpcStream();
            rpcStream.Msg.Add(rpc);

            TraceProcessorRpcStream returnStream = null;
            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("Connection", "keep-alive");
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/x-protobuf")); //ACCEPT header

                HttpContent sc = new ByteArrayContent(rpcStream.ToByteArray());

                var response = client.PostAsync($"http://localhost:{HttpPort}/rpc", sc).GetAwaiter().GetResult();
                var byteArray = response.Content.ReadAsByteArrayAsync().Result;
                returnStream = TraceProcessorRpcStream.Parser.ParseFrom(byteArray);
            }
            if (returnStream == null)
            {
                throw new Exception("Problem with the RPC stream returned from trace_processor_shell.exe");
            }
            return returnStream;
        }

        /// <summary>
        /// Check if another instance of trace_processor_shell is running on this port
        /// </summary>
        /// <param name="port"></param>
        /// <returns></returns>
        private bool IsPortAvailable(int port)
        {
            try
            {
                TraceProcessorRpc rpc = new TraceProcessorRpc
                {
                    Request = TraceProcessorRpc.Types.TraceProcessorMethod.TpmGetStatus
                };

                // Send the request
                var rpcResult = SendRpcRequest(rpc);

                if (rpcResult.Msg.Count != 1 || rpcResult.Msg[0].Status == null)
                {
                    throw new Exception("Invalid RPC stream result from trace_processor_shell");
                }
                else
                {
                    // If the response contains Perfetto, trace_processor_shell is already running on this port
                    return !rpcResult.Msg[0].Status.HumanReadableVersion.Contains("Perfetto");
                }
            }
            catch (HttpRequestException)
            {
                // Exception here is the connection refused message, meaning the RPC server has not been initialized
                // Which means this port is not being used by another trace_processor_shell
                return true;
            }
        }

        /// <summary>
        /// Initializes trace_processor_shell.exe in HTTP/RPC mode with the trace file
        /// </summary>
        /// <param name="shellPath">Full path to trace_processor_shell.exe</param>
        /// <param name="tracePath">Full path to the Perfetto trace file</param>
        public void OpenTraceProcessor(string shellPath, string tracePath, DataReceivedEventHandler outputDataReceivedEventHandler, DataReceivedEventHandler errorDataReceivedEventHandler)
        {
            StartTraceProcessor(shellPath, $"-D --http-port {HttpPort} -i \"{tracePath}\"", outputDataReceivedEventHandler, errorDataReceivedEventHandler);
        }

        /// <summary>
        /// Initializes trace_processor_shell.exe in HTTP/RPC mode without the trace file
        /// </summary>
        /// <param name="shellPath">Full path to trace_processor_shell.exe</param>
        public void OpenTraceProcessor(string shellPath, DataReceivedEventHandler outputDataReceivedEventHandler, DataReceivedEventHandler errorDataReceivedEventHandler)
        {
            StartTraceProcessor(shellPath, $"-D --http-port {HttpPort}", outputDataReceivedEventHandler, errorDataReceivedEventHandler);
        }

        /// <summary>
        /// Launches trace_processor_shell.exe
        /// </summary>
        /// <param name="shellPath">Full path to trace_processor_shell.exe</param>
        /// <param name="args"></param>
        private void StartTraceProcessor(string shellPath, string args, DataReceivedEventHandler outputDataReceivedEventHandler, DataReceivedEventHandler errorDataReceivedEventHandler)
        {
            using (var client = new HttpClient())
            {
                // Make sure another instance of trace_processor_shell isn't already running
                while (!IsPortAvailable(HttpPort) && HttpPort < PortMax)
                {
                    HttpPort++;
                }

                var traceProcessorProcessStartInfo = new ProcessStartInfo()
                {
                    FileName = shellPath,
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };

                ShellProcess = new Process
                {
                    StartInfo = traceProcessorProcessStartInfo
                };

                if (outputDataReceivedEventHandler != null)
                {
                    ShellProcess.OutputDataReceived += outputDataReceivedEventHandler;
                }
                if (errorDataReceivedEventHandler != null)
                {
                    ShellProcess.ErrorDataReceived += errorDataReceivedEventHandler;
                }

                ShellProcess.Start();

                if (outputDataReceivedEventHandler != null)
                {
                    ShellProcess.BeginOutputReadLine();
                }
                if (errorDataReceivedEventHandler != null)
                {
                    ShellProcess.BeginErrorReadLine();
                }
            }
            if (ShellProcess.HasExited)
            {
                throw new Exception("Problem starting trace_processor_shell.exe");
            }
        }

        /// <summary>
        /// Check if the trace_processor_shell has a loaded trace. Assumes the shell executable is already running. Will throw otherwise.
        /// </summary>
        /// <returns></returns>
        private bool CheckIfTraceIsLoaded()
        {
            if (ShellProcess == null || ShellProcess.HasExited)
            {
                throw new Exception("The trace_process_shell is not running");
            }

            try
            {
                TraceProcessorRpc rpc = new TraceProcessorRpc
                {
                    Request = TraceProcessorRpc.Types.TraceProcessorMethod.TpmGetStatus
                };

                // Send the request
                var rpcResult = SendRpcRequest(rpc);

                if (rpcResult.Msg.Count != 1 || rpcResult.Msg[0].Status == null)
                {
                    throw new Exception("Invalid RPC stream result from trace_processor_shell");
                }
                else
                {
                    return rpcResult.Msg[0].Status.HasLoadedTraceName;
                }
            }
            catch (HttpRequestException)
            {
                // Exception here is the connection refused message, meaning the RPC server has not been initialized
                return false;
            }

        }

        /// <summary>
        /// Perform a SQL query against trace_processor_shell to gather Perfetto trace data.
        /// </summary>
        /// <param name="sqlQuery">The query to perform against the loaded trace in trace_processor_shell</param>
        /// <returns>List of RPC objects that contain the QueryResults</returns>
        public RepeatedField<TraceProcessorRpc> QueryTrace(string sqlQuery)
        {
            // Make sure ShellProcess is running
            if (ShellProcess == null || ShellProcess.HasExited)
            {
                throw new Exception("The trace_process_shell is not running");
            }

            int cnt = 0;
            // Check if the trace is loaded
            // We know the shell is running, so the trace could still be in the loading process. Give it a little while to finish loading
            // before we error out.
            while (!CheckIfTraceIsLoaded())
            {
                if (cnt++ > MaxRetryLimit)
                {
                    throw new Exception("Unable to query Perfetto trace because trace_processor_shell.exe does not appear to have loaded it");
                }
            }

            TraceProcessorRpc rpc = new TraceProcessorRpc
            {
                Request = TraceProcessorRpc.Types.TraceProcessorMethod.TpmQueryStreaming,
                QueryArgs = new QueryArgs()
            };
            rpc.QueryArgs.SqlQuery = sqlQuery;
            var rpcResult = SendRpcRequest(rpc);

            return rpcResult.Msg;
        }

        /// <summary>
        /// Perform a SQL query against trace_processor_shell to gather Perfetto trace data. Processes the QueryResult
        /// and returns PerfettoSqlObjects through a callback
        /// </summary>
        /// <param name="sqlQuery">The query to perform against the loaded trace in trace_processor_shell</param>
        /// <param name="eventKey">The event key that corresponds to the type of PerfettoSqlEvent to process for this query</param>
        /// <param name="eventCallback">Completed PerfettoSqlEvents will be sent here</param>
        /// <returns>Count of rows from query result</returns>
        public long QueryTraceForEvents(string sqlQuery, string eventKey, Action<PerfettoSqlEvent> eventCallback)
        {
            long eventCount = 0;
            var rpcs = QueryTrace(sqlQuery);

            if (rpcs.Count == 0)
            {
                return eventCount;
            }

            // Column information is only available in first result
            var numColumns = rpcs[0].QueryResult.ColumnNames.Count;
            var cols = rpcs[0].QueryResult.ColumnNames;

            foreach (var rpc in rpcs)
            {
                var qr = rpc.QueryResult;

                foreach (var batch in qr.Batch)
                {
                    CellCounters cellCounters = new CellCounters();

                    // String cells get stored as a single string delimited by null character. Split that up ourselves
                    var stringCells = batch.StringCells.Split('\0');

                    int cellCount = 0;
                    PerfettoSqlEvent ev = null;
                    foreach (var cell in batch.Cells)
                    {
                        if (ev == null)
                        {
                            ev = eventKey switch
                            {
                                PerfettoSliceEvent.Key => new PerfettoSliceEvent(),
                                PerfettoArgEvent.Key => new PerfettoArgEvent(),
                                PerfettoThreadTrackEvent.Key => new PerfettoThreadTrackEvent(),
                                PerfettoThreadEvent.Key => new PerfettoThreadEvent(),
                                PerfettoProcessRawEvent.Key => new PerfettoProcessRawEvent(),
                                PerfettoSchedSliceEvent.Key => new PerfettoSchedSliceEvent(),
                                PerfettoAndroidLogEvent.Key => new PerfettoAndroidLogEvent(),
                                PerfettoRawEvent.Key => new PerfettoRawEvent(),
                                PerfettoCounterEvent.Key => new PerfettoCounterEvent(),
                                PerfettoCpuCounterTrackEvent.Key => new PerfettoCpuCounterTrackEvent(),
                                PerfettoGpuCounterTrackEvent.Key => new PerfettoGpuCounterTrackEvent(),
                                PerfettoClockSnapshotEvent.Key => new PerfettoClockSnapshotEvent(),
                                PerfettoTraceBoundsEvent.Key => new PerfettoTraceBoundsEvent(),
                                PerfettoMetadataEvent.Key => new PerfettoMetadataEvent(),
                                PerfettoProcessCounterTrackEvent.Key => new PerfettoProcessCounterTrackEvent(),
                                PerfettoCounterTrackEvent.Key => new PerfettoCounterTrackEvent(),
                                PerfettoProcessTrackEvent.Key => new PerfettoProcessTrackEvent(),
                                PerfettoPerfSampleEvent.Key => new PerfettoPerfSampleEvent(),
                                PerfettoStackProfileCallSiteEvent.Key => new PerfettoStackProfileCallSiteEvent(),
                                PerfettoStackProfileFrameEvent.Key => new PerfettoStackProfileFrameEvent(),
                                PerfettoStackProfileMappingEvent.Key => new PerfettoStackProfileMappingEvent(),
                                PerfettoStackProfileSymbolEvent.Key => new PerfettoStackProfileSymbolEvent(),
                                PerfettoActualFrameEvent.Key => new PerfettoActualFrameEvent(),
                                PerfettoExpectedFrameEvent.Key => new PerfettoExpectedFrameEvent(),
                                PerfettoPackageListEvent.Key => new PerfettoPackageListEvent(),
                                _ => throw new Exception("Invalid event type"),
                            };
                        }

                        var colIndex = cellCount % numColumns;
                        var colName = cols[colIndex].ToLower();

                        // The event itself is responsible for figuring out how to process and store cell contents
                        ev.ProcessCell(colName, cell, batch, stringCells, cellCounters);

                        // If we've reached the end of a row, we've finished an event.
                        if (++cellCount % numColumns == 0)
                        {
                            // Report the event back
                            eventCallback(ev);

                            ev = null;
                            eventCount++;
                        }
                    }
                }
            }
            return eventCount;
        }

        public void CloseTraceConnection()
        {
            ShellProcess.CancelOutputRead();
            ShellProcess.CancelErrorRead();
            ShellProcess?.Kill();
        }
    }
}
