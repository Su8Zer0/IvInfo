using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Io;
using AngleSharp.XPath;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace IvInfo.Providers.Scrapers;

// ReSharper disable once UnusedType.Global
// ReSharper disable once ClassNeverInstantiated.Global
public class DmmScraper : IScraper
{
    public const string Name = nameof(DmmScraper);

    private const string DomainUrl = "https://www.dmm.co.jp/";
    private const string SearchUrl = DomainUrl + "search/=/searchstr={0}";
    private const string NoPage = "404 Not Found";
    private const string NoResults = "に一致する商品は見つかりませんでした";
    private const string MetadataSelector = "//table[@class='mg-b20']/tbody/tr/td[@class='nw']";
    private const string PublishDate = "発売日";
    private const string PublishDateRental = "貸出開始日";
    private const string Director = "監督";
    private const string Series = "シリーズ";
    private const string Maker = "メーカー";
    private const string Label = "レーベル";
    private const string Expand = "すべて表示する";

    private readonly ILogger _logger;

    public DmmScraper(ILogger logger)
    {
        _logger = logger;
    }

    public bool Enabled => IvInfo.Instance?.Configuration.DmmScraperEnabled ?? false;
    public bool ImgEnabled => IvInfo.Instance?.Configuration.DmmImgEnabled ?? false;

    private static bool GetTrailers => IvInfo.Instance?.Configuration.DmmGetTrailers ?? false;

    public int Priority => IvInfo.Instance?.Configuration.DmmScraperPriority ?? 4;

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

        var doc = await GetHtml(scraperId, cancellationToken);
        if (doc?.Body == null) return false;

        if (string.IsNullOrEmpty(doc.Body.Text()))
        {
            _logger.LogDebug("{Name}: searching returned empty page, error?", Name);
            return false;
        }

        var title = doc.Body.SelectSingleNode("//div[@class='area-headline group']/div[@class='hreview']/h1")
            ?.Text();
        var textDate = doc.Body.SelectNodes(MetadataSelector)
            ?.Where(n => n.Text().Contains(PublishDate)).FirstOrDefault()?.ParentElement?.SelectNodes("td")
            .Last().Text().Trim();
        var datePresent = DateTime.TryParse(textDate, out var releaseDate);
        if (!datePresent)
        {
            textDate = doc.Body.SelectNodes(MetadataSelector)
                ?.Where(n => n.Text().Contains(PublishDateRental)).FirstOrDefault()?.ParentElement.SelectNodes("td")
                .Last().Text().Trim();
            datePresent = DateTime.TryParse(textDate, out releaseDate);
        }

        var performers = doc.Body.QuerySelector("span#performer")?.SelectNodes("a").ConvertAll(href => href.Text())
            .Where(item => !item.Contains(Expand)).ToList();
        var director = doc.Body.SelectNodes(MetadataSelector)
            ?.Where(node => node.Text().Contains(Director)).FirstOrDefault()?.ParentElement?.SelectNodes("td")
            .Last()?.Text().Trim('-');
        var maker = doc.Body.SelectNodes(MetadataSelector)
            ?.Where(node => node.Text().Contains(Maker)).FirstOrDefault()?.ParentElement?.SelectNodes("td").Last()
            ?.Text().Trim('-');
        var label = doc.Body.SelectNodes(MetadataSelector)
            ?.Where(node => node.Text().Contains(Label)).FirstOrDefault()?.ParentElement?.SelectNodes("td").Last()
            ?.Text().Trim('-');
        var series = doc.Body.SelectNodes(MetadataSelector)
            ?.Where(node => node.Text().Contains(Series)).FirstOrDefault()?.ParentElement?.SelectNodes("td").Last()
            ?.Text().Trim('-');

        var description = doc.Body
            .SelectSingleNode("//div[@class='clear mg-b20 lh4']/p[@class='mg-t0 mg-b20']")
            ?.Text().Trim();
        if (string.IsNullOrEmpty(description))
            description = doc.Body
                .SelectSingleNode("//div[@class='mg-b20 lh4']/p[@class='mg-b20']")
                ?.Text().Trim();
        if (string.IsNullOrEmpty(description))
            description = doc.Body
                .SelectSingleNode("//div[@class='mg-b20 lh4']")
                ?.Text().Trim();

        var trailerUrl = await GetTrailerUrl(doc, cancellationToken);

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

        if (performers != null && performers.Any())
        {
            foreach (var performer in performers)
            {
                metadata.AddPerson(new PersonInfo
                {
                    Name = performer,
                    Type = PersonKind.Actor
                });
            }
        }

        if (!string.IsNullOrEmpty(director) && !metadata.People.Exists(p => p.Name == director))
            metadata.AddPerson(new PersonInfo
            {
                Name = director,
                Type = PersonKind.Director
            });

        if (!string.IsNullOrEmpty(trailerUrl))
            metadata.Item.AddTrailerUrl(trailerUrl);

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

        var doc = await GetHtml(scraperId, cancellationToken);
        if (doc?.Body == null) return result;

        if (doc.Body.Text().Contains(NoPage)) return result;

        switch (imageType)
        {
            case ImageType.Primary:
                var imgNode = doc.Body.SelectSingleNode("//img[@class='tdmm']");
                if (imgNode is not IHtmlImageElement image) break;
                var frontUrl = image.Source;
                if (frontUrl == null) break;
                result.Add(new RemoteImageInfo
                    { Url = frontUrl, Type = imageType, ProviderName = Name });
                break;
            case ImageType.Box:
                var boxNode = doc.Body.SelectSingleNode("//a[@name='package-image']");
                if (boxNode is not IHtmlAnchorElement anchor) break;
                var boxUrl = anchor.Href;
                result.Add(new RemoteImageInfo
                    { Url = boxUrl, Type = imageType, ProviderName = Name });
                break;
            case ImageType.Screenshot:
                var screenshotNodes = doc.Body.SelectNodes("//div[@id='sample-image-block']/a/img");
                if (screenshotNodes == null) break;
                var screenshots = screenshotNodes.Select(node => node as IHtmlImageElement).Where(img => img != null)
                    .Select(img =>
                        new RemoteImageInfo
                            { Url = img!.Source, Type = imageType, ProviderName = Name });

                result.AddRange(screenshots);
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
        if (doc?.Body == null) return localResultList;
        if (string.IsNullOrEmpty(doc.Body.Text())) return localResultList;

        var nodeCollection =
            doc.Body.SelectNodes("//div[@class='d-item']/ul[@id='list']/li/div/p[@class='tmb']/a");
        foreach (var node in nodeCollection)
        {
            if (node is not IHtmlAnchorElement anchor) continue;
            var scraperId = HttpUtility.UrlDecode(anchor.Href ?? "");
            var tempGlobalId = ProcessId(globalId).ToLower().Replace("-", "");
            var regexp = $@"\/cid=(\w_\d+)?{tempGlobalId}\/";
            if (!Regex.IsMatch(scraperId.ToLower(), regexp))
                continue;
            var title = anchor.Text().Trim();
            if (string.IsNullOrEmpty(title))
                title = anchor.ChildNodes.QuerySelector("img")?.GetAttribute("alt")?.Trim();
            var imgUrl = anchor.ChildNodes.QuerySelector("img")?.GetAttribute("src");
            var nextIndex = localResultList.Count > 0 ? localResultList.Max(r => r.IndexNumber ?? 0) + 1 : 1;
            var result = new RemoteSearchResult
            {
                Name = title,
                ImageUrl = imgUrl,
                SearchProviderName = Name,
                Overview = $"{globalId}<br />{scraperId}",
                IndexNumber = nextIndex
            };
            result.SetProviderId(IvInfoConstants.Name, globalId);
            localResultList.Add(result);

            result.SetProviderId(Name, scraperId);

            if (firstOnly && !info.IsAutomated) break;
        }

        _logger.LogDebug("{Name}: Found {Num} results", Name, localResultList.Count);

        return localResultList;
    }

    /// <summary>
    ///     Returns page with search results if there were any results for this global id.
    ///     If nothing was found returns null.
    /// </summary>
    /// <param name="globalId">global id</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <returns>HtmlDocument</returns>
    private async Task<IDocument?> GetSearchResultsPage(string globalId, CancellationToken cancellationToken)
    {
        _logger.LogDebug("{Name}: searching for globalid: {Id}", Name, globalId);
        if (string.IsNullOrEmpty(globalId)) return null;
        var url = string.Format(SearchUrl, ProcessId(globalId));
        var doc = await GetHtml(url, cancellationToken);
        if (doc?.Body == null) return null;
        return doc.Body.Text().Contains(NoResults) ? null : doc;
    }

    private static string ProcessId(string globalId)
    {
        var parts = globalId.Split("-");
        try
        {
            var str = parts[0];
            var id = int.Parse(parts[1]);
            var newId = id.ToString("00000");
            return $"{str}-{newId}";
        }
        catch (FormatException)
        {
            return globalId;
        }
    }

    private async Task<string?> GetTrailerUrl(IDocument document, CancellationToken cancellationToken)
    {
        if (!GetTrailers)
        {
            _logger.LogDebug("{Name}: Getting trailers disabled", Name);
            return string.Empty;
        }
        _logger.LogDebug("{Name}: GetTrailerUrl for", Name);
        var sampleMovieUrlNode = document.Body.SelectSingleNode("//div[@id='detail-sample-movie']/div/a");
        if (sampleMovieUrlNode is not IHtmlAnchorElement element) return null;
        var sampleMovieScript = element.Attributes.GetNamedItem("onclick")?.Value;
        if (string.IsNullOrEmpty(sampleMovieScript)) return null;
        var start = sampleMovieScript.IndexOf("sampleplay('/", StringComparison.Ordinal) + "sampleplay('/".Length;
        var end = sampleMovieScript.IndexOf("'); return false;", start, StringComparison.Ordinal);
        var sampleUrl = string.Concat(DomainUrl, sampleMovieScript.AsSpan(start, end - start));
        var playerDoc = await GetHtml(sampleUrl, cancellationToken);
        if (playerDoc == null) return null;
        var iframe = playerDoc.QuerySelector("iframe");
        var iframeSrc = iframe?.Attributes.GetNamedItem("src")?.Value;
        if (string.IsNullOrEmpty(iframeSrc)) return null;
        playerDoc = await GetHtml(iframeSrc, cancellationToken);
        var scripts = playerDoc?.QuerySelectorAll("script");
        var oneScript = scripts?.First(s => s.InnerHtml.Contains("litevideo"));
        if (oneScript == null) return null;
        start = oneScript.InnerHtml.IndexOf("const args = {", StringComparison.Ordinal) + "const args = ".Length;
        end = oneScript.InnerHtml.IndexOf(";", start, StringComparison.Ordinal);
        var json = oneScript.InnerHtml.Substring(start, end - start);
        var jsonDoc = JsonDocument.Parse(json);
        var src = jsonDoc.RootElement.GetProperty("src").GetString();
        return src != null && src.StartsWith("http") ? src : $"https:{src}";
    }

    private async Task<IDocument?> GetHtml(string url, CancellationToken cancellationToken)
    {
        _logger.LogDebug("GetHtml: {Url}", url);
        var cookieProvider = new MemoryCookieProvider();
        cookieProvider.SetCookie(new Url(DomainUrl), "age_check_done=1");
        cookieProvider.SetCookie(new Url(DomainUrl), "cklg=ja");

        var config = AngleSharp.Configuration.Default.With(cookieProvider)
            .WithDefaultLoader(new LoaderOptions { IsResourceLoadingEnabled = true }).WithXPath();
        var context = BrowsingContext.New(config);
        var document = await context.OpenAsync(url, cancellationToken);

        try
        {
            _logger.LogDebug("{Name}: GetHtml finished, status code: {Code}", Name, document.StatusCode);
            return document.StatusCode == HttpStatusCode.OK
                ? document
                : null;
        }
        catch (Exception e)
        {
            _logger.LogError("Could not load page {Url}\n{Message}", url, e.Message);
            return null;
        }
    }
}