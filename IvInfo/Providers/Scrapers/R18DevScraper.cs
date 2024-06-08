using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
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
public class R18DevScraper(ILogger logger) : IScraper
{
    public const string Name = nameof(R18DevScraper);
    public bool Enabled => IvInfo.Instance?.Configuration.R18DevScraperEnabled ?? false;
    public bool ImgEnabled => IvInfo.Instance?.Configuration.R18DevImgEnabled ?? false;
    public int Priority => IvInfo.Instance?.Configuration.R18DevScraperPriority ?? 1;

    private const string SearchUrl = "https://r18.dev/videos/vod/movies/detail/-/dvd_id={0}/json";
    private const string DataUrl = "https://r18.dev/videos/vod/movies/detail/-/combined={0}/json";

    public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(IEnumerable<RemoteSearchResult> resultList, MovieInfo info, CancellationToken cancellationToken)
    {
        var globalId = info.GetProviderId(IvInfoConstants.Name) ?? IvInfoProvider.GetId(info);
        logger.LogDebug("{Name}: searching for global id: {Id}", Name, globalId);
        if (string.IsNullOrEmpty(globalId)) return resultList;

        var localResultList = new List<RemoteSearchResult>(resultList);
        var doc = await GetData(cancellationToken, globalId: globalId);
        if (doc == null)
        {
            return localResultList;
        }
        
        var scraperId = doc.RootElement.GetProperty("content_id").GetString();
        var title = doc.RootElement.GetProperty("title_ja").GetString();
        var imgUrl = doc.RootElement.GetProperty("jacket_thumb_url").GetString();
        var nextIndex = localResultList.Count > 0 ? localResultList.Max(r => r.IndexNumber ?? 0) + 1 : 1;
        var result = new RemoteSearchResult
        {
            Name = title,
            ImageUrl = imgUrl,
            SearchProviderName = Name,
            Overview = $"{globalId}<br />{scraperId}",
            IndexNumber = nextIndex
        };
        result.SetProviderId(Name, scraperId);
        result.SetProviderId(IvInfoConstants.Name, $"{globalId}|{scraperId}");
        localResultList.Add(result);

        return localResultList;
    }

    public async Task<bool> FillMetadata(MetadataResult<Movie> metadata, ItemLookupInfo info, CancellationToken cancellationToken)
    {
        var scraperId = metadata.Item.GetProviderId(Name);
        var globalId = metadata.Item.GetProviderId(IvInfoConstants.Name);
        logger.LogDebug("{Name}: searching for ids: {GlobalId}, {ScraperId}", Name, globalId, scraperId);
        if (globalId == null && scraperId == null)
        {
            logger.LogError("Could not determine any id");
            return false;
        }

        var doc = scraperId != null
            ? await GetData(cancellationToken, scraperId: scraperId)
            : await GetData(cancellationToken, globalId: globalId!);
        if (doc == null)
        {
            return false;
        }

        var title = doc.RootElement.GetProperty("title_ja").GetString();
        
        if (!string.IsNullOrEmpty(title) && string.IsNullOrWhiteSpace(metadata.Item.Name))
            metadata.Item.Name = title;

        metadata.Item.SetProviderId(Name, scraperId);

        logger.LogDebug("{Name}: metadata fetching finished", Name);
        return true;
    }

    public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken, ImageType imageType = ImageType.Primary)
    {
        logger.LogDebug("{Name}: searching for image {ImageType}", Name, imageType);
        var result = new List<RemoteImageInfo>();

        if (!HandledImageTypes().Contains(imageType))
        {
            logger.LogDebug("{Name}: {ImageType} image type not handled", Name, imageType);
            return result;
        }

        if (item.ImageInfos.Any(i => i.Type == imageType))
        {
            logger.LogDebug("{Name}: {ImageType} image already exists, not overwriting", Name, imageType);
            return result;
        }

        var scraperId = item.GetProviderId(Name);
        if (string.IsNullOrEmpty(scraperId)) return result;

        logger.LogDebug("{Name}: scraper id: {Id}", Name, scraperId);

        var doc = await GetData(cancellationToken, scraperId: scraperId);
        if (doc == null) return result;

        switch (imageType)
        {
            case ImageType.Primary:
                var frontUrl = doc.RootElement.GetProperty("jacket_thumb_url").GetString();
                if (string.IsNullOrEmpty(frontUrl)) break;
                result.Add(new RemoteImageInfo
                    { Url = frontUrl, Type = imageType, ProviderName = Name });
                break;
            case ImageType.Box:
                var boxUrl = doc.RootElement.GetProperty("jacket_full_url").GetString();
                if (string.IsNullOrEmpty(boxUrl)) break;
                result.Add(new RemoteImageInfo
                    { Url = boxUrl, Type = imageType, ProviderName = Name });
                break;
            case ImageType.Screenshot:
                doc.RootElement.GetProperty("gallery");
                break;
                // result.AddRange(screenshots);
            case ImageType.Art:
            case ImageType.Backdrop:
            case ImageType.Banner:
            case ImageType.Logo:
            case ImageType.Thumb:
            case ImageType.Disc:
            case ImageType.Menu:
            case ImageType.Chapter:
            case ImageType.BoxRear:
            case ImageType.Profile:
            default:
                logger.LogDebug("{Name}: {ImageType} image type not handled", Name, imageType);
                break;
        }

        logger.LogDebug("{Name}: image searching finished", Name);
        return await Task.Run(() => result, cancellationToken);
    }

    public IEnumerable<ImageType> HandledImageTypes()
    {
        yield return ImageType.Primary;
        yield return ImageType.Box;
        yield return ImageType.Screenshot;
    }

    private static async Task<JsonDocument?> GetData(CancellationToken cancellationToken, string globalId = "", string scraperId = "")
    {
        var client = new HttpClient();
        JsonDocument doc;
        if (!string.IsNullOrEmpty(scraperId))
        {
            var response = await client.GetAsync(string.Format(DataUrl, scraperId), cancellationToken);
            doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        }
        else
        {
            var response = await client.GetAsync(string.Format(SearchUrl, globalId), cancellationToken);
            doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            scraperId = doc.RootElement.GetProperty("content_id").GetString() ?? string.Empty;
            if (string.IsNullOrEmpty(scraperId))
            {
                return doc;
            }
            response = await client.GetAsync(string.Format(DataUrl, scraperId), cancellationToken);
            doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        }
        
        return doc;
    }
}