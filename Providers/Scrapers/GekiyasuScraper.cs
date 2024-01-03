using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Io;
using AngleSharp.XPath;
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

        IDocument? doc;
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

        if (string.IsNullOrEmpty(doc?.Body?.Text()))
        {
            _logger.LogDebug("{Name}: searching returned empty page, error?", Name);
            return false;
        }

        scraperId = GetScraperId(doc);

        var title = doc.Body.SelectSingleNode("//div[@class='b-title']/strong")?.Text();
        var datePresent = DateTime.TryParse(
            doc.Body.SelectSingleNode("//div[@class='b-releasedate']/span")?.Text(),
            out var releaseDate);
        var description = doc.Body.SelectSingleNode("//div[@id='detailarea']/div[@class='well']/span")
            ?.Text();
        var maker = doc.Body.SelectNodes("//ul[@class='b-relative']/li/a")
            ?.Where(node => node.Text() == MakerLbl).FirstOrDefault()?.Parent?.LastChild?.Text();

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
        var doc = await GetHtml(url, cancellationToken);

        if (doc?.Body?.Text() == null) return result;

        if (doc.Body.Text().Contains(NoResults)) return result;

        var thumbUrl = BaseUrl +
                       (doc.Body.SelectSingleNode("//a[@class='thumbnail']") as IHtmlAnchorElement)?.GetAttribute(
                           "href");
        var boxUrl = BaseUrl + (doc.Body.SelectSingleNode("//img[@class='img-responsive']") as IHtmlAnchorElement)
            ?.GetAttribute("src");

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
        if (string.IsNullOrEmpty(doc?.Body?.Text())) return localResultList;
        if (multiple)
        {
            var nodeCollection = doc.Body.SelectNodes(Selector) ?? new List<INode>();
            foreach (var node in nodeCollection)
            {
                var scraperId = node.ChildNodes.QuerySelector("a")?.Attributes.GetNamedItem("href")?.Value.Trim('/');
                if (string.IsNullOrEmpty(scraperId)) continue;
                var title = node.ChildNodes.QuerySelector("p")?.Text();
                var imgUrl = BaseUrl + node.ChildNodes.QuerySelector("a")?.Attributes.GetNamedItem("src")?.Value;
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
            if (string.IsNullOrEmpty(html?.Body?.Text())) return localResultList;
            var title = html.Body.SelectSingleNode("//div[@class='b-title']/strong")?.Text();
            var imgUrl = BaseUrl + (html.Body.SelectSingleNode("//a[@class='thumbnail']") as IHtmlAnchorElement)
                ?.Attributes.GetNamedItem("href")?.Value;
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

    private static bool HasMultipleResults(IDocument doc)
    {
        var results = doc.Body.SelectNodes(Selector);
        return results.Count > 1;
    }

    /// <summary>
    ///     Returns page with search results and true if there are multiple results or false if only one.
    ///     If nothing was found returns empty page and false.
    /// </summary>
    /// <param name="globalId">global id</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <returns>bool, HtmlDocument pair</returns>
    private async Task<(bool, IDocument?)> GetSearchResultsPage(string globalId,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("{Name}: searching for id: {Id}", Name, globalId);
        if (string.IsNullOrEmpty(globalId)) return (false, null);
        var url = string.Format(SearchUrl, globalId);
        var doc = await GetHtml(url, cancellationToken);
        if (doc?.Body?.Text() == null) return (false, null);
        if (string.IsNullOrEmpty(doc.Body.Text())) return (false, doc);

        return doc.Body.Text().Contains(NoResults)
            ? (false, null)
            : (HasMultipleResults(doc), doc);
    }

    /// <summary>
    ///     Returns result page for specific scraper id or empty page if not found.
    /// </summary>
    /// <param name="scraperId">scraper id</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <returns>page for id</returns>
    private async Task<IDocument?> GetSingleResult(string scraperId, CancellationToken cancellationToken)
    {
        _logger.LogDebug("{Name}: getting page for id: {Id}", Name, scraperId);
        if (string.IsNullOrEmpty(scraperId)) return null;
        var url = BaseUrl + scraperId;
        return await GetHtml(url, cancellationToken);
    }

    private static string GetFirstResultId(IDocument? doc)
    {
        return (string.IsNullOrEmpty(doc?.Body?.Text())
            ? string.Empty
            : doc.Body.SelectSingleNode(Selector).ChildNodes.QuerySelector("div a")?.Attributes.GetNamedItem("href")?
                .Value.Trim('/')) ?? string.Empty;
    }

    private static string GetScraperId(IDocument doc)
    {
        var node = doc.Body.SelectSingleNode("//form[@name='form1']");
        if (node is not IHtmlFormElement element) return string.Empty;
        return element.GetAttribute("action")?.Trim('/') ?? string.Empty;
    }

    private async Task<IDocument?> GetHtml(string url, CancellationToken cancellationToken, string? productId = null,
        string? transactionId = null)
    {
        _logger.LogDebug("{Name}: loading html from url: {Url}", Name, url);
        var request = new DocumentRequest(new Url(url));
        if (transactionId != null) request.Headers.Add("Referrer", string.Format(Referer, productId, transactionId));

        var config = AngleSharp.Configuration.Default.WithDefaultCookies().WithMetaRefresh()
            .WithDefaultLoader(new LoaderOptions { IsResourceLoadingEnabled = true }).WithXPath();
        var context = BrowsingContext.New(config);
        var document = await context.OpenAsync(request, cancellationToken);
        if (document.Body?.Text() == null) return null;

        try
        {
            _logger.LogDebug("{Name}: GetHtml finished, status code: {Code}", Name, document.StatusCode);
            if (document.StatusCode != HttpStatusCode.OK) return null;
            if (!document.Body.Text().Contains(AgeCheck)) return document;

            if (transactionId != null)
            {
                _logger.LogError("{Name}: AgeCheck failed", Name);
                return null;
            }

            if (!document.Url.Contains(TransactionId)) return document;
            var id = document.Url.Split(TransactionId)[0].Trim('?', '&').Split('=')[1].Trim('/');
            // post to agecheck
            if (document.Body.SelectSingleNode("form['form1']") is not IHtmlFormElement form)
            {
                _logger.LogError("{Name}: AgeCheck failed, form not found", Name);
                return null;
            }
            foreach (var formElement in form.Elements.OfType<IHtmlInputElement>())
            {
                formElement.Value = formElement.Name switch
                {
                    "mode" => "confirm",
                    "url" => "/" + id,
                    _ => formElement.Value
                };
            }

            form.Action = AgeCheckUrl;

            document = await form.SubmitAsync();

            if (document.Body?.Text() == null)
            {
                _logger.LogError("{Name}: AgeCheck failed", Name);
                return null;
            }

            if (!document.Body.Text().Contains(AgeCheck)) return document;
            
            _logger.LogError("{Name}: AgeCheck failed", Name);
            return null;
        }
        catch (Exception e)
        {
            _logger.LogError("{Name}: could not load page {Url}\n{Message}", Name, url, e.Message);
            return null;
        }
    }
}