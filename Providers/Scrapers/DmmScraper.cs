using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using HtmlAgilityPack;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.IvInfo.Providers.Scrapers;

// ReSharper disable once UnusedType.Global
// ReSharper disable once ClassNeverInstantiated.Global
public class DmmScraper : IScraper
{
    internal const string Name = nameof(DmmScraper);

    private const string DomainUrl = "https://www.dmm.co.jp/";
    private const string SearchUrl = DomainUrl + "search/=/searchstr={0}";
    private const string ProxyDomain = "https://jppx.azurewebsites.net/";
    private const string ProxyUrl = ProxyDomain + "browse.php?u={0}&b=8";
    private const string NoPage = "404 Not Found";
    private const string NoResults = "に一致する商品は見つかりませんでした";
    private const string MetadataSelector = "//table[@class='mg-b20']/tr/td[@class='nw']";
    private const string PublishDate = "発売日";
    private const string PublishDateRental = "貸出開始日";
    private const string Performer = "出演者";
    private const string Director = "監督";
    private const string Series = "シリーズ";
    private const string Maker = "メーカー";
    private const string Label = "レーベル";

    private readonly ILogger _logger;

    public DmmScraper(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    ///     Should we use proxy for loading DMM pages? Most of DMM product pages is not available and not possible to find
    ///     outside of Japan.
    /// </summary>
    private static bool UseProxy => IvInfo.Instance?.Configuration.DmmUseProxy ?? false;

    public bool Enabled => IvInfo.Instance?.Configuration.DmmScraperEnabled ?? false;
    public bool ImgEnabled => IvInfo.Instance?.Configuration.DmmImgEnabled ?? false;

    public int Priority => IvInfo.Instance?.Configuration.DmmScraperPriority ?? 1;

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
        }

        var html = await GetHtml(scraperId, cancellationToken);
        if (string.IsNullOrEmpty(html)) return false;
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        if (string.IsNullOrEmpty(doc.Text))
        {
            _logger.LogDebug("{Name}: searching returned empty page, error?", Name);
            return false;
        }

        var title = doc.DocumentNode
            .SelectSingleNode("//div[@class='area-headline group']/div[@class='hreview']/h1")
            ?.InnerText;
        var textDate = doc.DocumentNode.SelectNodes(MetadataSelector)
            ?.Where(n => n.InnerText.Contains(PublishDate)).FirstOrDefault()?.ParentNode.SelectNodes("td")
            .Last().InnerText.Trim();
        var datePresent = DateTime.TryParse(textDate, out var releaseDate);
        if (!datePresent)
        {
            textDate = doc.DocumentNode.SelectNodes(MetadataSelector)
                ?.Where(n => n.InnerText.Contains(PublishDateRental)).FirstOrDefault()?.ParentNode.SelectNodes("td")
                .Last().InnerText.Trim();
            datePresent = DateTime.TryParse(textDate, out releaseDate);
        }

        var director = doc.DocumentNode.SelectNodes(MetadataSelector)
            ?.Where(node => node.InnerText.Contains(Director)).FirstOrDefault()?.ParentNode?.SelectNodes("td")
            .Last()?.InnerText?.Trim('-');
        var maker = doc.DocumentNode.SelectNodes(MetadataSelector)
            ?.Where(node => node.InnerText.Contains(Maker)).FirstOrDefault()?.ParentNode?.SelectNodes("td").Last()
            ?.InnerText?.Trim('-');
        var label = doc.DocumentNode.SelectNodes(MetadataSelector)
            ?.Where(node => node.InnerText.Contains(Label)).FirstOrDefault()?.ParentNode?.SelectNodes("td").Last()
            ?.InnerText?.Trim('-');
        var series = doc.DocumentNode.SelectNodes(MetadataSelector)
            ?.Where(node => node.InnerText.Contains(Series)).FirstOrDefault()?.ParentNode?.SelectNodes("td").Last()
            ?.InnerText?.Trim('-');

        var description = doc.DocumentNode
            .SelectSingleNode("//div[@class='clear mg-b20 lh4']/p[@class='mg-t0 mg-b20']")
            ?.InnerText.Trim();
        if (string.IsNullOrEmpty(description))
            description = doc.DocumentNode
                .SelectSingleNode("//div[@class='mg-b20 lh4']/p[@class='mg-b20']")
                ?.InnerText.Trim();

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
        if (!string.IsNullOrEmpty(label) && !metadata.Item.Studios.Contains(label))
            metadata.Item.AddStudio(label);
        if (!string.IsNullOrEmpty(maker) && !metadata.Item.Studios.Contains(maker))
            metadata.Item.AddStudio(maker);

        if (!string.IsNullOrEmpty(series) && string.IsNullOrEmpty(metadata.Item.CollectionName))
            metadata.Item.CollectionName = series;

        if (!string.IsNullOrWhiteSpace(director) && !metadata.People.Exists(p => p.Name == director))
            metadata.AddPerson(new PersonInfo
            {
                Name = director,
                Type = PersonType.Director
            });

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
            return result;
        }

        if (item.ImageInfos.Any(i => i.Type == imageType))
        {
            _logger.LogDebug("{Name}: {ImageType} image already exists, not overwriting", Name, imageType);
            return result;
        }

        var scraperId = item.GetProviderId(Name);
        if (string.IsNullOrEmpty(scraperId)) return result;

        _logger.LogDebug("{Name}: scraper id: {Id}", Name, scraperId);

        var doc = new HtmlDocument();
        var html = await GetHtml(scraperId, cancellationToken);
        if (string.IsNullOrEmpty(html)) return result;
        doc.LoadHtml(html);

        if (doc.DocumentNode.InnerText.Contains(NoPage)) return result;

        switch (imageType)
        {
            case ImageType.Primary:
                var frontUrl = doc.DocumentNode.SelectSingleNode("//a[@name='package-image']/img")
                    ?.GetAttributeValue("src", "") ?? doc.DocumentNode.SelectSingleNode("//img[@class='tdmm']")
                    ?.GetAttributeValue("src", "");
                if (frontUrl == null) break;
                result.Add(new RemoteImageInfo
                    { Url = GetProxyUrl(frontUrl), Type = imageType, ProviderName = Name });
                break;
            case ImageType.Box:
                var boxUrl = doc.DocumentNode.SelectSingleNode("//a[@name='package-image']")
                    ?.GetAttributeValue("href", "");
                if (boxUrl == null) break;
                result.Add(new RemoteImageInfo
                    { Url = GetProxyUrl(boxUrl), Type = imageType, ProviderName = Name });
                break;
            case ImageType.Screenshot:
                var screenshotNodes = doc.DocumentNode.SelectNodes("//div[@id='sample-image-block']/a/img");
                if (screenshotNodes == null) break;
                result.AddRange(screenshotNodes.Select(node => node.GetAttributeValue("src", "")).Select(scrUrl =>
                    new RemoteImageInfo
                        { Url = GetProxyUrl(scrUrl), Type = imageType, ProviderName = Name }));
                break;
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
        yield return ImageType.Screenshot;
    }

    private async Task<List<RemoteSearchResult>> GetSearchResults(IEnumerable<RemoteSearchResult> resultList,
        ItemLookupInfo info, string globalId, CancellationToken cancellationToken, bool firstOnly = false)
    {
        var localResultList = new List<RemoteSearchResult>(resultList);
        var doc = await GetSearchResultsPage(globalId, cancellationToken);
        if (doc == null) return localResultList;
        if (string.IsNullOrEmpty(doc.Text)) return localResultList;

        var nodeCollection =
            doc.DocumentNode.SelectNodes("//div[@class='d-item']/ul[@id='list']/li/div/p[@class='tmb']/a");
        foreach (var node in nodeCollection)
        {
            // browse.php?u=https%3A%2F%2Fwww.dmm.co.jp%2Fmono%2Fdvd%2F-%2Fdetail%2F%3D%2Fcid%3Dn_650ecr0089%2F&b=8
            // browse.php?u=https://www.dmm.co.jp/mono/dvd/-/detail/=/cid=n_650ecr0089/&b=8
            var url = HttpUtility.UrlDecode(node.GetAttributeValue("href", ""));
            var scraperId = url.Replace("/browse.php?u=", "").Replace("&amp;b=8", "");
            var tempGlobalId = globalId.ToLower().Replace("-", "");
            var regexp = $@"\/cid=(\w_\d+)?{tempGlobalId}\/";
            if (!Regex.IsMatch(scraperId.ToLower(), regexp))
                continue;
            var title = node.InnerText.Trim();
            var imgUrl = ProxyDomain + node.ChildNodes.FindFirst("img").GetAttributeValue("src", "");
            var nextIndex = localResultList.Count > 0 ? localResultList.Max(r => r.IndexNumber ?? 0) + 1 : 1;
            // var result = localResultList.Find(r => r.ProviderIds[IvInfoConstants.Name].Equals(globalId));
            // if (result == null)
            // {
            var result = new RemoteSearchResult
            {
                Name = title,
                ImageUrl = imgUrl,
                SearchProviderName = Name,
                IndexNumber = nextIndex,
                AlbumArtist = new RemoteSearchResult { Name = $"{Name}: {scraperId}/{globalId}" }
            };
            result.SetProviderId(IvInfoConstants.Name, globalId);
            localResultList.Add(result);
            // }

            result.SetProviderId(Name, scraperId);

            if (firstOnly && !info.IsAutomated) break;
        }

        return localResultList;
    }

    /// <summary>
    ///     Returns page with search results if there were any results for this global id.
    ///     If nothing was found returns null.
    /// </summary>
    /// <param name="globalId">global id</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <returns>HtmlDocument</returns>
    private async Task<HtmlDocument?> GetSearchResultsPage(string globalId, CancellationToken cancellationToken)
    {
        _logger.LogDebug("{Name}: searching for globalid: {Id}", Name, globalId);
        if (string.IsNullOrEmpty(globalId)) return null;
        var url = string.Format(SearchUrl, globalId);
        var html = await GetHtml(url, cancellationToken);
        if (string.IsNullOrEmpty(html)) return null;
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        return doc.DocumentNode.InnerText.Contains(NoResults) ? null : doc;
    }

    private async Task<string> GetHtml(string url, CancellationToken cancellationToken)
    {
        _logger.LogDebug("GetHtml: {Url}", url);
        var cookies = new CookieContainer();
        cookies.Add(new Uri(DomainUrl), new Cookie("age_check_done", "1"));
        cookies.Add(new Uri(DomainUrl), new Cookie("cklg", "ja"));
        if (UseProxy) cookies.Add(new Uri(ProxyDomain), new Cookie("c[dmm.co.jp][/][age_check_done]", "1"));

        var handler = new HttpClientHandler { CookieContainer = cookies };
        var request = new HttpRequestMessage();
        request.RequestUri = new Uri(GetProxyUrl(url));
        request.Method = HttpMethod.Get;
        var client = new HttpClient(handler);

        try
        {
            var response = await client.SendAsync(request, cancellationToken);
            _logger.LogDebug("{Name}: GetHtml finished, status code: {Code}", Name, response.StatusCode);
            return response.IsSuccessStatusCode
                ? await response.Content.ReadAsStringAsync(cancellationToken)
                : string.Empty;
        }
        catch (Exception e)
        {
            _logger.LogError("Could not load page {Url}\n{Message}", url, e.Message);
            return string.Empty;
        }
    }

    /// <summary>
    ///     Returns proxified url if needed, else returns url.
    /// </summary>
    /// <param name="url">Url to proxify</param>
    /// <returns>Processed url</returns>
    private string GetProxyUrl(string url)
    {
        var proxyUrl = url.Contains("browse.php?u=")
            ? ProxyDomain + url
            : string.Format(ProxyUrl, HttpUtility.UrlEncode(url));
        _logger.LogDebug("GetHtml: {Url} (proxified: {Proxified})", UseProxy ? proxyUrl : url, UseProxy);
        return UseProxy ? proxyUrl : url;
    }
}