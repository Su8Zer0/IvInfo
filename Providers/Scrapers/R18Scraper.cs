using System.Collections.Generic;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.IvInfo.Providers.Scrapers
{
    // ReSharper disable once UnusedType.Global
    public class R18Scraper : IScraper
    {
        private const string Name = nameof(R18Scraper);
        
        private readonly ILogger _logger;

        public R18Scraper(ILogger logger)
        {
            _logger = logger;
        }
        
        public int Priority => 2;

        public bool Enabled => false;

        public IEnumerable<RemoteSearchResult> GetSearchResults(MovieInfo info)
        {
            var globalId = info.GetProviderId(IvInfoConstants.Name);
            _logger.LogDebug("{Name}: searching for id: {Id}", Name, globalId);
            yield break;
        }

        public bool FillMetadata(MetadataResult<Movie> metadata, bool overwrite = false)
        {
            var id = metadata.Item.GetProviderId(IvInfoConstants.Name);
            if (string.IsNullOrEmpty(id)) return false;
            
            return false;
        }

        public IEnumerable<RemoteImageInfo> GetImages(BaseItem item, ImageType imageType = ImageType.Primary,
            bool overwrite = false)
        {
            yield break;
        }

        public IEnumerable<ImageType> HandledImageTypes()
        {
            yield break;
        }
    }
}