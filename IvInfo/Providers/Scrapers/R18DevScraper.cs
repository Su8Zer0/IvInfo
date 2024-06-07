using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace IvInfo.Providers.Scrapers;

// ReSharper disable once UnusedType.Global
// ReSharper disable once ClassNeverInstantiated.Global
public class R18DevScraper: IScraper
{
    public const string Name = nameof(R18DevScraper);
    public bool Enabled => IvInfo.Instance?.Configuration.R18DevScraperEnabled ?? false;
    public bool ImgEnabled => IvInfo.Instance?.Configuration.R18DevImgEnabled ?? false;
    public int Priority => IvInfo.Instance?.Configuration.R18DevScraperPriority ?? 1;

    private const string SearchUrl = "https://r18.dev/videos/vod/movies/detail/-/dvd_id={id}/json";
    
    private readonly ILogger _logger;
    
    public R18DevScraper(ILogger logger)
    {
        _logger = logger;
    }
    
    public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(IEnumerable<RemoteSearchResult> resultList, MovieInfo info, CancellationToken cancellationToken)
    {
        throw new System.NotImplementedException();
    }

    public Task<bool> FillMetadata(MetadataResult<Movie> metadata, ItemLookupInfo info, CancellationToken cancellationToken)
    {
        throw new System.NotImplementedException();
    }

    public Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken, ImageType imageType = ImageType.Primary)
    {
        throw new System.NotImplementedException();
    }

    public IEnumerable<ImageType> HandledImageTypes()
    {
        throw new System.NotImplementedException();
    }
}