using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.IvInfo.Configuration
{
    // ReSharper disable once ClassNeverInstantiated.Global
    public class IvInfoPluginConfiguration : BasePluginConfiguration
    {
        public bool JavlibraryScraperEnabled { get; set; }
        public bool DmmScraperEnabled { get; set; }
        public bool GekiyasuScraperEnabled { get; set; }
    }
}