using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.IvInfo.Providers.Scrapers;

public interface IScraper
{
    /// <summary>
    ///     Should scraper fetch only first result if multiple matches were found.
    /// </summary>
    protected static bool FirstOnly => IvInfo.Instance?.Configuration.FirstOnly ?? false;

    /// <summary>
    ///     Should scrapers called later in scraping order replace data already filled by earlier scrapers.
    /// </summary>
    protected static bool Overwriting => IvInfo.Instance?.Configuration.Overwriting ?? false;

    /// <summary>
    ///     Is scraper enabled in configuration. Enabled scrapers are used in searching and metadata fetching.
    /// </summary>
    public bool Enabled { get; }

    /// <summary>
    ///     Is image fetching from scraper enabled in configuration.
    /// </summary>
    public bool ImgEnabled { get; }

    /// <summary>
    ///     Scraper priority, used in filling metadata. Scrapers with lower priority are called earlier.
    /// </summary>
    /// <returns>scraper priority</returns>
    public int Priority { get; }

    /// <summary>
    ///     Returns search results for this item info.
    /// </summary>
    /// <param name="resultList"></param>
    /// <param name="info">searched item</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <returns>list with search results</returns>
    public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(IEnumerable<RemoteSearchResult> resultList,
        MovieInfo info, CancellationToken cancellationToken);

    /// <summary>
    ///     Fills metadata for <see cref="MetadataResult{T}" />.
    /// </summary>
    /// <param name="metadata">metadata object to fill</param>
    /// <param name="info">source item info</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <returns>true if any data was filled, false otherwise</returns>
    public Task<bool> FillMetadata(MetadataResult<Movie> metadata, ItemLookupInfo info,
        CancellationToken cancellationToken);

    /// <summary>
    ///     Gets url list for image for passed item and requested type.
    /// </summary>
    /// <param name="item">item</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <param name="imageType">image type <seealso cref="ImageType" /></param>
    /// <returns>url list for image or empty list if image was not found</returns>
    public Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken,
        ImageType imageType = ImageType.Primary);

    /// <summary>
    ///     Returns array of <see cref="ImageType" />s provided by scraper.
    /// </summary>
    /// <returns>array of <see cref="ImageType" />s</returns>
    public IEnumerable<ImageType> HandledImageTypes();
}