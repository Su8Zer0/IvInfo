using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
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

        private readonly ILogger _logger;

        public JavlibraryScraper(ILogger logger)
        {
            _logger = logger;
        }

        public int Priority => 3;

        public bool Enabled => IvInfo.Instance?.Configuration.JavlibraryScraperEnabled ?? false;

        public IEnumerable<RemoteSearchResult> GetSearchResults(MovieInfo info)
        {
            var globalId = info.GetProviderId(IvInfoConstants.Name) ?? IvInfoProvider.GetId(info);
            _logger.LogDebug("{Name}: searching for id: {Id}", Name, globalId);
            if (string.IsNullOrEmpty(globalId)) yield break;

            var (multiple, doc) = GetSearchResultsOrResultPage(globalId);
            if (string.IsNullOrEmpty(doc.Text)) yield break;
            if (multiple)
            {
                var nodeCollection = doc.DocumentNode.SelectNodes("//div[@class='videos']/div[@class='video']/a");
                foreach (var node in nodeCollection)
                {
                    var scraperId = node.GetAttributeValue("href", "").Split("=")[1];
                    var foundGlobalId = node.FirstChild.FirstChild.InnerText;
                    var title = node.LastChild.InnerText.Replace(globalId, "").Trim();
                    var imgUrl = node.ChildNodes.FindFirst("img").GetAttributeValue("src", "");
                    var dmmId = imgUrl.Split("/").Last().Replace("ps.jpg", "");
                    var result = new RemoteSearchResult
                    {
                        Name = title,
                        ImageUrl = imgUrl,
                        SearchProviderName = Name,
                        Overview = $"{foundGlobalId}<br />{title}",
                        AlbumArtist = new RemoteSearchResult
                        {
                            Name = foundGlobalId
                        }
                    };
                    result.SetProviderId(Name, scraperId);
                    result.SetProviderId(nameof(DmmScraper), dmmId);
                    yield return result;
                }
            }
            else
            {
                var scraperId = GetScraperId(doc);
                var title = doc.DocumentNode.SelectSingleNode("//div[@id='video_title']/*/a").InnerText
                    .Replace(globalId, "").Trim();
                var imgUrl = doc.DocumentNode.SelectSingleNode("//img[@id='video_jacket_img']")
                    .GetAttributeValue("src", null).Replace("pl.jpg", "ps.jpg");
                var dmmId = imgUrl.Split("/").Last().Replace("ps.jpg", "");
                var result = new RemoteSearchResult
                {
                    Name = title,
                    ImageUrl = imgUrl,
                    SearchProviderName = Name
                };
                result.SetProviderId(Name, scraperId);
                result.SetProviderId(nameof(DmmScraper), dmmId);
                yield return result;
            }
        }

        public bool FillMetadata(MetadataResult<Movie> metadata, bool overwrite = false)
        {
            var scraperId = metadata.Item.GetProviderId(Name);
            var globalId = IvInfoProvider.GetId(metadata.Item.GetLookupInfo());

            HtmlDocument doc;
            if (scraperId == null)
            {
                bool multi;
                (multi, doc) = GetSearchResultsOrResultPage(globalId);
                if (multi) return false;
            }
            else
            {
                doc = GetSingleResult(scraperId);
            }

            if (string.IsNullOrEmpty(doc.Text)) return false;

            scraperId = GetScraperId(doc);
            globalId = GetGlobalId(doc);
            var urlJa = DomainUrlJa + string.Format(PageUrl, scraperId);
            var docJa = new HtmlDocument();
            docJa.LoadHtml(GetHtml(urlJa));

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
            var label = doc.DocumentNode.SelectSingleNode("//span[@class='label']/a")?.InnerText?.Trim();
            var maker = doc.DocumentNode.SelectSingleNode("//span[@class='maker']/a")?.InnerText?.Trim();
            var director = doc.DocumentNode.SelectSingleNode("//span[@class='director']/a")?.InnerText?.Trim();
            var scoreText = doc.DocumentNode.SelectSingleNode("//span[@class='score']")?.InnerText?.Replace("(", "")
                .Replace(")", "");
            var score = string.IsNullOrWhiteSpace(scoreText)
                ? -1
                : float.Parse(scoreText, new CultureInfo("en-US").NumberFormat);

            if (overwrite || string.IsNullOrWhiteSpace(metadata.Item.Name)) metadata.Item.Name = title;
            if (overwrite || string.IsNullOrWhiteSpace(metadata.Item.OriginalTitle))
                metadata.Item.OriginalTitle = titleJa;
            if (overwrite || metadata.Item.PremiereDate == null) metadata.Item.PremiereDate = releaseDate;
            if (overwrite || metadata.Item.ProductionYear == null) metadata.Item.ProductionYear = releaseDate.Year;
            if (overwrite || metadata.Item.CommunityRating == null)
                metadata.Item.CommunityRating = score > -1 ? score : null;
            if (overwrite || string.IsNullOrWhiteSpace(metadata.Item.OfficialRating))
                metadata.Item.OfficialRating = "R";
            if (overwrite || string.IsNullOrWhiteSpace(metadata.Item.ExternalId)) metadata.Item.ExternalId = globalId;
            if (overwrite || metadata.Item.Studios.Length == 0) metadata.Item.AddStudio(label ?? maker);

            if (genres != null && (overwrite || metadata.Item.Genres.Length == 0))
                foreach (var genre in genres)
                    metadata.Item.AddGenre(genre);

            if (!overwrite && metadata.People != null) return true;

            if (!string.IsNullOrWhiteSpace(director))
                metadata.AddPerson(new PersonInfo
                {
                    Name = director,
                    Type = PersonType.Director
                });

            if (cast == null) return true;
            {
                foreach (var person in cast)
                    metadata.AddPerson(new PersonInfo
                    {
                        Name = castJa != null && cast.IndexOf(person) > -1 ? castJa[cast.IndexOf(person)] : "",
                        Role = person,
                        Type = PersonType.Actor
                    });
            }

            var dmmId = doc.DocumentNode.SelectSingleNode("//img[@id='video_jacket_img']")
                .GetAttributeValue("src", null).Split("/").Last().Replace("pl.jpg", "");

            metadata.Item.SetProviderId(Name, scraperId);
            metadata.Item.SetProviderId(nameof(DmmScraper), dmmId);
            metadata.Item.SetProviderId(IvInfoConstants.Name, globalId);

            return true;
        }

        public IEnumerable<RemoteImageInfo> GetImages(BaseItem item, ImageType imageType = ImageType.Primary,
            bool overwrite = false)
        {
            var result = new List<RemoteImageInfo>();

            if (!HandledImageTypes().Contains(imageType)) return result;

            if (item.ImageInfos.Any(i => i.Type == imageType) && !overwrite) return result;

            var scraperId = item.GetProviderId(Name);
            if (string.IsNullOrEmpty(scraperId)) return result;

            var doc = GetSingleResult(scraperId);
            var url = doc.DocumentNode?.SelectSingleNode("//img[@id='video_jacket_img']")
                ?.GetAttributeValue("src", null);
            if (string.IsNullOrEmpty(url)) return result;
            if (!url.StartsWith("http")) url = "https:" + url;

            result = IScraper.AddOrOverwrite(result, imageType, url, overwrite);

            return result;
        }

        public IEnumerable<ImageType> HandledImageTypes()
        {
            yield return ImageType.Primary;
        }

        /// <summary>
        ///     Returns true and page with search results if there were multiple results for this global id.
        ///     When there were only one result returns false and result page. If nothing was found returns false and empty page.
        /// </summary>
        /// <param name="globalId">global id</param>
        /// <returns>bool, HtmlDocument pair</returns>
        private (bool, HtmlDocument) GetSearchResultsOrResultPage(string globalId)
        {
            _logger.LogDebug("{Name}: searching for id: {Id}", Name, globalId);
            if (string.IsNullOrEmpty(globalId)) return (false, new HtmlDocument());
            var url = string.Format(BaseUrlEn, globalId);
            var html = GetHtml(url);
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
        /// <returns>page for id</returns>
        private HtmlDocument GetSingleResult(string scraperId)
        {
            _logger.LogDebug("{Name}: getting page for id: {Id}", Name, scraperId);
            var doc = new HtmlDocument();
            if (string.IsNullOrEmpty(scraperId)) return doc;
            var url = DomainUrlEn + string.Format(PageUrl, scraperId);
            var html = GetHtml(url);
            if (string.IsNullOrEmpty(html)) return doc;
            doc.LoadHtml(html);
            return doc;
        }

        private static string GetScraperId(HtmlDocument doc)
        {
            return doc.DocumentNode.SelectSingleNode("//div[@id='video_title']/*/a")
                .GetAttributeValue("href", "").Split("=")[1];
        }

        private static string GetGlobalId(HtmlDocument doc)
        {
            return doc.DocumentNode.SelectSingleNode("//div[@id='video_id']/table/tr/td[@class='text']").InnerText;
        }

        private string GetHtml(string url)
        {
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Referer = "https://www.javlibrary.com/";
            request.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:99.0) Gecko/20100101 Firefox/99.0";
            request.CookieContainer = new CookieContainer();
            request.CookieContainer.Add(
                new Cookie("__cf_bm",
                    "Hu9B4Rh_fEnJtQ870COFtsI5D2o_d5NKSR0CTRCjBMU-1650621637-0-ARs0jjHbPVj2Fs0Q+pwe6xYylUgq2ITKMU7W1aRvvpib6WMMM/trVUroA1SrLLQiEOCuhG5bHQ4Ybz+qeZFyJxVQxAhAeoAy2QZQUL59JOAkfmgewtrR2Kf1x/wzXd0zjQ==",
                    "/", ".javlibrary.com"));
            request.CookieContainer.Add(new Cookie("cf_clearance",
                "QuiobQ7bNToHC8orI_SCLBjzHgtviH2nPxMLIuzbC5M-1650621636-0-150", "/", ".javlibrary.com"));
            try
            {
                var response = (HttpWebResponse)request.GetResponse();
                var stream = response.GetResponseStream();

                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }
            catch (WebException e)
            {
                _logger.LogError("Could not load page {Url}\n{Message}", url, e.Message);
                return string.Empty;
            }
        }
    }
}