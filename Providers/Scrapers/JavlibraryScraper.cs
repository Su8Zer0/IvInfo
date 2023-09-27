using System;
using System.Collections.Generic;
using System.Globalization;
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
// ReSharper disable once ClassNeverInstantiated.Global
public class JavlibraryScraper : IScraper
{
    internal const string Name = nameof(JavlibraryScraper);

    private const string DomainUrlEn = "https://www.javlibrary.com/en/";
    private const string DomainUrlJp = "https://www.javlibrary.com/ja/";
    private const string PageUrl = "?v={0}";
    private const string SearchUrl = "vl_searchbyid.php?keyword={0}";
    private const string BaseUrlJp = DomainUrlJp + SearchUrl;
    private const string NoResults = "ご指定の検索条件に合う項目がありませんでした";
    private const string MultipleResults = "品番検索結果";
    private const string NoImage = "img/noimage";

    private readonly ILogger _logger;

    public JavlibraryScraper(ILogger logger)
    {
        _logger = logger;
    }

    private static bool GetEngTitles => IvInfo.Instance?.Configuration.JavlibraryTitles ?? false;
    private static bool GetEngCastNames => IvInfo.Instance?.Configuration.JavlibraryCast ?? false;

    public bool Enabled => IvInfo.Instance?.Configuration.JavlibraryScraperEnabled ?? false;
    public bool ImgEnabled => IvInfo.Instance?.Configuration.JavlibraryImgEnabled ?? false;
    public int Priority => IvInfo.Instance?.Configuration.JavLibraryScraperPriority ?? 2;

    public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(IEnumerable<RemoteSearchResult> resultList,
        MovieInfo info, CancellationToken cancellationToken)
    {
        var globalId = info.GetProviderId(IvInfoConstants.Name) ?? IvInfoProvider.GetId(info);
        _logger.LogDebug("{Name}: searching for global id: {Id}", Name, globalId);
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

        HtmlDocument docJp;
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

            var results = await GetSearchResults(Array.Empty<RemoteSearchResult>(), info, globalId, cancellationToken);
            if (results.Count > 1 && !IScraper.FirstOnly)
            {
                _logger.LogDebug("{Name}: multiple results found and selecting first result not enabled - aborting",
                    Name);
                return false;
            }

            var result = results.FirstOrDefault();
            scraperId = result?.GetProviderId(Name);
            if (string.IsNullOrEmpty(scraperId)) return false;
            docJp = await GetSingleResult(scraperId, cancellationToken);
        }
        else
        {
            _logger.LogDebug("{Name}: searching for scraperid: {Id}", Name, scraperId);
            docJp = await GetSingleResult(scraperId, cancellationToken);
        }

        if (string.IsNullOrEmpty(docJp.Text))
        {
            _logger.LogDebug("{Name}: searching returned empty page, error?", Name);
            return false;
        }

        scraperId = GetScraperId(docJp);

        FillJpMetadata(metadata, docJp);

        if (GetEngTitles || GetEngCastNames) await FillEnMetadata(metadata, scraperId, cancellationToken);

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
        if (string.IsNullOrEmpty(scraperId))
        {
            _logger.LogDebug("{Name}: No scraper id", Name);
            return result;
        }

        var doc = await GetSingleResult(scraperId, cancellationToken);
        var url = doc.DocumentNode?.SelectSingleNode("//img[@id='video_jacket_img']")
            ?.GetAttributeValue("src", null);
        if (string.IsNullOrEmpty(url) || url.Contains(NoImage)) return result;
        if (!url.StartsWith("http")) url = "https:" + url;

        if (imageType == ImageType.Primary) url = url.Replace("pl.jpg", "ps.jpg");

        result.Add(new RemoteImageInfo
            { Url = url, Type = imageType, ProviderName = Name });

        _logger.LogDebug("{Name}: image searching finished", Name);
        return result;
    }

    public IEnumerable<ImageType> HandledImageTypes()
    {
        yield return ImageType.Primary;
        yield return ImageType.Box;
    }

    private void FillJpMetadata(MetadataResult<Movie> metadata, HtmlDocument doc)
    {
        var globalId = metadata.Item.GetProviderId(IvInfoConstants.Name);
        var scraperId = GetScraperId(doc);

        var titleJp = doc.DocumentNode.SelectNodes("//div[@id='video_title']/*/a")?.FindFirst("a")?.InnerText;
        titleJp = titleJp?.Replace(globalId ?? "", "").Trim();
        var releaseDateExists = DateTime.TryParse(
            doc.DocumentNode.SelectSingleNode("//div[@id='video_date']/table/tr/td[@class='text']")
                .InnerText, out var releaseDate);
        var castJp = doc.DocumentNode?.SelectNodes("//span[@class='cast']/span[@class='star']")
            ?.Where(node => !string.IsNullOrWhiteSpace(node.InnerText)).ToList()
            .ConvertAll(input => input.InnerText.Trim());
        var genres = doc.DocumentNode?.SelectNodes("//span[@class='genre']")?.ToList()
            .ConvertAll(input => input.InnerText.Trim()).ToArray();
        var label = doc.DocumentNode?.SelectSingleNode("//span[@class='label']/a")?.InnerText?.Trim();
        var maker = doc.DocumentNode?.SelectSingleNode("//span[@class='maker']/a")?.InnerText?.Trim();
        var director = doc.DocumentNode?.SelectSingleNode("//span[@class='director']/a")?.InnerText?.Trim();
        var scoreText = doc.DocumentNode?.SelectSingleNode("//span[@class='score']")?.InnerText?.Replace("(", "")
            .Replace(")", "");
        var score = string.IsNullOrWhiteSpace(scoreText)
            ? -1
            : float.Parse(scoreText, CultureInfo.InvariantCulture.NumberFormat);

        if (!string.IsNullOrEmpty(titleJp) && (string.IsNullOrWhiteSpace(metadata.Item.Name) || IScraper.Overwriting))
            metadata.Item.Name = titleJp;
        if (releaseDateExists && (metadata.Item.PremiereDate == null || IScraper.Overwriting))
            metadata.Item.PremiereDate = releaseDate;
        if (releaseDateExists && (metadata.Item.ProductionYear == null || IScraper.Overwriting))
            metadata.Item.ProductionYear = releaseDate.Year;
        if (score > -1 && (metadata.Item.CommunityRating == null || IScraper.Overwriting))
            metadata.Item.CommunityRating = score;
        if (string.IsNullOrWhiteSpace(metadata.Item.OfficialRating) || IScraper.Overwriting)
            metadata.Item.OfficialRating = "R";
        if (string.IsNullOrWhiteSpace(metadata.Item.ExternalId) || IScraper.Overwriting)
            metadata.Item.ExternalId = scraperId;
        if (!string.IsNullOrEmpty(label) && !metadata.Item.Studios.Contains(label))
            metadata.Item.AddStudio(label);
        if (!string.IsNullOrEmpty(maker) && !metadata.Item.Studios.Contains(maker))
            metadata.Item.AddStudio(maker);
        if (!string.IsNullOrEmpty(label) &&
            (string.IsNullOrEmpty(metadata.Item.CollectionName) || IScraper.Overwriting))
            metadata.Item.CollectionName = label;

        if (genres != null && (metadata.Item.Genres.Length == 0 || IScraper.Overwriting))
            foreach (var genre in genres)
                if (!metadata.Item.Genres.Contains(genre))
                    metadata.Item.AddGenre(genre);


        if (!string.IsNullOrWhiteSpace(director) &&
            (metadata.People == null || !metadata.People.Exists(p => p.Name == director) || IScraper.Overwriting))
            metadata.AddPerson(new PersonInfo
            {
                Name = director,
                Type = PersonType.Director
            });

        if (castJp == null ||
            (metadata.People != null && string.IsNullOrWhiteSpace(director) && !IScraper.Overwriting)) return;

        foreach (var person in castJp.Where(person => !metadata.People?.Exists(p => p.Name == person) ?? true))
            metadata.AddPerson(new PersonInfo
            {
                Name = person,
                Type = PersonType.Actor
            });
    }

    private async Task FillEnMetadata(MetadataResult<Movie> metadata, string scraperId,
        CancellationToken cancellationToken)
    {
        var globalId = metadata.Item.GetProviderId(IvInfoConstants.Name);
        var urlEn = DomainUrlEn + string.Format(PageUrl, scraperId);
        var docEn = new HtmlDocument();
        docEn.LoadHtml(await GetHtml(urlEn, cancellationToken));

        var titleEn = docEn.DocumentNode?.SelectNodes("//div[@id='video_title']/*/a")?.FindFirst("a")?.InnerText;
        titleEn = titleEn?.Replace(globalId ?? "", "").Trim();
        var director = docEn.DocumentNode?.SelectSingleNode("//span[@class='director']/a")?.InnerText?.Trim();
        var castEn = docEn.DocumentNode?.SelectNodes("//span[@class='cast']/span[@class='star']")?.Where(node =>
            !string.IsNullOrWhiteSpace(node.InnerText)).ToList().ConvertAll(input => input.InnerText.Trim());

        if (!string.IsNullOrEmpty(titleEn) &&
            (string.IsNullOrWhiteSpace(metadata.Item.OriginalTitle) || IScraper.Overwriting) && GetEngTitles)
            metadata.Item.OriginalTitle = titleEn;

        if (!GetEngCastNames) return;

        if (!string.IsNullOrWhiteSpace(director))
        {
            var dir = metadata.People.First(p => p.Type == PersonType.Director);
            if (dir != null) dir.Role = director;
        }

        if (castEn == null) return;
        if (!string.IsNullOrWhiteSpace(director))
            castEn.Insert(0, director);

        var castJp = metadata.People;
        for (var i = 0; i < castJp.Count; i++)
            if (metadata.People[i].Type == PersonType.Actor)
                metadata.People[i].Role = castEn[i];
    }

    private async Task<List<RemoteSearchResult>> GetSearchResults(IEnumerable<RemoteSearchResult> resultList,
        ItemLookupInfo info, string globalId, CancellationToken cancellationToken, bool firstOnly = false)
    {
        var localResultList = new List<RemoteSearchResult>(resultList);
        var (multiple, doc) = await GetSearchResultsOrResultPage(globalId, cancellationToken);
        if (string.IsNullOrEmpty(doc.Text)) return localResultList;

        if (multiple)
        {
            var nodeCollection = doc.DocumentNode.SelectNodes("//div[@class='videos']/div[@class='video']/a");
            foreach (var node in nodeCollection)
            {
                var scraperId = node.GetAttributeValue("href", "").Split("=")[1];
                var foundGlobalId = node.ChildNodes.FindFirst("div").InnerText;
                var title = node.LastChild.InnerText.Replace(globalId, "").Trim();
                var imgUrl = node.ChildNodes.FindFirst("img").GetAttributeValue("src", "");
                var nextIndex = localResultList.Count > 0 ? localResultList.Max(r => r.IndexNumber ?? 0) + 1 : 1;
                var result = new RemoteSearchResult
                {
                    Name = title,
                    ImageUrl = imgUrl,
                    SearchProviderName = Name,
                    Overview = $"{foundGlobalId}<br />{scraperId}",
                    IndexNumber = nextIndex
                };
                result.SetProviderId(Name, scraperId);
                result.SetProviderId(IvInfoConstants.Name,
                    foundGlobalId != globalId ? $"{foundGlobalId}" : $"{globalId}|{scraperId}");
                localResultList.Add(result);

                // IsAutomated is true when search is started manually using Identify command
                if (firstOnly && !info.IsAutomated) break;
            }
        }
        else
        {
            var scraperId = GetScraperId(doc);
            var title = doc.DocumentNode.SelectSingleNode("//div[@id='video_title']/*/a").InnerText
                .Replace(globalId, "").Trim();
            var imgUrl = doc.DocumentNode.SelectSingleNode("//img[@id='video_jacket_img']")
                .GetAttributeValue("src", null).Replace("pl.jpg", "ps.jpg");
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

    /// <summary>
    ///     Returns true and page with search results if there were multiple results for this global id.
    ///     When there were only one result returns false and result page. If nothing was found returns false and empty page.
    /// </summary>
    /// <param name="globalId">global id</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <returns>bool, HtmlDocument pair</returns>
    private async Task<(bool, HtmlDocument)> GetSearchResultsOrResultPage(string globalId,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("{Name}: searching for globalid: {Id}", Name, globalId);
        if (string.IsNullOrEmpty(globalId)) return (false, new HtmlDocument());
        var url = string.Format(BaseUrlJp, globalId);
        var html = await GetHtml(url, cancellationToken);
        if (string.IsNullOrEmpty(html)) return (false, new HtmlDocument());
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        if (doc.DocumentNode.InnerText.Contains(NoResults)) return (false, new HtmlDocument());

        return doc.DocumentNode.InnerText.Contains(MultipleResults) ? (true, doc) : (false, doc);
    }

    /// <summary>
    ///     Returns result page for specific scraper id or empty page if not found.
    /// </summary>
    /// <param name="scraperId">scraper id</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <returns>page for id</returns>
    private async Task<HtmlDocument> GetSingleResult(string scraperId, CancellationToken cancellationToken)
    {
        _logger.LogDebug("{Name}: getting page for scraper id: {Id}", Name, scraperId);
        var doc = new HtmlDocument();
        if (string.IsNullOrEmpty(scraperId)) return doc;
        var url = DomainUrlJp + string.Format(PageUrl, scraperId);
        var html = await GetHtml(url, cancellationToken);
        if (string.IsNullOrEmpty(html)) return doc;
        doc.LoadHtml(html);
        return doc;
    }

    private string GetScraperId(HtmlDocument doc)
    {
        _logger.LogDebug("{Name}: parsing scraperid", Name);
        return doc.DocumentNode.SelectSingleNode("//div[@id='video_title']/*/a")
            .GetAttributeValue("href", "").Split("=")[1];
    }

    private async Task<string> GetHtml(string url, CancellationToken cancellationToken)
    {
        _logger.LogDebug("{Name}: loading html from url: {Url}", Name, url);
        var cookies = new CookieContainer();
        cookies.Add(
            new Cookie("__cf_bm",
                "Hu9B4Rh_fEnJtQ870COFtsI5D2o_d5NKSR0CTRCjBMU-1650621637-0-ARs0jjHbPVj2Fs0Q+pwe6xYylUgq2ITKMU7W1aRvvpib6WMMM/trVUroA1SrLLQiEOCuhG5bHQ4Ybz+qeZFyJxVQxAhAeoAy2QZQUL59JOAkfmgewtrR2Kf1x/wzXd0zjQ==",
                "/", ".javlibrary.com"));
        cookies.Add(new Cookie("cf_clearance",
            "QuiobQ7bNToHC8orI_SCLBjzHgtviH2nPxMLIuzbC5M-1650621636-0-150", "/", ".javlibrary.com"));
        var handler = new HttpClientHandler { CookieContainer = cookies };
        var request = new HttpRequestMessage();
        var client = new HttpClient(handler);
        request.RequestUri = new Uri(url);
        request.Method = HttpMethod.Get;
        request.Headers.Referrer = new Uri("https://www.javlibrary.com/");
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
            _logger.LogError("{Name}: could not load page {Url}\n{Message}", Name, url, e.Message);
            return string.Empty;
        }
    }
}