// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Performance.Toolkit.Engine;
using Microsoft.Performance.Toolkit.Plugins.PerfDataExtension;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using Assembly = System.Reflection.Assembly;

namespace PerfDataUnitTest
{
    [TestClass]
    public class PerfDataUnitTest
    {
        [TestMethod]
        public void ProcessPerfGenericEvents()
        {
            // Input data
            var inputPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            var perfDataPath = Path.Combine(inputPath, "TestData\\perf.data");
            Assert.IsTrue(File.Exists(perfDataPath));

            var pluginPath = Path.GetDirectoryName(typeof(PerfDataProcessingSource).Assembly.Location);
            using var plugins = PluginSet.Load(pluginPath);

            using var dataSources = DataSourceSet.Create(plugins);
            dataSources.AddFile(perfDataPath);

            var createInfo = new EngineCreateInfo(dataSources.AsReadOnly());
            using var engine = Engine.Create(createInfo);
            engine.EnableCooker(PerfDataGenericSourceCooker.DataCookerPath);
            engine.EnableTable(PerfDataGenericEventsTable.TableDescriptor);
            var results = engine.Process();

            var table = results.BuildTable(PerfDataGenericEventsTable.TableDescriptor);
            Assert.AreEqual(1003, table.RowCount);
        }
    }
}
