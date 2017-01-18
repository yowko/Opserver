using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace StackExchange.Opserver.Data.Dashboard.Providers
{
    partial class WmiDataProvider : DashboardDataProvider<WMISettings>
    {
        private readonly IEnumerable<WMISettings>  _config;
        private readonly List<WmiNode> _wmiNodes;
        private readonly Dictionary<string, WmiNode> _wmiNodeLookup;

        public WmiDataProvider(IEnumerable<WMISettings> settings) : base(settings)
        {
            _config = settings;
            _wmiNodes = InitNodeList(_config.Select(a =>a.Nodes).ToList()).OrderBy(x => x.Endpoint).ToList();
            // Do this ref cast list once
            AllNodes = _wmiNodes.Cast<Node>().ToList();
            // For fast lookups
            _wmiNodeLookup = new Dictionary<string, WmiNode>(_wmiNodes.Count);
            foreach(var n in _wmiNodes)
            {
                _wmiNodeLookup[n.Id] = n;
            }
        }

        /// <summary>
        /// Make list of nodes as per configuration. 
        /// When adding, a node's ip address is resolved via Dns.
        /// </summary>
        private IEnumerable<WmiNode> InitNodeList(IEnumerable<IList<string>> names)
        {
            var nodesList = new List<WmiNode>();
            var exclude = Current.Settings.Dashboard.ExcludePatternRegex;
            foreach (var nodeNames in names)
            {
                foreach (var nodeName in nodeNames)
                {
                    if (exclude?.IsMatch(nodeName) ?? false) continue;

                    var node = new WmiNode(nodeName)
                    {
                        Config = _config.FirstOrDefault(a => a.Nodes.Contains(nodeName)),
                        DataProvider = this
                    };

                    try
                    {
                        var hostEntry = Dns.GetHostEntry(node.Name);
                        if (hostEntry.AddressList.Any())
                        {
                            node.Ip = hostEntry.AddressList[0].ToString();
                            node.Status = NodeStatus.Active;
                        }
                        else
                        {
                            node.Status = NodeStatus.Unreachable;
                        }
                    }
                    catch (Exception)
                    {
                        node.Status = NodeStatus.Unreachable;
                    }

                    node.Caches.Add(ProviderCache(
                        () => node.PollNodeInfoAsync(),
                        node.Config.StaticDataTimeoutSeconds.Seconds(),
                        memberName: node.Name + "-Static"));

                    node.Caches.Add(ProviderCache(
                        () => node.PollStats(),
                        node.Config.DynamicDataTimeoutSeconds.Seconds(),
                        memberName: node.Name + "-Dynamic"));

                    nodesList.Add(node);
                }
            }
            return nodesList;
        }

        private WmiNode GetWmiNodeById(string id)
        {
            WmiNode n;
            return _wmiNodeLookup.TryGetValue(id, out n) ? n : null;
        }

        public override int MinSecondsBetweenPolls => 10;

        public override string NodeType => "WMI";

        public override IEnumerable<Cache> DataPollers => _wmiNodes.SelectMany(x => x.Caches);

        protected override IEnumerable<MonitorStatus> GetMonitorStatus()
        {
            yield break;
        }

        protected override string GetMonitorStatusReason() => null;

        public override bool HasData => DataPollers.Any(x => x.ContainsData);

        public override List<Node> AllNodes { get; }
    }
}
