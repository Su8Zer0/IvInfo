using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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

        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(IEnumerable<RemoteSearchResult> resultList,
            MovieInfo info,
            CancellationToken cancellationToken)
        {
            var globalId = info.GetProviderId(IvInfoConstants.Name);
            _logger.LogDebug("{Name}: searching for id: {Id}", Name, globalId);
            return await Task.Run(() => new List<RemoteSearchResult>(), cancellationToken);
        }

        public async Task<bool> FillMetadata(MetadataResult<Movie> metadata, CancellationToken cancellationToken,
            bool overwrite = false)
        {
            return await Task.Run(() => false, cancellationToken);
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken,
            ImageType imageType = ImageType.Primary,
            bool overwrite = false)
        {
            return await Task.Run(() => new List<RemoteImageInfo>(), cancellationToken);
        }

        public IEnumerable<ImageType> HandledImageTypes()
        {
            yield break;
        }
    }
}