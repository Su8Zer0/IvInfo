using System;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.IvInfo
{
    // ReSharper disable once ClassNeverInstantiated.Global
    public class IvInfo : BasePlugin<IvInfoPluginConfiguration>
    {
        public IvInfo(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer) : base(applicationPaths,
            xmlSerializer)
        {
            Instance = this;
        }

        /// <summary>
        /// Gets the current plugin instance.
        /// </summary>
        public static IvInfo? Instance { get; private set; }

        public override string Name => IvInfoConstants.Name;

        public override Guid Id => Guid.Parse(IvInfoConstants.Guid);

        public override PluginInfo GetPluginInfo()
        {
            return new PluginInfo(IvInfoConstants.Name, new Version(0, 1, 2, 0), "Idol Video metadata provider", Id,
                true);
        }
    }
}