using System;
using System.Collections.Generic;
using System.Linq;
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

        private readonly ILogger _logger;

        public GekiyasuScraper(ILogger logger)
        {
            _logger = logger;
        }

        public int Priority => 4;

        public bool Enabled => false;

        public IEnumerable<RemoteSearchResult> GetSearchResults(MovieInfo info)
        {
            var globalId = info.GetProviderId(IvInfoConstants.Name);
            _logger.LogDebug("{Name}: searching for id: {Id}", Name, globalId);
            yield break;
        }

        public bool FillMetadata(MetadataResult<Movie> metadata, bool overwrite = false)
        {
            var scraperId = metadata.Item.GetProviderId(Name);
            var globalId = metadata.Item.GetProviderId(IvInfoConstants.Name);

            var web = new HtmlWeb();
            var doc = scraperId != null ? GetSingleResult(scraperId) : GetSearchResultsPage(globalId!); 
            
            if (string.IsNullOrEmpty(doc.Text)) return false;

            var resultUrl = BaseUrl + doc.DocumentNode.SelectSingleNode("//a[@class='thumbnail']")
                .GetAttributeValue("href", "");
            doc = web.Load(resultUrl);
            if (doc == null) return false;

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

            return true;
        }

        public IEnumerable<RemoteImageInfo> GetImages(BaseItem item, ImageType imageType = ImageType.Primary,
            bool overwrite = false)
        {
            var result = new List<RemoteImageInfo>();

            if (!HandledImageTypes().Contains(imageType)) return result;

            var scraperId = item.GetProviderId(Name);
            if (string.IsNullOrEmpty(scraperId)) return result;

            var web = new HtmlWeb();
            var doc = web.Load(string.Format(SearchUrl, scraperId));

            if (doc.DocumentNode.InnerText.Contains(NoResults)) return result;

            var resultUrl = BaseUrl + doc.DocumentNode.SelectSingleNode("//a[@class='thumbnail']")
                .GetAttributeValue("href", "");
            doc = web.Load(resultUrl);
            if (doc == null) return result;

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

            return result;
        }

        public IEnumerable<ImageType> HandledImageTypes()
        {
            yield return ImageType.Primary;
            yield return ImageType.Box;
        }

        /// <summary>
        /// Returns page with search results. If nothing was found returns empty page.
        /// </summary>
        /// <param name="globalId">global id</param>
        /// <returns>HtmlDocument</returns>
        private HtmlDocument GetSearchResultsPage(string globalId)
        {
            _logger.LogDebug("{Name}: searching for id: {Id}", Name, globalId);
            var doc = new HtmlDocument();
            if (string.IsNullOrEmpty(globalId)) return doc;
            var url = string.Format(SearchUrl, globalId);
            var web = new HtmlWeb();
            doc = web.Load(url);
            return doc.DocumentNode.InnerText.Contains(NoResults) ? new HtmlDocument() : doc;
        }

        /// <summary>
        /// Returns result page for specific scraper id.
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
    }
}