using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Io;
using AngleSharp.XPath;
using FlareSolverrSharp;
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
public class JavlibraryScraper : IScraper
{
    public const string Name = nameof(JavlibraryScraper);

    private const string DomainUrl = "https://www.javlibrary.com/";
    private const string DomainUrlEn = DomainUrl + "en/";
    private const string DomainUrlJp = DomainUrl + "ja/";
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

    private static bool EngTitles => IvInfo.Instance?.Configuration.JavlibraryTitles ?? false;
    private static bool EngCastNames => IvInfo.Instance?.Configuration.JavlibraryCast ?? false;
    private static bool EngTags => IvInfo.Instance?.Configuration.JavlibraryTags ?? false;
    private static bool UseSolverr => IvInfo.Instance?.Configuration.JavlibraryUseSolverr ?? false;
    private static string SolverrUrl => IvInfo.Instance?.Configuration.JavlibrarySolverrUrl ?? string.Empty;

    public bool Enabled => IvInfo.Instance?.Configuration.JavlibraryScraperEnabled ?? false;
    public bool ImgEnabled => IvInfo.Instance?.Configuration.JavlibraryImgEnabled ?? false;
    public int Priority => IvInfo.Instance?.Configuration.JavLibraryScraperPriority ?? 3;

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

        IDocument? docJp;
        if (scraperId == null)
        {
            _logger.LogDebug("{Name}: no scraper id, searching for global id: {Id}", Name, globalId);
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

        if (string.IsNullOrEmpty(docJp?.Body?.Text()))
        {
            _logger.LogDebug("{Name}: searching returned empty page, error?", Name);
            return false;
        }

        scraperId = GetScraperId(docJp);

        var castJp = FillJpMetadata(metadata, docJp);

        if (EngTitles || EngCastNames || EngTags) await FillEnMetadata(metadata, scraperId, castJp, cancellationToken);

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
        if (string.IsNullOrEmpty(doc?.Body?.Text())) return result;
        var url = (doc.Body.SelectSingleNode("//img[@id='video_jacket_img']") as IHtmlImageElement)
            ?.Source;
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

    private List<string>? FillJpMetadata(MetadataResult<Movie> metadata, IDocument doc)
    {
        var globalId = metadata.Item.GetProviderId(IvInfoConstants.Name);
        var scraperId = GetScraperId(doc);

        var titleJp = doc.Body.SelectSingleNode("//div[@id='video_title']/*/a")?.Text();
        titleJp = titleJp?.Replace(globalId ?? "", "").Trim();
        var releaseDateExists = DateTime.TryParse(
            doc.Body.SelectSingleNode("//div[@id='video_date']/table/tbody/tr/td[@class='text']")?
                .Text(), out var releaseDate);
        var castJp = doc.Body.SelectNodes("//span[@class='cast']/span[@class='star']")
            ?.Where(node => !string.IsNullOrWhiteSpace(node.Text())).ToList()
            .ConvertAll(input => input.Text().Trim());
        var genres = doc.Body.SelectNodes("//span[@class='genre']")?.ToList()
            .ConvertAll(input => input.Text().Trim()).ToArray();
        var label = doc.Body.SelectSingleNode("//span[@class='label']/a")?.Text().Trim();
        var maker = doc.Body.SelectSingleNode("//span[@class='maker']/a")?.Text().Trim();
        var director = doc.Body.SelectSingleNode("//span[@class='director']/a")?.Text().Trim();
        var scoreText = doc.Body.SelectSingleNode("//span[@class='score']")?.Text().Replace("(", "")
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

        if (genres != null && (metadata.Item.Genres.Length == 0 || IScraper.Overwriting) && !EngTags)
            foreach (var genre in genres)
                if (!metadata.Item.Genres.Contains(genre))
                    metadata.Item.AddGenre(genre);


        if (!string.IsNullOrWhiteSpace(director) &&
            (metadata.People == null || metadata.People.All(p => p.Name != director) || IScraper.Overwriting))
        {
            metadata.AddPerson(new PersonInfo
            {
                Name = director,
                Type = PersonKind.Director
            });
        }

        if (castJp == null ||
            (metadata.People != null && string.IsNullOrWhiteSpace(director) && !IScraper.Overwriting)) return null;

        foreach (var person in castJp.Where(person => !metadata.People?.Any(p => p.Name == person) ?? true))
        {
            metadata.AddPerson(new PersonInfo
            {
                Name = person,
                Type = PersonKind.Actor
            });
        }

        return castJp;
    }

    private async Task FillEnMetadata(MetadataResult<Movie> metadata, string scraperId, IReadOnlyList<string>? castJp,
        CancellationToken cancellationToken)
    {
        var globalId = metadata.Item.GetProviderId(IvInfoConstants.Name);
        var urlEn = DomainUrlEn + string.Format(PageUrl, scraperId);
        var docEn = await GetHtml(urlEn, cancellationToken);

        if (string.IsNullOrEmpty(docEn?.Body?.Text())) return;

        var titleEn = docEn.Body.SelectSingleNode("//div[@id='video_title']/*/a").Text();
        titleEn = titleEn.Replace(globalId ?? "", "").Trim();
        var genres = docEn.Body.SelectNodes("//span[@class='genre']")?.ToList()
            .ConvertAll(input => input.Text().Trim()).ToArray();
        var director = docEn.Body.SelectSingleNode("//span[@class='director']/a")?.Text().Trim();
        var castEn = docEn.Body.SelectNodes("//span[@class='cast']/span[@class='star']")?.Where(node =>
            !string.IsNullOrWhiteSpace(node.Text())).ToList().ConvertAll(input => input.Text().Trim());

        if (!string.IsNullOrEmpty(titleEn) &&
            (string.IsNullOrWhiteSpace(metadata.Item.OriginalTitle) || IScraper.Overwriting) && EngTitles)
            metadata.Item.OriginalTitle = titleEn;

        if (genres != null && (metadata.Item.Genres.Length == 0 || IScraper.Overwriting) && EngTags)
            foreach (var genre in genres)
                if (!metadata.Item.Genres.Contains(genre))
                    metadata.Item.AddGenre(genre);

        if (!EngCastNames) return;

        if (!string.IsNullOrWhiteSpace(director))
        {
            var dir = metadata.People.First(p => p.Type == PersonKind.Director);
            if (dir != null) dir.Role = director;
        }

        if (castEn == null || castJp == null) return;
        for (var i = 0; i < castJp.Count; i++)
        {
            var person = metadata.People.First(p => p.Name == castJp[i]);
            if (person is { Type: PersonKind.Actor })
                person.Role = castEn[i];
        }
    }

    private async Task<List<RemoteSearchResult>> GetSearchResults(IEnumerable<RemoteSearchResult> resultList,
        ItemLookupInfo info, string globalId, CancellationToken cancellationToken, bool firstOnly = false)
    {
        var localResultList = new List<RemoteSearchResult>(resultList);
        var (multiple, doc) = await GetSearchResultsOrResultPage(globalId, cancellationToken);
        if (string.IsNullOrEmpty(doc?.Body?.Text())) return localResultList;

        if (multiple)
        {
            var nodeCollection = doc.Body.SelectNodes("//div[@class='videos']/div[@class='video']/a");
            foreach (var node in nodeCollection.OfType<IHtmlAnchorElement>())
            {
                var scraperId = node.Href.Split("=")[1];
                var foundGlobalId = node.SelectSingleNode("div").Text();
                var title = node.LastChild?.Text().Replace(globalId, "").Trim();
                var imgUrl = (node.SelectSingleNode("img") as IHtmlImageElement)?.Source;
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
            var title = doc.Body.SelectSingleNode("//div[@id='video_title']/*/a").Text()
                .Replace(globalId, "").Trim();
            var imgUrl = (doc.Body.SelectSingleNode("//img[@id='video_jacket_img']") as IHtmlImageElement)?
                .Source?.Replace("pl.jpg", "ps.jpg");
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
    private async Task<(bool, IDocument?)> GetSearchResultsOrResultPage(string globalId,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("{Name}: searching for globalid: {Id}", Name, globalId);
        if (string.IsNullOrEmpty(globalId)) return (false, null);
        var url = string.Format(BaseUrlJp, globalId);
        var doc = await GetHtml(url, cancellationToken);
        if (string.IsNullOrEmpty(doc?.Body?.Text())) return (false, null);
        if (doc.Body.Text().Contains(NoResults)) return (false, null);

        return doc.Body.Text().Contains(MultipleResults) ? (true, doc) : (false, doc);
    }

    /// <summary>
    ///     Returns result page for specific scraper id or empty page if not found.
    /// </summary>
    /// <param name="scraperId">scraper id</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <returns>page for id</returns>
    private async Task<IDocument?> GetSingleResult(string scraperId, CancellationToken cancellationToken)
    {
        _logger.LogDebug("{Name}: getting page for scraper id: {Id}", Name, scraperId);
        if (string.IsNullOrEmpty(scraperId)) return null;
        var url = DomainUrlJp + string.Format(PageUrl, scraperId);
        var doc = await GetHtml(url, cancellationToken);
        return string.IsNullOrEmpty(doc?.Body?.Text()) ? null : doc;
    }

    private string GetScraperId(IDocument doc)
    {
        _logger.LogDebug("{Name}: parsing scraperid", Name);
        return (doc.Body.SelectSingleNode("//div[@id='video_title']/*/a") as IHtmlAnchorElement)?.Href.Split("=")[1] ??
               string.Empty;
    }

    private async Task<IDocument?> GetHtml(string url, CancellationToken cancellationToken)
    {
        _logger.LogDebug("{Name}: loading html from url: {Url}", Name, url);

        IDocument document;
        var config = AngleSharp.Configuration.Default.WithDefaultLoader(new LoaderOptions { IsResourceLoadingEnabled = true }).WithXPath();
        var context = BrowsingContext.New(config);

        if (UseSolverr && !string.IsNullOrEmpty(SolverrUrl))
        {
            _logger.LogDebug("{Name}: using FlareSolverr at: {Url}", Name, SolverrUrl);
            var handler = new ClearanceHandler(SolverrUrl)
            {
                MaxTimeout = 40000
            };
            var client = new HttpClient(handler);
            client.Timeout = TimeSpan.FromSeconds(40);
            var content = await client.GetStringAsync(url, cancellationToken);
            
            document = await context.OpenAsync(req => req.Content(content), cancellationToken);    
        }
        else
        {
            document = await context.OpenAsync(url, cancellationToken);
        }
        
        try
        {
            _logger.LogDebug("{Name}: GetHtml finished, status code: {Code}", Name, document.StatusCode);
            return document.StatusCode == HttpStatusCode.OK
                ? document
                : null;
        }
        catch (Exception e)
        {
            _logger.LogError("{Name}: could not load page {Url}\n{Message}", Name, url, e.Message);
            return null;
        }
    }
}