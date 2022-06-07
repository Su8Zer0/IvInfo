using System;
using System.Collections.Generic;
using Jellyfin.Plugin.IvInfo.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.IvInfo
{
    // ReSharper disable once ClassNeverInstantiated.Global
    public class IvInfo : BasePlugin<IvInfoPluginConfiguration>, IHasWebPages
    {
        public IvInfo(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer) : base(applicationPaths,
            xmlSerializer)
        {
            Instance = this;
        }

        /// <summary>
        ///     Gets the current plugin instance.
        /// </summary>
        public static IvInfo? Instance { get; private set; }

        public override string Name => IvInfoConstants.Name;

        public override Guid Id => Guid.Parse(IvInfoConstants.Guid);

        /// <inheritdoc />
        public IEnumerable<PluginPageInfo> GetPages()
        {
            yield return new PluginPageInfo
            {
                Name = Name,
                EmbeddedResourcePath = GetType().Namespace + ".Configuration.config.html"
            };
        }

        public override PluginInfo GetPluginInfo()
        {
            return new PluginInfo(IvInfoConstants.Name, new Version(0, 1, 2, 0), "Idol Video metadata provider", Id,
                true);
        }
    }
}