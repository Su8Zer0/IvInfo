using System;
using System.Collections.Generic;
using System.Linq;
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
    public class GekiyasuScraper : IScraper
    {
        private const string Name = nameof(GekiyasuScraper);

        private const string BaseUrl = "https://www.gekiyasu-dvdshop.jp/";
        private const string PageUrl = BaseUrl + "products/{0}";
        private const string SearchUrl = BaseUrl + "products/list.php?mode=search&name={0}";
        private const string NoResults = "該当件数0件です";
        private const string MakerLbl = "メーカー";
        private const string Selector = "//div[@class='b-list-area']/*/*/*/a[@class='thumbnail']";

        private readonly ILogger _logger;

        public GekiyasuScraper(ILogger logger)
        {
            _logger = logger;
        }

        public int Priority => 4;

        public bool Enabled => IvInfo.Instance?.Configuration.GekiyasuScraperEnabled ?? false;

        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(MovieInfo info,
            CancellationToken cancellationToken)
        {
            var globalId = info.GetProviderId(IvInfoConstants.Name) ?? IvInfoProvider.GetId(info);
            _logger.LogDebug("{Name}: searching for id: {Id}", Name, globalId);
            var resultList = new List<RemoteSearchResult>();
            if (string.IsNullOrEmpty(globalId)) return resultList;

            var (multiple, doc) = GetSearchResultsPage(globalId);
            if (string.IsNullOrEmpty(doc.Text)) return resultList;
            if (multiple)
            {
                var nodeCollection =
                    doc.DocumentNode.SelectNodes("//div[@class='b-list-area']/ul[@class='b-list clearfix']/li") ??
                    new HtmlNodeCollection(null);
                foreach (var node in nodeCollection)
                {
                    var scraperId = node.ChildNodes.FindFirst("a").GetAttributeValue("href", "").Split("/").Last();
                    var title = node.ChildNodes.FindFirst("p").InnerText;
                    var imgUrl = BaseUrl + node.ChildNodes.FindFirst("a").FirstChild.GetAttributeValue("src", "");
                    var result = new RemoteSearchResult
                    {
                        Name = title,
                        ImageUrl = imgUrl,
                        SearchProviderName = Name
                    };
                    result.SetProviderId(Name, scraperId);
                    resultList.Add(result);
                }
            }
            else
            {
                doc = GetFirstResult(doc);
                var scraperId = GetScraperId(doc);
                var title = doc.DocumentNode.SelectSingleNode("//div[@class='b-title']/strong")?.InnerText;
                var imgUrl = BaseUrl + doc.DocumentNode.SelectSingleNode("//a[@class='thumbnail']")
                    .GetAttributeValue("href", "");
                var result = new RemoteSearchResult
                {
                    Name = title,
                    ImageUrl = imgUrl,
                    SearchProviderName = Name
                };
                result.SetProviderId(Name, scraperId);
                resultList.Add(result);
            }

            return await Task.Run(() => resultList, cancellationToken);
        }

        public async Task<bool> FillMetadata(MetadataResult<Movie> metadata, CancellationToken cancellationToken,
            bool overwrite = false)
        {
            var scraperId = metadata.Item.GetProviderId(Name);
            var globalId = metadata.Item.GetProviderId(IvInfoConstants.Name);

            HtmlDocument doc;
            if (scraperId == null)
            {
                (_, doc) = GetSearchResultsPage(globalId!);
                doc = GetFirstResult(doc);
            }
            else
            {
                doc = GetSingleResult(scraperId);
            }

            if (string.IsNullOrEmpty(doc.Text)) return await Task.Run(() => false, cancellationToken);

            scraperId = GetScraperId(doc);

            var title = doc.DocumentNode.SelectSingleNode("//div[@class='b-title']/strong")?.InnerText;
            var datePresent = DateTime.TryParse(
                doc.DocumentNode.SelectSingleNode("//div[@class='b-releasedate']/span")?.InnerText,
                out var releaseDate);
            var description = doc.DocumentNode.SelectSingleNode("//div[@id='detailarea']/div[@class='well']/span")
                ?.InnerText;
            var maker = doc.DocumentNode.SelectNodes("//ul[@class='b-relative']/li/a")
                ?.Where(node => node.InnerText == MakerLbl).First()?.ParentNode?.LastChild?.InnerText;

            if (overwrite || string.IsNullOrWhiteSpace(metadata.Item.Name)) metadata.Item.Name = title;
            if (overwrite || string.IsNullOrWhiteSpace(metadata.Item.OriginalTitle))
                metadata.Item.OriginalTitle = title;
            if (overwrite || string.IsNullOrWhiteSpace(metadata.Item.Overview)) metadata.Item.Overview = description;
            if (overwrite || metadata.Item.PremiereDate == null)
                metadata.Item.PremiereDate = datePresent ? releaseDate : null;
            if (overwrite || metadata.Item.ProductionYear == null)
                metadata.Item.ProductionYear = datePresent ? releaseDate.Year : null;
            if (overwrite || string.IsNullOrWhiteSpace(metadata.Item.OfficialRating))
                metadata.Item.OfficialRating = "R";
            if (overwrite || string.IsNullOrWhiteSpace(metadata.Item.ExternalId)) metadata.Item.ExternalId = scraperId;
            if (overwrite || metadata.Item.Studios.Length == 0) metadata.Item.AddStudio(maker);

            metadata.Item.SetProviderId(Name, scraperId);

            return await Task.Run(() => true, cancellationToken);
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken,
            ImageType imageType = ImageType.Primary,
            bool overwrite = false)
        {
            var result = new List<RemoteImageInfo>();

            if (!HandledImageTypes().Contains(imageType)) return result;

            if (item.ImageInfos.Any(i => i.Type == imageType) && !overwrite) return result;

            var scraperId = item.GetProviderId(Name);
            if (string.IsNullOrEmpty(scraperId)) return result;

            var web = new HtmlWeb();
            var doc = web.Load(string.Format(PageUrl, scraperId));

            if (doc.DocumentNode.InnerText.Contains(NoResults)) return result;

            var thumbUrl = BaseUrl + doc.DocumentNode.SelectSingleNode("//a[@class='thumbnail']")
                .GetAttributeValue("href", "");
            var boxUrl = BaseUrl + doc.DocumentNode.SelectSingleNode("//img[@class='img-responsive']")
                .GetAttributeValue("src", "");

            result = imageType switch
            {
                ImageType.Primary => IScraper.AddOrOverwrite(result, imageType, thumbUrl, overwrite),
                ImageType.Box => IScraper.AddOrOverwrite(result, imageType, boxUrl, overwrite),
                _ => result
            };

            return await Task.Run(() => result, cancellationToken);
        }

        public IEnumerable<ImageType> HandledImageTypes()
        {
            yield return ImageType.Primary;
            yield return ImageType.Box;
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
        /// <returns>bool, HtmlDocument pair</returns>
        private (bool, HtmlDocument) GetSearchResultsPage(string globalId)
        {
            _logger.LogDebug("{Name}: searching for id: {Id}", Name, globalId);
            var doc = new HtmlDocument();
            if (string.IsNullOrEmpty(globalId)) return (false, doc);
            var url = string.Format(SearchUrl, globalId);
            var web = new HtmlWeb();
            doc = web.Load(url);
            if (string.IsNullOrEmpty(doc.Text)) return (false, doc);

            return doc.DocumentNode.InnerText.Contains(NoResults)
                ? (false, new HtmlDocument())
                : (HasMultipleResults(doc), doc);
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
            var url = string.Format(PageUrl, scraperId);
            var web = new HtmlWeb();
            doc = web.Load(url);
            return doc;
        }

        private static HtmlDocument GetFirstResult(HtmlDocument doc)
        {
            var result = new HtmlDocument();
            if (string.IsNullOrEmpty(doc.Text)) return result;
            if (HasMultipleResults(doc)) return result;

            var resultUrl = BaseUrl + doc.DocumentNode.SelectSingleNode(Selector).GetAttributeValue("href", "");
            var web = new HtmlWeb();
            result = web.Load(resultUrl);
            return result;
        }

        private static string GetScraperId(HtmlDocument doc)
        {
            return doc.DocumentNode.SelectSingleNode("//form[@id='form1']")?.GetAttributeValue("action", "").Split("/")
                .Last() ?? string.Empty;
        }
    }
}