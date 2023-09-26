using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.IvInfo.Providers.Scrapers;

// ReSharper disable once UnusedType.Global
public class GekiyasuScraper : IScraper
{
    internal const string Name = nameof(GekiyasuScraper);

    private const string BaseUrl = "https://www.gekiyasu-dvdshop.jp/";
    private const string SearchUrl = BaseUrl + "products/list.php?mode=search&name={0}";
    private const string NoResults = "該当件数0件です";
    private const string MakerLbl = "メーカー";
    private const string Selector = "//div[@class='b-list-area']/ul[@class='b-list clearfix']/li";
    private const string AgeCheck = "年齢認証";
    private const string AgeCheckUrl = "https://www.gekiyasu-dvdshop.jp/r18_auth.php?";
    private const string Referer = "https://www.gekiyasu-dvdshop.jp/r18_auth.php?url=/{0}&transactionid={1}";
    private const string TransactionId = "transactionid";

    private readonly ILogger _logger;

    public GekiyasuScraper(ILogger logger)
    {
        _logger = logger;
    }

    public int Priority => IvInfo.Instance?.Configuration.GekiyasuScraperPriority ?? 3;

    public bool Enabled => IvInfo.Instance?.Configuration.GekiyasuScraperEnabled ?? false;
    public bool ImgEnabled => IvInfo.Instance?.Configuration.GekiyasuImgEnabled ?? false;

    public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(IEnumerable<RemoteSearchResult> resultList,
        MovieInfo info, CancellationToken cancellationToken)
    {
        var globalId = info.GetProviderId(IvInfoConstants.Name) ?? IvInfoProvider.GetId(info);
        _logger.LogDebug("{Name}: searching for id: {Id}", Name, globalId);
        if (string.IsNullOrEmpty(globalId)) return resultList;

        return await GetSearchResults(resultList, info, globalId, cancellationToken, IScraper.FirstOnly);
    }

    public async Task<bool> FillMetadata(MetadataResult<Movie> metadata, ItemLookupInfo info,
        CancellationToken cancellationToken)
    {
        var scraperId = metadata.Item.GetProviderId(Name);
        var globalId = metadata.Item.GetProviderId(IvInfoConstants.Name);
        _logger.LogDebug("{Name}: searching for ids: {GlobalId}, {ScraperId}", Name, globalId, scraperId);
        if (globalId == null && scraperId == null)
        {
            _logger.LogError("Could not determine any id");
            return false;
        }

        HtmlDocument doc;
        if (scraperId == null)
        {
            _logger.LogDebug("{Name}: no scraper id, searching for global id: {Id}", Name, globalId);
            if (info.IsAutomated)
            {
                _logger.LogDebug("Manual search should have id already, aborting");
                return false;
            }

            if (globalId == null)
            {
                _logger.LogError("Could not determine global id");
                return false;
            }

            var results = new List<RemoteSearchResult>();
            results = await GetSearchResults(results, info, globalId, cancellationToken);
            if (results.Count > 1 && !IScraper.FirstOnly)
            {
                _logger.LogDebug("{Name}: multiple results found and selecting first result not enabled - aborting",
                    Name);
                return false;
            }

            var result = results.FirstOrDefault();
            scraperId = result?.GetProviderId(Name);
            if (string.IsNullOrEmpty(scraperId)) return false;
            doc = await GetSingleResult(scraperId, cancellationToken);
        }
        else
        {
            _logger.LogDebug("{Name}: searching for scraperid: {Id}", Name, scraperId);
            doc = await GetSingleResult(scraperId, cancellationToken);
        }

        if (string.IsNullOrEmpty(doc.Text))
        {
            _logger.LogDebug("{Name}: searching returned empty page, error?", Name);
            return false;
        }

        scraperId = GetScraperId(doc);

        var title = doc.DocumentNode.SelectSingleNode("//div[@class='b-title']/strong")?.InnerText;
        var datePresent = DateTime.TryParse(
            doc.DocumentNode.SelectSingleNode("//div[@class='b-releasedate']/span")?.InnerText,
            out var releaseDate);
        var description = doc.DocumentNode.SelectSingleNode("//div[@id='detailarea']/div[@class='well']/span")
            ?.InnerText;
        var maker = doc.DocumentNode.SelectNodes("//ul[@class='b-relative']/li/a")
            ?.Where(node => node.InnerText == MakerLbl).FirstOrDefault()?.ParentNode?.LastChild?.InnerText;

        if (!string.IsNullOrEmpty(title) && string.IsNullOrWhiteSpace(metadata.Item.Name))
            metadata.Item.Name = title;
        if (!string.IsNullOrEmpty(description) && string.IsNullOrWhiteSpace(metadata.Item.Overview))
            metadata.Item.Overview = description;
        if (datePresent && metadata.Item.PremiereDate == null)
            metadata.Item.PremiereDate = releaseDate;
        if (datePresent && metadata.Item.ProductionYear == null)
            metadata.Item.ProductionYear = releaseDate.Year;
        if (string.IsNullOrWhiteSpace(metadata.Item.OfficialRating))
            metadata.Item.OfficialRating = "R";
        if (string.IsNullOrWhiteSpace(metadata.Item.ExternalId)) metadata.Item.ExternalId = scraperId;
        if (!string.IsNullOrEmpty(maker) && !metadata.Item.Studios.Contains(maker))
            metadata.Item.AddStudio(maker);

        metadata.Item.SetProviderId(Name, scraperId);

        _logger.LogDebug("{Name}: metadata fetching finished", Name);
        return true;
    }

    public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken,
        ImageType imageType = ImageType.Primary)
    {
        _logger.LogDebug("{Name}: searching for image {ImageType}", Name, imageType);
        var result = new List<RemoteImageInfo>();

        if (!HandledImageTypes().Contains(imageType))
        {
            _logger.LogDebug("{Name}: {ImageType} image type not handled", Name, imageType);
            return await Task.Run(() => result, cancellationToken);
        }

        if (item.ImageInfos.Any(i => i.Type == imageType))
        {
            _logger.LogDebug("{Name}: {ImageType} image already exists, not overwriting", Name, imageType);
            return await Task.Run(() => result, cancellationToken);
        }

        var scraperId = item.GetProviderId(Name);
        if (string.IsNullOrEmpty(scraperId)) return result;

        var url = BaseUrl + scraperId;
        var html = await GetHtml(url, cancellationToken);
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        if (doc.DocumentNode.InnerText.Contains(NoResults)) return result;

        var thumbUrl = BaseUrl + doc.DocumentNode.SelectSingleNode("//a[@class='thumbnail']")
            .GetAttributeValue("href", "");
        var boxUrl = BaseUrl + doc.DocumentNode.SelectSingleNode("//img[@class='img-responsive']")
            .GetAttributeValue("src", "");

        switch (imageType)
        {
            case ImageType.Primary:
                result.Add(new RemoteImageInfo
                    { Url = thumbUrl, Type = imageType, ProviderName = Name });
                break;
            case ImageType.Box:
                result.Add(new RemoteImageInfo
                    { Url = boxUrl, Type = imageType, ProviderName = Name });
                break;
            case ImageType.Art:
            case ImageType.Backdrop:
            case ImageType.Banner:
            case ImageType.Logo:
            case ImageType.Thumb:
            case ImageType.Disc:
            case ImageType.Screenshot:
            case ImageType.Menu:
            case ImageType.Chapter:
            case ImageType.BoxRear:
            case ImageType.Profile:
            default:
                _logger.LogDebug("{Name}: {ImageType} image type not handled", Name, imageType);
                break;
        }

        _logger.LogDebug("{Name}: image searching finished", Name);
        return await Task.Run(() => result, cancellationToken);
    }

    public IEnumerable<ImageType> HandledImageTypes()
    {
        yield return ImageType.Primary;
        yield return ImageType.Box;
    }

    private async Task<List<RemoteSearchResult>> GetSearchResults(IEnumerable<RemoteSearchResult> resultList,
        ItemLookupInfo info, string globalId, CancellationToken cancellationToken, bool firstOnly = false)
    {
        var localResultList = new List<RemoteSearchResult>(resultList);
        var (multiple, doc) = await GetSearchResultsPage(globalId, cancellationToken);
        if (string.IsNullOrEmpty(doc.Text)) return localResultList;
        if (multiple)
        {
            var nodeCollection = doc.DocumentNode.SelectNodes(Selector) ?? new HtmlNodeCollection(null);
            foreach (var node in nodeCollection)
            {
                var scraperId = node.ChildNodes.FindFirst("a").GetAttributeValue("href", "").Trim('/');
                var title = node.ChildNodes.FindFirst("p").InnerText;
                var imgUrl = BaseUrl + node.ChildNodes.FindFirst("a").FirstChild.GetAttributeValue("src", "");
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

                if (firstOnly && !info.IsAutomated) break;
            }
        }
        else
        {
            var scraperId = GetFirstResultId(doc);
            if (string.IsNullOrEmpty(scraperId)) return localResultList;
            var html = await GetHtml(BaseUrl + scraperId, cancellationToken);
            if (string.IsNullOrEmpty(html)) return localResultList;
            doc.LoadHtml(html);
            var title = doc.DocumentNode.SelectSingleNode("//div[@class='b-title']/strong")?.InnerText;
            var imgUrl = BaseUrl + doc.DocumentNode.SelectSingleNode("//a[@class='thumbnail']")
                .GetAttributeValue("href", "");
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
        }
        
        _logger.LogDebug("{Name}: Found {Num} results", Name, localResultList.Count);

        return localResultList;
    }

    private static bool HasMultipleResults(HtmlDocument doc)
    {
        var results = doc.DocumentNode.SelectNodes(Selector);
        return results.Count > 1;
    }

    /// <summary>
    ///     Returns page with search results and true if there are multiple results or false if only one.
    ///     If nothing was found returns empty page and false.
    /// </summary>
    /// <param name="globalId">global id</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <returns>bool, HtmlDocument pair</returns>
    private async Task<(bool, HtmlDocument)> GetSearchResultsPage(string globalId,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("{Name}: searching for id: {Id}", Name, globalId);
        var doc = new HtmlDocument();
        if (string.IsNullOrEmpty(globalId)) return (false, doc);
        var url = string.Format(SearchUrl, globalId);
        var html = await GetHtml(url, cancellationToken);
        doc.LoadHtml(html);
        if (string.IsNullOrEmpty(doc.Text)) return (false, doc);

        return doc.DocumentNode.InnerText.Contains(NoResults)
            ? (false, new HtmlDocument())
            : (HasMultipleResults(doc), doc);
    }

    /// <summary>
    ///     Returns result page for specific scraper id or empty page if not found.
    /// </summary>
    /// <param name="scraperId">scraper id</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <returns>page for id</returns>
    private async Task<HtmlDocument> GetSingleResult(string scraperId, CancellationToken cancellationToken)
    {
        _logger.LogDebug("{Name}: getting page for id: {Id}", Name, scraperId);
        var doc = new HtmlDocument();
        if (string.IsNullOrEmpty(scraperId)) return doc;
        var url = BaseUrl + scraperId;
        var html = await GetHtml(url, cancellationToken);
        doc.LoadHtml(html);
        return doc;
    }

    private static string GetFirstResultId(HtmlDocument doc)
    {
        return string.IsNullOrEmpty(doc.Text)
            ? string.Empty
            : doc.DocumentNode.SelectSingleNode(Selector).SelectSingleNode("div/a").GetAttributeValue("href", "")
                .Trim('/');
    }

    private static string GetScraperId(HtmlDocument doc)
    {
        return doc.DocumentNode.SelectSingleNode("//form[@name='form1']")?.GetAttributeValue("action", "")
            .Trim('/') ?? string.Empty;
    }

    private async Task<string> GetHtml(string url, CancellationToken cancellationToken, string? productId = null,
        string? transactionId = null)
    {
        _logger.LogDebug("{Name}: loading html from url: {Url}", Name, url);
        var cookies = new CookieContainer();
        var handler = new HttpClientHandler { CookieContainer = cookies };
        var client = new HttpClient(handler);
        var request = new HttpRequestMessage();
        request.RequestUri = new Uri(url);
        request.Method = HttpMethod.Get;
        if (transactionId != null) request.Headers.Referrer = new Uri(string.Format(Referer, productId, transactionId));

        try
        {
            var response = await client.SendAsync(request, cancellationToken);
            _logger.LogDebug("{Name}: GetHtml finished, status code: {Code}", Name, response.StatusCode);
            if (response.IsSuccessStatusCode)
            {
                var text = await response.Content.ReadAsStringAsync(cancellationToken);
                if (text.Contains(AgeCheck))
                {
                    if (transactionId != null)
                    {
                        _logger.LogError("{Name}: AgeCheck failed", Name);
                        return string.Empty;
                    }

                    var query = response.RequestMessage?.RequestUri?.Query;
                    if (query != null && query.Contains(TransactionId))
                    {
                        var id = query.Split(TransactionId)[0].Trim('?', '&').Split('=')[1].Trim('/');
                        // post to agecheck
                        var parameters = new List<KeyValuePair<string, string>>
                        {
                            new("mode", "confirm"),
                            new("url", "/" + id)
                        };
                        response = await client.PostAsync(AgeCheckUrl, new FormUrlEncodedContent(parameters),
                            cancellationToken);
                        text = await response.Content.ReadAsStringAsync(cancellationToken);
                        if (text.Contains(AgeCheck))
                        {
                            _logger.LogError("{Name}: AgeCheck failed", Name);
                            return string.Empty;
                        }

                        return text;
                    }
                }

                return text;
            }

            return string.Empty;
        }
        catch (Exception e)
        {
            _logger.LogError("{Name}: could not load page {Url}\n{Message}", Name, url, e.Message);
            return string.Empty;
        }
    }
}