using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MigrationTools.Enrichers;
using MigrationTools.Tests;


namespace MigrationTools.ProcessorEnrichers.Tests
{
    class MappedNodeNameRequest
    {
        public string sourceNodeName;
        public string sourceStructureName;

        public string targetProjectName;
        public string targetStructureName;

        public string expectedNodeName;
    }


    [TestClass()]
    public class TfsNodeStructureTests
    {
        private ServiceProvider _services;

        [TestInitialize]
        public void Setup()
        {
            _services = ServiceProviderHelper.GetServices();
        }

        [TestMethod(), TestCategory("L0"), TestCategory("AzureDevOps.ObjectModel")]
        public void GetTfsNodeStructure_WithDifferentAreaPath()
        {
            var nodeStructure = _services.GetRequiredService<TfsNodeStructure>();

            const string targetStructureName = "Area\\test";
            const string sourceStructureName = "Area";

            nodeStructure.ApplySettings(new TfsNodeStructureSettings
            {
                SourceProjectName = "SourceProject",
                TargetProjectName = "TargetProject",
                FoundNodes = new Dictionary<string, bool>
                {
                    { @"TargetProject\Area\test\PUL", true }
                }
            });

            const string sourceNodeName = @"SourceProject\PUL";
            const TfsNodeStructureType nodeStructureType = TfsNodeStructureType.Area;


            var newNodeName = nodeStructure.GetNewNodeName(sourceNodeName, nodeStructureType, targetStructureName, sourceStructureName);

            Assert.AreEqual(newNodeName, @"TargetProject\test\PUL");
        }

        [TestMethod(), TestCategory("L0"), TestCategory("AzureDevOps.ObjectModel")]
        public void MappedNodeName_WithDifferentMappings()
        {
            var requests = new List<MappedNodeNameRequest>
            {
                new()
                {
                    sourceNodeName = @"PartsUnlimited\testarea",
                    sourceStructureName = @"Area\testarea",

                    targetProjectName = "Test Migration Project",
                    targetStructureName = @"Area\Migrated testarea",

                    expectedNodeName = @"Test Migration Project\Migrated testarea"
                },
                new()
                {
                    sourceNodeName = @"PartsUnlimited\456",
                    sourceStructureName = @"Area\456",

                    targetProjectName = "Test Migration Project",
                    targetStructureName = @"Area\team\place\123",

                    expectedNodeName = @"Test Migration Project\team\place\123"
                },
                new()
                {
                    sourceNodeName = @"PartsUnlimited\source",
                    sourceStructureName = @"Area\source",

                    targetProjectName = "Test Migration Project",
                    targetStructureName = @"Area\team\place\source",

                    expectedNodeName = @"Test Migration Project\team\place\source"
                },
                new()
                {
                    sourceNodeName = @"PartsUnlimited",
                    sourceStructureName = @"Area\123",

                    targetProjectName = "Test Migration Project",
                    targetStructureName = @"Area\team\place\source",

                    expectedNodeName = @"Test Migration Project\team\place\source"
                }
            };

            foreach (var request in requests)
            {
                var result = TfsNodeStructure.MappedNodeName(
                    request.sourceNodeName,
                    request.sourceStructureName,
                    request.targetProjectName,
                    request.targetStructureName
                );

                Assert.AreEqual(request.expectedNodeName, result);
            }
        }

    }
}