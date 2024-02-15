using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
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

namespace Jellyfin.Plugin.IvInfo.Providers.Scrapers
{
    // ReSharper disable once UnusedType.Global
    public class DmmScraper : IScraper
    {
        private const string Name = nameof(DmmScraper);

        private const string DomainUrl = "https://www.dmm.co.jp/";
        private const string SearchUrl = DomainUrl + "search/=/searchstr={0}";
        private const string ProxyDomain = "https://jppx.azurewebsites.net/";
        private const string ProxyUrl = ProxyDomain + "browse.php?u={0}&b=8";
        private const string NoPage = "404 Not Found";
        private const string NoResults = "に一致する商品は見つかりませんでした";
        private const string MetadataSelector = "//table[@class='mg-b20']/tr/td[@class='nw']";
        private const string NoInfo = "----";
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
        /// Should we use proxy for loading DMM pages? Most of DMM product pages is not available and not possible to find outside of Japan.
        /// </summary>
        private static bool UseProxy => IvInfo.Instance?.Configuration.DmmUseProxy ?? false;

        private static bool FirstOnly => IvInfo.Instance?.Configuration.FirstOnly ?? false;

        public int Priority => 1;

        public bool Enabled => IvInfo.Instance?.Configuration.DmmScraperEnabled ?? false;
        public bool ImgEnabled => IvInfo.Instance?.Configuration.DmmImgEnabled ?? false;

        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(IEnumerable<RemoteSearchResult> resultList,
            MovieInfo info, CancellationToken cancellationToken)
        {
            var globalId = info.GetProviderId(IvInfoConstants.Name) ?? IvInfoProvider.GetId(info);
            _logger.LogDebug("{Name}: searching for id: {Id}", Name, globalId);
            if (string.IsNullOrEmpty(globalId)) return resultList;

            return await GetSearchResults(resultList, globalId, cancellationToken, FirstOnly);
        }

        public async Task<bool> FillMetadata(MetadataResult<Movie> metadata, CancellationToken cancellationToken,
            bool overwrite = false)
        {
            var scraperId = metadata.Item.GetProviderId(Name);
            var globalId = metadata.Item.GetProviderId(IvInfoConstants.Name) ??
                           IvInfoProvider.GetId(metadata.Item.GetLookupInfo());
            _logger.LogDebug("{Name}: searching for ids: {GlobalId}, {ScraperId}", Name, globalId, scraperId);

            if (scraperId == null)
            {
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
            {
                description = doc.DocumentNode
                    .SelectSingleNode("//div[@class='mg-b20 lh4']/p[@class='mg-b20']")
                    ?.InnerText.Trim();
            }

            if (!string.IsNullOrEmpty(title) && (overwrite || string.IsNullOrWhiteSpace(metadata.Item.Name)))
                metadata.Item.Name = title;
            if (!string.IsNullOrEmpty(description) && (overwrite || string.IsNullOrWhiteSpace(metadata.Item.Overview)))
                metadata.Item.Overview = description;
            if (datePresent && (overwrite || metadata.Item.PremiereDate == null))
                metadata.Item.PremiereDate = releaseDate;
            if (datePresent && (overwrite || metadata.Item.ProductionYear == null))
                metadata.Item.ProductionYear = releaseDate.Year;
            if (overwrite || string.IsNullOrWhiteSpace(metadata.Item.OfficialRating))
                metadata.Item.OfficialRating = "R";
            if (overwrite || string.IsNullOrWhiteSpace(metadata.Item.ExternalId)) metadata.Item.ExternalId = scraperId;
            if (!string.IsNullOrEmpty(label) && (overwrite || !metadata.Item.Studios.Contains(label)))
                metadata.Item.AddStudio(label);
            if (!string.IsNullOrEmpty(maker) && (overwrite || !metadata.Item.Studios.Contains(maker)))
                metadata.Item.AddStudio(maker);

            if (!string.IsNullOrEmpty(series) && (overwrite || string.IsNullOrEmpty(metadata.Item.CollectionName)))
                metadata.Item.CollectionName = series;

            if (!string.IsNullOrWhiteSpace(director) && (overwrite || !metadata.People.Exists(p => p.Name == director)))
            {
                metadata.AddPerson(new PersonInfo
                {
                    Name = director,
                    Type = PersonType.Director
                });
            }

            metadata.Item.SetProviderId(Name, scraperId);

            _logger.LogDebug("{Name}: metadata fetching finished", Name);
            return true;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken,
            ImageType imageType = ImageType.Primary, bool overwrite = false)
        {
            _logger.LogDebug("{Name}: searching for image {ImageType}", Name, imageType);
            var result = new List<RemoteImageInfo>();

            if (!HandledImageTypes().Contains(imageType))
            {
                _logger.LogDebug("{Name}: {ImageType} image type not handled", Name, imageType);
                return result;
            }

            if (item.ImageInfos.Any(i => i.Type == imageType) && !overwrite)
            {
                _logger.LogDebug("{Name}: {ImageType} image already exists, not overwriting", Name, imageType);
                return result;
            }

            var scraperId = item.GetProviderId(Name);
            _logger.LogDebug("{Name}: scraper id: {Id}", Name, scraperId);
            if (string.IsNullOrEmpty(scraperId))
            {
                var globalId = item.GetProviderId(IvInfoConstants.Name);
                if (string.IsNullOrEmpty(globalId)) return result;
                var list = await GetSearchResults(Array.Empty<RemoteSearchResult>(), globalId, cancellationToken, true);
                var first = list.FirstOrDefault();
                scraperId = first?.GetProviderId(Name);
                if (string.IsNullOrEmpty(scraperId)) return result;
            }

            _logger.LogDebug("{Name}: pageUrl: {Url}", Name, scraperId);

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
                    result = IScraper.AddOrOverwrite(result, imageType, GetProxyUrl(frontUrl), overwrite);
                    break;
                case ImageType.Box:
                    var boxUrl = doc.DocumentNode.SelectSingleNode("//a[@name='package-image']")
                        ?.GetAttributeValue("href", "");
                    if (boxUrl == null) break;
                    result = IScraper.AddOrOverwrite(result, imageType, GetProxyUrl(boxUrl), overwrite);
                    break;
                case ImageType.Screenshot:
                    var screenshotNodes = doc.DocumentNode.SelectNodes("//a[@name='sample-image']/img");
                    if (screenshotNodes == null) break;
                    result.AddRange(screenshotNodes.Select(node => node.GetAttributeValue("src", "")).Select(scrUrl =>
                        new RemoteImageInfo
                            { Url = GetProxyUrl(scrUrl), Type = imageType, ProviderName = IvInfoConstants.Name }));
                    break;
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
            string globalId, CancellationToken cancellationToken, bool firstOnly = false)
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
                if (!scraperId.ToLower().Contains(globalId.ToLower().Replace("-", "")))
                    continue;
                var title = node.InnerText.Trim();
                var imgUrl = ProxyDomain + node.ChildNodes.FindFirst("img").GetAttributeValue("src", "");
                var result = localResultList.Find(r => r.ProviderIds[IvInfoConstants.Name].Equals(globalId));
                if (result == null)
                {
                    result = new RemoteSearchResult
                    {
                        Name = title,
                        ImageUrl = imgUrl,
                        SearchProviderName = Name,
                        AlbumArtist = new RemoteSearchResult { Name = $"{Name}: {scraperId}/{globalId}" }
                    };
                    result.SetProviderId(IvInfoConstants.Name, globalId);
                    localResultList.Add(result);
                }

                result.SetProviderId(Name, scraperId);

                if (firstOnly) break;
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
            if (UseProxy)
            {
                cookies.Add(new Uri(ProxyDomain), new Cookie("c[dmm.co.jp][/][age_check_done]", "1"));
            }

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
        /// Returns proxified url if needed, else returns url.
        /// </summary>
        /// <param name="url">Url to proxify</param>
        /// <returns>Processed url</returns>
        private string GetProxyUrl(string url)
        {
            _logger.LogDebug("GetHtml: {Url} (proxified: {Proxified})", url, UseProxy);
            var proxyUrl = url.Contains("browse.php?u=")
                ? ProxyDomain + url
                : string.Format(ProxyUrl, HttpUtility.UrlEncode(url));
            return UseProxy ? proxyUrl : url;
        }
    }
}