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

namespace Jellyfin.Plugin.IvInfo.Providers.Scrapers
{
    // ReSharper disable once UnusedType.Global
    public class DmmScraper : IScraper
    {
        private const string Name = nameof(DmmScraper);

        private const string DomainUrl = "https://www.dmm.co.jp/";
        private const string PageUrl = DomainUrl + "mono/dvd/-/detail/=/cid={0}";
        private const string NoPage = "404 Not Found";

        private readonly ILogger _logger;

        public DmmScraper(ILogger logger)
        {
            _logger = logger;
        }

        public int Priority => 1;

        public bool Enabled => IvInfo.Instance?.Configuration.DmmScraperEnabled ?? false;

        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(MovieInfo info,
            CancellationToken cancellationToken)
        {
            _logger.LogDebug("{Name}: Searching not supported", Name);
            return await Task.Run(() => new List<RemoteSearchResult>().AsEnumerable(), cancellationToken);
        }

        public async Task<bool> FillMetadata(MetadataResult<Movie> metadata, CancellationToken cancellationToken,
            bool overwrite = false)
        {
            _logger.LogDebug("{Name}: Metadata lookup not supported", Name);
            return await Task.Run(() => false, cancellationToken);
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken,
            ImageType imageType = ImageType.Primary, bool overwrite = false)
        {
            _logger.LogDebug("Getting image type {Type} for item {Name}", imageType, item.Name);
            var result = new List<RemoteImageInfo>();

            if (!HandledImageTypes().Contains(imageType)) return result;

            var scraperId = item.GetProviderId(Name);
            _logger.LogDebug("Scraper id: {Id}", scraperId);
            if (string.IsNullOrEmpty(scraperId)) return result;

            var url = string.Format(PageUrl, scraperId);
            _logger.LogDebug("PageUrl: {Url}", url);

            var doc = new HtmlDocument();
            var html = await GetHtml(url, cancellationToken);
            if (string.IsNullOrEmpty(html)) return result;
            doc.LoadHtml(html);

            if (doc.DocumentNode.InnerText.Contains(NoPage)) return result;

            switch (imageType)
            {
                case ImageType.Primary:
                    var frontUrl = doc.DocumentNode.SelectSingleNode("//a[@name='package-image']/img")
                        .GetAttributeValue("src", "");
                    result.Add(new RemoteImageInfo
                        { Url = frontUrl, Type = imageType, ProviderName = IvInfoConstants.Name });
                    break;
                case ImageType.Box:
                    var boxUrl = doc.DocumentNode.SelectSingleNode("//a[@name='package-image']")
                        .GetAttributeValue("href", "");
                    result.Add(new RemoteImageInfo
                        { Url = boxUrl, Type = imageType, ProviderName = IvInfoConstants.Name });
                    break;
                case ImageType.Screenshot:
                    var screenshotNodes = doc.DocumentNode.SelectNodes("//a[@name='sample-image']/img");
                    if (screenshotNodes == null) break;
                    result.AddRange(screenshotNodes.Select(node => node.GetAttributeValue("src", "")).Select(scrUrl =>
                        new RemoteImageInfo { Url = scrUrl, Type = imageType, ProviderName = IvInfoConstants.Name }));
                    break;
            }

            return await Task.Run(() => result, cancellationToken);
        }

        public IEnumerable<ImageType> HandledImageTypes()
        {
            yield return ImageType.Primary;
            yield return ImageType.Box;
            yield return ImageType.Screenshot;
        }

        private async Task<string> GetHtml(string url, CancellationToken cancellationToken)
        {
            _logger.LogDebug("GetHtml: {Url}", url);
            var cookies = new CookieContainer();
            cookies.Add(new Uri(DomainUrl), new Cookie("age_check_done", "1"));
            cookies.Add(new Uri(DomainUrl), new Cookie("cklg", "ja"));
            var handler = new HttpClientHandler { CookieContainer = cookies };
            var request = new HttpRequestMessage();
            request.RequestUri = new Uri(url);
            request.Method = HttpMethod.Get;
            var client = new HttpClient(handler);

            try
            {
                var response = await client.SendAsync(request, cancellationToken);
                return await response.Content.ReadAsStringAsync(cancellationToken);
            }
            catch (WebException e)
            {
                _logger.LogError("Could not load page {Url}\n{Message}", url, e.Message);
                return string.Empty;
            }
        }
    }
}