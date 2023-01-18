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

namespace Jellyfin.Plugin.IvInfo.Providers.Scrapers
{
    // ReSharper disable once UnusedType.Global
    public class JavlibraryScraper : IScraper
    {
        private const string Name = nameof(JavlibraryScraper);

        private const string DomainUrlEn = "https://www.javlibrary.com/en/";
        private const string DomainUrlJa = "https://www.javlibrary.com/ja/";
        private const string PageUrl = "?v={0}";
        private const string SearchUrl = "vl_searchbyid.php?keyword={0}";
        private const string BaseUrlEn = DomainUrlEn + SearchUrl;
        private const string NoResults = "Search returned no result";
        private const string MultipleResults = "ID Search Result";
        private const string Selector = "";

        private readonly ILogger _logger;

        public JavlibraryScraper(ILogger logger)
        {
            _logger = logger;
        }

        private static bool FirstOnly => IvInfo.Instance?.Configuration.FirstOnly ?? false;

        public int Priority => IvInfo.Instance?.Configuration.JavLibraryScraperPriority ?? 2;

        public bool Enabled => IvInfo.Instance?.Configuration.JavlibraryScraperEnabled ?? false;
        public bool ImgEnabled => IvInfo.Instance?.Configuration.JavlibraryImgEnabled ?? false;

        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(IEnumerable<RemoteSearchResult> resultList,
            MovieInfo info, CancellationToken cancellationToken)
        {
            var globalId = info.GetProviderId(IvInfoConstants.Name) ?? IvInfoProvider.GetId(info);
            _logger.LogDebug("{Name}: searching for id: {Id}", Name, globalId);
            if (string.IsNullOrEmpty(globalId)) return resultList;

            return await GetSearchResults(resultList, globalId, cancellationToken, FirstOnly);
        }

        public async Task<bool> FillMetadata(MetadataResult<Movie> metadata, CancellationToken cancellationToken)
        {
            var scraperId = metadata.Item.GetProviderId(Name);
            var globalId = metadata.Item.GetProviderId(IvInfoConstants.Name) ??
                           IvInfoProvider.GetId(metadata.Item.GetLookupInfo());
            _logger.LogDebug("{Name}: searching for ids: {GlobalId}, {ScraperId}", Name, globalId, scraperId);

            HtmlDocument doc;
            if (scraperId == null)
            {
                _logger.LogDebug("{Name}: no scraperid, searching for globalid: {Id}", Name, globalId);
                var results = new List<RemoteSearchResult>();
                results = await GetSearchResults(results, globalId, cancellationToken);
                if (results.Count > 1 && !FirstOnly)
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
            globalId = GetGlobalId(doc);
            var urlJa = DomainUrlJa + string.Format(PageUrl, scraperId);
            var docJa = new HtmlDocument();
            docJa.LoadHtml(await GetHtml(urlJa, cancellationToken));

            var title = doc.DocumentNode.SelectNodes("//div[@id='video_title']/*/a")?.FindFirst("a")?.InnerText;
            title = title?.Replace(globalId, "").Trim();

            var titleJa = docJa.DocumentNode?.SelectNodes("//div[@id='video_title']/*/a")?.FindFirst("a")?.InnerText;
            titleJa = titleJa?.Replace(globalId, "").Trim();

            var releaseDate = DateTime.Parse(
                doc.DocumentNode.SelectSingleNode("//div[@id='video_date']/table/tr/td[@class='text']")
                    .InnerText);
            var cast = doc.DocumentNode.SelectNodes("//span[@class='cast']/span[@class='star']")?.Where(node =>
                !string.IsNullOrWhiteSpace(node.InnerText)).ToList().ConvertAll(input => input.InnerText.Trim());
            var castJa = docJa.DocumentNode?.SelectNodes("//span[@class='cast']/span[@class='star']")
                ?.Where(node => !string.IsNullOrWhiteSpace(node.InnerText)).ToList()
                .ConvertAll(input => input.InnerText.Trim());
            var genres = doc.DocumentNode.SelectNodes("//span[@class='genre']")?.ToList()
                .ConvertAll(input => input.InnerText.Trim()).ToArray();
            var label = docJa.DocumentNode?.SelectSingleNode("//span[@class='label']/a")?.InnerText?.Trim();
            var maker = docJa.DocumentNode?.SelectSingleNode("//span[@class='maker']/a")?.InnerText?.Trim();
            var director = docJa.DocumentNode?.SelectSingleNode("//span[@class='director']/a")?.InnerText?.Trim();
            var scoreText = doc.DocumentNode.SelectSingleNode("//span[@class='score']")?.InnerText?.Replace("(", "")
                .Replace(")", "");
            var score = string.IsNullOrWhiteSpace(scoreText)
                ? -1
                : float.Parse(scoreText, CultureInfo.InvariantCulture.NumberFormat);

            if (!string.IsNullOrEmpty(titleJa) && (string.IsNullOrWhiteSpace(metadata.Item.Name)))
                metadata.Item.Name = titleJa;
            if (!string.IsNullOrEmpty(title) && (string.IsNullOrWhiteSpace(metadata.Item.OriginalTitle)))
                metadata.Item.OriginalTitle = title;
            if (metadata.Item.PremiereDate == null) metadata.Item.PremiereDate = releaseDate;
            if (metadata.Item.ProductionYear == null) metadata.Item.ProductionYear = releaseDate.Year;
            if (score > -1 && (metadata.Item.CommunityRating == null))
                metadata.Item.CommunityRating = score;
            if (string.IsNullOrWhiteSpace(metadata.Item.OfficialRating))
                metadata.Item.OfficialRating = "R";
            if (string.IsNullOrWhiteSpace(metadata.Item.ExternalId)) metadata.Item.ExternalId = scraperId;
            if (!string.IsNullOrEmpty(label) && (!metadata.Item.Studios.Contains(label)))
                metadata.Item.AddStudio(label);
            if (!string.IsNullOrEmpty(maker) && (!metadata.Item.Studios.Contains(maker)))
                metadata.Item.AddStudio(maker);
            if (!string.IsNullOrEmpty(label) && (string.IsNullOrEmpty(metadata.Item.CollectionName)))
                metadata.Item.CollectionName = label;

            if (genres != null && (metadata.Item.Genres.Length == 0))
                foreach (var genre in genres)
                    metadata.Item.AddGenre(genre);

            if (metadata.People != null) return true;

            if (!string.IsNullOrWhiteSpace(director) &&
                (metadata.People == null || !metadata.People.Exists(p => p.Name == director)))
            {
                metadata.AddPerson(new PersonInfo
                {
                    Name = director,
                    Type = PersonType.Director
                });
            }

            if (castJa == null) return true;

            foreach (var person in castJa)
            {
                metadata.AddPerson(new PersonInfo
                {
                    Name = person,
                    Role = cast != null && castJa.IndexOf(person) > -1 ? cast[castJa.IndexOf(person)] : "",
                    Type = PersonType.Actor
                });
            }

            metadata.Item.SetProviderId(Name, scraperId);
            metadata.Item.SetProviderId(IvInfoConstants.Name, globalId);

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

            var doc = await GetSingleResult(scraperId, cancellationToken);
            var url = doc.DocumentNode?.SelectSingleNode("//img[@id='video_jacket_img']")
                ?.GetAttributeValue("src", null);
            if (string.IsNullOrEmpty(url)) return result;
            if (!url.StartsWith("http")) url = "https:" + url;

            result = IScraper.AddOrOverwrite(result, imageType, url);

            _logger.LogDebug("{Name}: image searching finished", Name);
            return result;
        }

        public IEnumerable<ImageType> HandledImageTypes()
        {
            yield return ImageType.Box;
        }

        private async Task<List<RemoteSearchResult>> GetSearchResults(IEnumerable<RemoteSearchResult> resultList,
            string globalId,
            CancellationToken cancellationToken, bool firstOnly = false)
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
                    var foundGlobalId = node.FirstChild.FirstChild.InnerText;
                    var title = node.LastChild.InnerText.Replace(globalId, "").Trim();
                    var imgUrl = node.ChildNodes.FindFirst("img").GetAttributeValue("src", "");
                    var result = localResultList.Find(r => r.ProviderIds[IvInfoConstants.Name].Equals(foundGlobalId));
                    if (result == null)
                    {
                        result = new RemoteSearchResult
                        {
                            Name = title,
                            ImageUrl = imgUrl,
                            SearchProviderName = Name,
                            Overview = $"{foundGlobalId}<br />{title}",
                            AlbumArtist = new RemoteSearchResult { Name = $"{Name}: {scraperId}/{globalId}" }
                        };
                        result.SetProviderId(IvInfoConstants.Name, foundGlobalId);
                        localResultList.Add(result);
                    }

                    result.SetProviderId(Name, scraperId);

                    if (firstOnly) break;
                }
            }
            else
            {
                var scraperId = GetScraperId(doc);
                var title = doc.DocumentNode.SelectSingleNode("//div[@id='video_title']/*/a").InnerText
                    .Replace(globalId, "").Trim();
                var imgUrl = doc.DocumentNode.SelectSingleNode("//img[@id='video_jacket_img']")
                    .GetAttributeValue("src", null).Replace("pl.jpg", "ps.jpg");
                var result = localResultList.Find(r => r.ProviderIds[IvInfoConstants.Name].Equals(globalId));
                if (result == null)
                {
                    result = new RemoteSearchResult
                    {
                        Name = title,
                        ImageUrl = imgUrl,
                        SearchProviderName = Name,
                        Overview = $"{globalId}<br />{title}",
                        AlbumArtist = new RemoteSearchResult { Name = $"{Name}: {scraperId}/{globalId}" }
                    };
                    result.SetProviderId(IvInfoConstants.Name, globalId);
                    localResultList.Add(result);
                }

                result.SetProviderId(Name, scraperId);
            }

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
            var url = string.Format(BaseUrlEn, globalId);
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
            _logger.LogDebug("{Name}: getting page for scraperid: {Id}", Name, scraperId);
            var doc = new HtmlDocument();
            if (string.IsNullOrEmpty(scraperId)) return doc;
            var url = DomainUrlEn + string.Format(PageUrl, scraperId);
            var html = await GetHtml(url, cancellationToken);
            if (string.IsNullOrEmpty(html)) return doc;
            doc.LoadHtml(html);
            return doc;
        }

        private static string GetFirstResultId(HtmlDocument doc)
        {
            return string.IsNullOrEmpty(doc.Text)
                ? string.Empty
                : doc.DocumentNode.SelectSingleNode(Selector).GetAttributeValue("href", "").Trim('/');
        }

        private string GetScraperId(HtmlDocument doc)
        {
            _logger.LogDebug("{Name}: parsing scraperid", Name);
            return doc.DocumentNode.SelectSingleNode("//div[@id='video_title']/*/a")
                .GetAttributeValue("href", "").Split("=")[1];
        }

        private string GetGlobalId(HtmlDocument doc)
        {
            _logger.LogDebug("{Name}: parsing globalid", Name);
            return doc.DocumentNode.SelectSingleNode("//div[@id='video_id']/table/tr/td[@class='text']").InnerText;
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
}