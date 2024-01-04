using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using F23.StringSimilarity;
using IvInfo.Providers.Scrapers;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Extensions;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace IvInfo.Providers;

/// <summary>
///     IV Info Provider.
/// </summary>
// ReSharper disable once ClassNeverInstantiated.Global
public class IvInfoProvider : IRemoteMetadataProvider<Movie, MovieInfo>, IRemoteImageProvider
{
    private const string IdPattern = @".*?(\w{2,5}-\w{0,2}\d{3,6}\w?).*";

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

    public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
    {
        _logger.LogDebug("GetImages");
        _logger.LogDebug("Params: Item:{Item}", item);

        var result = new List<RemoteImageInfo>();
        var language = item.GetPreferredMetadataLanguage();
        var id = item.GetProviderId(Name);
        _logger.LogDebug("Global Id: {Id}", id);

        if (string.IsNullOrWhiteSpace(id))
        {
            _logger.LogError("No global id found");
            return result.OrderByLanguageDescending(language);
        }

        var scrapers = GetImageScrapers();

        foreach (var scraper in scrapers)
        foreach (var imageType in scraper.HandledImageTypes())
            try
            {
                result.AddRange(await scraper.GetImages(item, cancellationToken, imageType));
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error getting images from scraper {Scraper}\n{Error}", scraper, e.Message);
            }

        return result.OrderByLanguageDescending(language);
    }

    public string Name => IvInfoConstants.Name;

    public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(MovieInfo searchInfo,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("GetSearchResults");
        _logger.LogDebug("Params: Name:{Name}, Path:{Path}", searchInfo.Name, searchInfo.Path);
        var result = new List<RemoteSearchResult>();
        var id = GetId(searchInfo);
        if (string.IsNullOrEmpty(id)) return result;
        var scrapers = GetEnabledScrapers();
        // ReSharper disable once LoopCanBeConvertedToQuery
        foreach (var scraper in scrapers)
        {
            result = (List<RemoteSearchResult>)await scraper.GetSearchResults(result, searchInfo, cancellationToken);
        }

        _logger.LogDebug("Found {Num} results", result.Count);

        result = MergeResults(result);

        _logger.LogDebug("Results after merging: {Num}", result.Count);

        return result;
    }

    public async Task<MetadataResult<Movie>> GetMetadata(MovieInfo info, CancellationToken cancellationToken)
    {
        _logger.LogDebug("GetMetadata");
        _logger.LogDebug(
            "Params: Name:{Name}, Path:{Path}, Year:{Year}, IndexNumber:{IndexNumber}, ProviderIds{Ids}",
            info.Name, info.Path, info.Year, info.IndexNumber, info.ProviderIds);
        var result = new MetadataResult<Movie>
        {
            HasMetadata = false
        };
        var id = info.GetProviderId(IvInfoConstants.Name) ?? GetId(info);
        _logger.LogDebug("Global id: {Id}", id);
        if (id.Contains('|')) id = id.Split('|')[0];

        if (string.IsNullOrWhiteSpace(id))
        {
            _logger.LogError("Global Id could not be determined (Name: {Name}, Path: {Path})", info.Name,
                info.Path);
            return result;
        }

        result.Item = new Movie
        {
            Path = info.Path,
            ProviderIds = info.ProviderIds
        };
        result.Item.SetProviderId(Name, id);

        var scrapers = GetEnabledScrapers();
        foreach (var scraper in scrapers)
            try
            {
                result.HasMetadata |= await scraper.FillMetadata(result, info, cancellationToken);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "GetMetadata error, scraper {Scraper}: {Message}", scraper, e.Message);
            }

        return result;
    }

    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
    {
        _logger.LogDebug("GetImageResponse");
        _logger.LogDebug("Params: url:{Url}", url);
        return _httpClientFactory.CreateClient(NamedClient.Default).GetAsync(new Uri(url), cancellationToken);
    }

    private static List<RemoteSearchResult> MergeResults(List<RemoteSearchResult> list)
    {
        if (list.Count <= 1) return list;
        var current = 0;
        var next = 1;
        while (true)
        {
            if (current + 1 > list.Count || next + 1 > list.Count) break;
            var first = list[current];
            var second = list[next];
            var globalIdFirst = first.GetProviderId(IvInfoConstants.Name)?.Split('|')[0] ?? string.Empty;
            var globalIdSecond = second.GetProviderId(IvInfoConstants.Name)?.Split('|')[0] ?? string.Empty;
            var l = new NormalizedLevenshtein();
            var sim = l.Similarity(first.Name, second.Name);
            if (sim > 0.3 && globalIdFirst.Equals(globalIdSecond))
            {
                foreach (var (name, value) in second.ProviderIds)
                    if (string.IsNullOrEmpty(first.GetProviderId(name)))
                        first.SetProviderId(name, value);

                if (string.IsNullOrEmpty(first.Overview) && !string.IsNullOrEmpty(second.Overview))
                    first.Overview = second.Overview;
                if (string.IsNullOrEmpty(first.ImageUrl) && !string.IsNullOrEmpty(second.ImageUrl))
                    first.ImageUrl = second.ImageUrl;
                list.Remove(second);
                if (list.Count <= 1) break;
            }
            else
            {
                if (next + 1 == list.Count)
                {
                    if (current + 1 == list.Count)
                    {
                        break;
                    }

                    current++;
                    next = current + 1;
                }
                else
                {
                    next++;
                }
            }
        }

        return list;
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
            .Where(x => typeof(IScraper).IsAssignableFrom(x) && x is { IsInterface: false, IsAbstract: false })
            .ToList();
        List<IScraper> objects = list.FindAll(t => t.GetConstructor(new[] { typeof(ILogger) }) != null)
            .ConvertAll(t =>
            {
                var obj = t.GetConstructor(new[] { typeof(ILogger) })!.Invoke(new object?[]
                    { _logger }) as IScraper;
                return obj;
            })!;
        objects.Sort((x, y) => x.Priority - y.Priority);
        return objects;
    }

    private IEnumerable<IScraper> GetEnabledScrapers()
    {
        var list = GetAllScrapers().ToList();
        list.RemoveAll(s => !s.Enabled);
        return list;
    }

    private IEnumerable<IScraper> GetImageScrapers()
    {
        var list = GetAllScrapers().ToList();
        list.RemoveAll(s => !s.ImgEnabled || !s.Enabled);
        return list;
    }
}