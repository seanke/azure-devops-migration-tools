using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.TeamFoundation.Server;
using MigrationTools._EngineV1.Clients;
using MigrationTools.DataContracts;
using MigrationTools.Endpoints;
using MigrationTools.Processors;

namespace MigrationTools.Enrichers
{
    public enum TfsNodeStructureType
    {
        Area,
        Iteration
    }

    public struct TfsNodeStructureSettings
    {
        public string SourceProjectName;
        public string TargetProjectName;

        public Dictionary<string, bool> FoundNodes;
    }

    public class TfsNodeStructure : WorkItemProcessorEnricher
    {
        private Dictionary<string, bool> _foundNodes = new();
        private string[] _nodeBasePaths;

        private bool _prefixProjectToNodes;

        private ICommonStructureService4 _sourceCommonStructureService;

        private TfsLanguageMapOptions _sourceLanguageMaps;

        private string _sourceProjectName;
        private ICommonStructureService4 _targetCommonStructureService;
        private TfsLanguageMapOptions _targetLanguageMaps;
        private string _targetProjectName;

        public TfsNodeStructure(IServiceProvider services, ILogger<WorkItemProcessorEnricher> logger) : base(services,
            logger)
        {
        }

        private TfsNodeStructureOptions Options { get; set; }

        [Obsolete("Old v1 arch: this is a v2 class", true)]
        public override void Configure(bool save = true, bool filter = true)
        {
            throw new NotImplementedException();
        }

        public override void Configure(IProcessorEnricherOptions options)
        {
            Options = (TfsNodeStructureOptions)options;
        }

        public void ApplySettings(TfsNodeStructureSettings settings)
        {
            _sourceProjectName = settings.SourceProjectName;
            _targetProjectName = settings.TargetProjectName;
            _foundNodes = settings.FoundNodes;
        }

        [Obsolete("Old v1 arch: this is a v2 class", true)]
        public override int Enrich(WorkItemData sourceWorkItem, WorkItemData targetWorkItem)
        {
            throw new NotImplementedException();
        }

        public string GetNewNodeName(string sourceNodeName, TfsNodeStructureType nodeStructureType,
            string targetStructureName = null, string sourceStructureName = null)
        {
            Log.LogDebug("NodeStructureEnricher.GetNewNodeName({sourceNodeName}, {nodeStructureType})", sourceNodeName,
                nodeStructureType.ToString());
            var tStructureName = targetStructureName ??
                                 NodeStructureTypeToLanguageSpecificName(_targetLanguageMaps, nodeStructureType);
            var sStructureName = sourceStructureName ??
                                 NodeStructureTypeToLanguageSpecificName(_sourceLanguageMaps, nodeStructureType);
            // Replace project name with new name (if necessary) and inject nodePath (Area or Iteration) into path for node validation
            string newNodeName;
            if (_prefixProjectToNodes)
            {
                newNodeName = $@"{_targetProjectName}\{tStructureName}\{sourceNodeName}";
            }
            else
            {
                var regex = new Regex(Regex.Escape(_sourceProjectName));
                if (sourceNodeName.StartsWith($@"{_sourceProjectName}\{sStructureName}\"))
                    newNodeName = regex.Replace(sourceNodeName, _targetProjectName, 1);
                else
                    newNodeName = regex.Replace(sourceNodeName, $@"{_targetProjectName}\{tStructureName}", 1);
            }

            // Validate the node exists
            if (!TargetNodeExists(newNodeName))
            {
                Log.LogWarning(
                    "The Node '{newNodeName}' does not exist, leaving as '{newProjectName}'. This may be because it has been renamed or moved and no longer exists, or that you have not migrated the Node Structure yet",
                    newNodeName, _targetProjectName);
                newNodeName = _targetProjectName;
            }

            // Remove nodePath (Area or Iteration) from path for correct population in work item
            if (newNodeName.StartsWith(_targetProjectName + '\\' + tStructureName + '\\'))
                newNodeName = newNodeName.Remove(newNodeName.IndexOf($@"{nodeStructureType}\", StringComparison.Ordinal),
                    $@"{nodeStructureType}\".Length);
            else if (newNodeName.StartsWith(_targetProjectName + '\\' + tStructureName))
                newNodeName = newNodeName.Remove(newNodeName.IndexOf($@"{nodeStructureType}", StringComparison.Ordinal),
                    $@"{nodeStructureType}".Length);
            newNodeName = newNodeName.Replace(@"\\", @"\");

            return newNodeName;
        }

        public override void ProcessorExecutionBegin(IProcessor processor)
        {
            if (!Options.Enabled)
                return;

            Log.LogInformation("Migrating all Nodes before the Processor run");
            EntryForProcessorType(processor);
            MigrateAllNodeStructures();
            RefreshForProcessorType(processor);
        }

        protected override void EntryForProcessorType(IProcessor processor)
        {
            if (processor is not null)
                throw new Exception("Why is the processor not null? What is the processor used for??");

            if (_sourceCommonStructureService is not null)
                throw new Exception("Why is the _sourceCommonStructureService not null??");


            if (_targetCommonStructureService is not null)
                throw new Exception("Why is the _targetCommonStructureService not null??");

            var engine = Services.GetRequiredService<IMigrationEngine>();
            _sourceCommonStructureService = engine.Source.GetService<ICommonStructureService4>();
            _sourceLanguageMaps = engine.Source.Config.AsTeamProjectConfig().LanguageMaps;
            _sourceProjectName = engine.Source.Config.AsTeamProjectConfig().Project;

            _targetCommonStructureService = engine.Target.GetService<ICommonStructureService4>();
            _targetLanguageMaps = engine.Target.Config.AsTeamProjectConfig().LanguageMaps;
            _targetProjectName = engine.Target.Config.AsTeamProjectConfig().Project;
        }

        protected override void RefreshForProcessorType(IProcessor processor)
        {
            if (processor is not null)
                throw new Exception("Why is the processor not null??");

            var engine = Services.GetRequiredService<IMigrationEngine>();
            ((TfsWorkItemMigrationClient)engine.Target.WorkItems).Store?.RefreshCache(true);
        }

        private NodeInfo CreateNode(string name, NodeInfo parent, DateTime? startDate, DateTime? finishDate)
        {
            var nodePath = $@"{parent.Path}\{name}";
            NodeInfo node;

            Log.LogInformation(" Processing Node: {nodePath}, start date: {startDate}, finish date: {finishDate}",
                nodePath, startDate, finishDate);

            try
            {
                node = _targetCommonStructureService.GetNodeFromPath(nodePath);
                Log.LogDebug("  Node {node} already exists", nodePath);
                Log.LogTrace("{node}", node);
            }
            catch (CommonStructureSubsystemException ex)
            {
                try
                {
                    var newPathUri = _targetCommonStructureService.CreateNode(name, parent.Uri);
                    Log.LogDebug("  Node {newPathUri} has been created", newPathUri);
                    node = _targetCommonStructureService.GetNode(newPathUri);
                }
                catch
                {
                    Log.LogError(ex, "Creating Node");
                    throw;
                }
            }

            if (startDate != null && finishDate != null)
                try
                {
                    ((ICommonStructureService4)_targetCommonStructureService).SetIterationDates(node.Uri, startDate, finishDate);
                    Log.LogDebug("  Node {node} has been assigned {startDate} / {finishDate}", nodePath, startDate,
                        finishDate);
                }
                catch (CommonStructureSubsystemException ex)
                {
                    Log.LogWarning(ex, " Unable to set {node}dates of {startDate} / {finishDate}", nodePath, startDate,
                        finishDate);
                }

            return node;
        }

        private void CreateNodes(XmlNodeList nodeList, NodeInfo parentPath, string treeType)
        {
            foreach (XmlNode item in nodeList)
            {
                var newNodeName = item.Attributes["Name"].Value;

                if (!ShouldCreateNode(parentPath, newNodeName)) continue;

                NodeInfo targetNode;
                if (treeType == "Iteration")
                {
                    DateTime? startDate = null;
                    DateTime? finishDate = null;
                    if (item.Attributes["StartDate"] != null)
                        startDate = DateTime.Parse(item.Attributes["StartDate"].Value);
                    if (item.Attributes["FinishDate"] != null)
                        finishDate = DateTime.Parse(item.Attributes["FinishDate"].Value);

                    targetNode = CreateNode(newNodeName, parentPath, startDate, finishDate);
                }
                else
                {
                    targetNode = CreateNode(newNodeName, parentPath, null, null);
                }

                if (item.HasChildNodes) CreateNodes(item.ChildNodes[0].ChildNodes, targetNode, treeType);
            }
        }

        private void MigrateAllNodeStructures()
        {
            _prefixProjectToNodes = Options.PrefixProjectToNodes;
            _nodeBasePaths = Options.NodeBasePaths;

            Log.LogDebug("NodeStructureEnricher.MigrateAllNodeStructures({prefixProjectToNodes}, {nodeBasePaths})",
                _prefixProjectToNodes, _nodeBasePaths);

            //Process Area Paths
            ProcessCommonStructure(_sourceLanguageMaps.AreaPath, _targetLanguageMaps.AreaPath);

            //Process Iterations Paths
            ProcessCommonStructure(_sourceLanguageMaps.IterationPath,
                _targetLanguageMaps.IterationPath);
        }

        private static string NodeStructureTypeToLanguageSpecificName(TfsLanguageMapOptions languageMaps,
            TfsNodeStructureType value)
        {
            return value switch
            {
                TfsNodeStructureType.Area => languageMaps.AreaPath,
                TfsNodeStructureType.Iteration => languageMaps.IterationPath,
                _ => throw new InvalidOperationException("Not a valid NodeStructureType ")
            };
        }

        private void ProcessCommonStructure(string treeTypeSource, string treeTypeTarget)
        {
            Log.LogDebug("NodeStructureEnricher.ProcessCommonStructure({treeTypeSource}, {treeTypeTarget})",
                treeTypeSource, treeTypeTarget);

            var sourceNode = GetSourceNode(treeTypeSource);

            var sourceTree = _sourceCommonStructureService.GetNodesXml(new[] { sourceNode.Uri }, true);

            NodeInfo structureParent;
            try // May run into language problems!!! This is to try and detect that
            {
                structureParent = _targetCommonStructureService.GetNodeFromPath($"\\{_targetProjectName}\\{treeTypeTarget}");
            }
            catch (Exception ex)
            {
                var ex2 = new Exception(
                    $"Unable to load Common Structure for Target.This is usually due to different language versions. Validate that '{treeTypeTarget}' is the correct name in your version. ",
                    ex);
                Log.LogError(ex2, "Unable to load Common Structure for Target");
                throw ex2;
            }

            if (_prefixProjectToNodes) structureParent = CreateNode(_sourceProjectName, structureParent, null, null);
            if (sourceTree.ChildNodes[0].HasChildNodes)
                CreateNodes(sourceTree.ChildNodes[0].ChildNodes[0].ChildNodes, structureParent, treeTypeTarget);
        }

        private NodeInfo GetSourceNode(string treeTypeSource)
        {
            // (i.e. "\CoolProject\Area\MyCustomArea" )
            var path = "\\" + _sourceProjectName + "\\" + treeTypeSource;

            var node = _sourceCommonStructureService.GetNodeFromPath(path);

            if (node is not null)
                return node;

            var ex = new Exception(
                $"Unable to load Common Structure for Source <{treeTypeSource}>");
            Log.LogError(ex, "Unable to load Common Structure for Source");
            throw ex;

        }

        /// <summary>
        ///     Checks node-to-be-created with allowed BasePath's
        /// </summary>
        /// <param name="parentPath">Parent Node</param>
        /// <param name="newNodeName">Node to be created</param>
        /// <returns>true/false</returns>
        private bool ShouldCreateNode(NodeInfo parentPath, string newNodeName)
        {
            var nodePath = string.Format(@"{0}\{1}", parentPath.Path, newNodeName);

            if (_nodeBasePaths != null && _nodeBasePaths.Any())
            {
                var split = nodePath.Split('\\');
                var removeProjectAndType = split.Skip(3);
                var path = string.Join(@"\", removeProjectAndType);

                // We need to check if the path is a parent path of one of the base paths, as we need those
                foreach (var basePath in _nodeBasePaths)
                {
                    var splitBase = basePath.Split('\\');

                    for (var i = 0; i < splitBase.Length; i++)
                        if (string.Equals(path, string.Join(@"\", splitBase.Take(i)),
                            StringComparison.InvariantCultureIgnoreCase))
                            return true;
                }

                if (!_nodeBasePaths.Any(p => path.StartsWith(p, StringComparison.InvariantCultureIgnoreCase)))
                {
                    Log.LogWarning("The node {nodePath} is being excluded due to your basePath setting. ", nodePath);
                    return false;
                }
            }

            return true;
        }

        private bool TargetNodeExists(string nodePath)
        {
            if (_foundNodes.ContainsKey(nodePath))
                return _foundNodes[nodePath];

            try
            {
                _targetCommonStructureService.GetNodeFromPath(nodePath);
                _foundNodes.Add(nodePath, true);
            }
            catch
            {
                _foundNodes.Add(nodePath, false);
            }

            return _foundNodes[nodePath];
        }
    }
}