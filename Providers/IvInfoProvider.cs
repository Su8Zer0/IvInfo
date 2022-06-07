using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.IvInfo.Providers.Scrapers;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Sorting;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Extensions;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.IvInfo.Providers
{
    /// <summary>
    ///     IV Info Provider.
    /// </summary>
    public class IvInfoProvider : IRemoteMetadataProvider<Movie, MovieInfo>, IRemoteImageProvider
    {
        private const string IdPattern = @".*?(\w{2,5}-\d{3,6}\w?).*";

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<IvInfoProvider> _logger;

        public IvInfoProvider(IHttpClientFactory httpClientFactory, ILogger<IvInfoProvider> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public bool Supports(BaseItem item)
        {
            return item is Movie;
        }

        public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
        {
            yield return ImageType.Primary;
            yield return ImageType.Box;
            yield return ImageType.Screenshot;
        }

        public Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
        {
            _logger.LogDebug("GetImages");
            _logger.LogDebug("Params: Item:{Item}", item);

            var result = new List<RemoteImageInfo>();
            var language = item.GetPreferredMetadataLanguage();
            var id = item.GetProviderId(Name);
            _logger.LogDebug("Id: {Id}", id);

            if (string.IsNullOrWhiteSpace(id))
            {
                _logger.LogError("No id found");
                return Task.Run(() => result.OrderByLanguageDescending(language), cancellationToken);
            }

            var scrapers = GetAllScrapers();

            foreach (var urls in from scraper in scrapers
                     where scraper.HandledImageTypes().Any()
                     from t in GetSupportedImages(item)
                     select scraper.GetImages(item, t))
                result.AddRange(urls);

            return Task.Run(() => result.OrderByLanguageDescending(language), cancellationToken);
        }

        public string Name => IvInfoConstants.Name;

        public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(MovieInfo searchInfo,
            CancellationToken cancellationToken)
        {
            _logger.LogDebug("GetSearchResults");
            _logger.LogDebug("Params: Name:{Name}, Path:{Path}", searchInfo.Name, searchInfo.Path);
            var result = new List<RemoteSearchResult>();
            var id = GetId(searchInfo);
            if (string.IsNullOrEmpty(id)) return Task.Run(() => result.OrderByString(_ => ""), cancellationToken);
            var scrapers = GetAllScrapers();
            foreach (var scraper in scrapers) result.AddRange(scraper.GetSearchResults(searchInfo));

            return Task.Run(() => result.OrderByString(_ => ""), cancellationToken);
        }

        public Task<MetadataResult<Movie>> GetMetadata(MovieInfo info, CancellationToken cancellationToken)
        {
            _logger.LogDebug("GetMetadata");
            _logger.LogDebug(
                "Params: Name:{Name}, Path:{Path}, Year:{Year}, IndexNumber:{IndexNumber}, ProviderIds{Ids}",
                info.Name, info.Path, info.Year, info.IndexNumber, info.ProviderIds);
            var result = new MetadataResult<Movie>
            {
                HasMetadata = false
            };
            var id = GetId(info);
            _logger.LogDebug("Parsed id:{Id}", id);

            if (string.IsNullOrWhiteSpace(id))
            {
                _logger.LogError("Id could not be determined ({Name})", info.Name);
                return Task.Run(() => result, cancellationToken);
            }

            result.Item = new Movie
            {
                Path = info.Path,
                ProviderIds = info.ProviderIds
            };
            result.Item.SetProviderId(Name, id);

            var scrapers = GetAllScrapers();
            foreach (var scraper in scrapers) result.HasMetadata |= scraper.FillMetadata(result);

            return Task.Run(() => result, cancellationToken);
        }

        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            _logger.LogDebug("GetImageResponse");
            _logger.LogDebug("Params: url:{Url}", url);
            return _httpClientFactory.CreateClient(NamedClient.Default).GetAsync(new Uri(url), cancellationToken);
        }

        public static string GetId(ItemLookupInfo info)
        {
            var id = info.GetProviderId(IvInfoConstants.Name) ?? ParseId(info);
            return id;
        }

        private static string ParseId(ItemLookupInfo info)
        {
            if (!string.IsNullOrEmpty(info.Path)) return Regex.Match(info.Path, IdPattern).Groups[1].Value;

            return !string.IsNullOrEmpty(info.Name) ? Regex.Match(info.Name, IdPattern).Groups[1].Value : string.Empty;
        }

        private IEnumerable<IScraper> GetAllScrapers()
        {
            var list = AppDomain.CurrentDomain.GetAssemblies().SelectMany(x => x.GetTypes())
                .Where(x => typeof(IScraper).IsAssignableFrom(x) && !x.IsInterface && !x.IsAbstract)
                .ToList();
            List<IScraper> objects = list.FindAll(t => t.GetConstructor(new[] { typeof(ILogger) }) != null)
                .ConvertAll(t =>
                {
                    var obj = t.GetConstructor(new[] { typeof(ILogger) })!.Invoke(new object?[]
                        { _logger }) as IScraper;
                    return obj;
                })!;
            objects.RemoveAll(s => !s.Enabled);
            objects.Sort((x, y) => x.Priority - y.Priority);
            return objects;
        }
    }
}