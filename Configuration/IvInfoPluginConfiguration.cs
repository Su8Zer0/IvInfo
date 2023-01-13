using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.IvInfo.Configuration
{
    // ReSharper disable once ClassNeverInstantiated.Global
    public class IvInfoPluginConfiguration : BasePluginConfiguration
    {
        public bool FirstOnly { get; set; }
        public bool JavlibraryScraperEnabled { get; set; }
        public bool DmmScraperEnabled { get; set; }
        public bool GekiyasuScraperEnabled { get; set; }
        public bool JavlibraryImgEnabled { get; set; }
        public bool DmmImgEnabled { get; set; }
        public bool GekiyasuImgEnabled { get; set; }
        public bool DmmUseProxy { get; set; }
    }
}