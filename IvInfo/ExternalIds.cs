using IvInfo.Providers.Scrapers;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace IvInfo;

public class IvInfoExternalId : IExternalId
{
    public bool Supports(IHasProviderIds item)
    {
        return item is Movie;
    }

    public string ProviderName => IvInfoConstants.Name;
    public string Key => IvInfoConstants.Name;
    public ExternalIdMediaType? Type => null;
    public string? UrlFormatString => null;
}

public class JavlibraryScraperExternalId : IExternalId
{
    public bool Supports(IHasProviderIds item)
    {
        return item is Movie;
    }

    public string ProviderName => JavlibraryScraper.Name;
    public string Key => JavlibraryScraper.Name;
    public ExternalIdMediaType? Type => null;
    public string? UrlFormatString => null;
}

public class DmmScraperExternalId : IExternalId
{
    public bool Supports(IHasProviderIds item)
    {
        return item is Movie;
    }

    public string ProviderName => DmmScraper.Name;
    public string Key => DmmScraper.Name;
    public ExternalIdMediaType? Type => null;
    public string? UrlFormatString => null;
}

public class GekiyasuScraperExternalId : IExternalId
{
    public bool Supports(IHasProviderIds item)
    {
        return item is Movie;
    }

    public string ProviderName => GekiyasuScraper.Name;
    public string Key => GekiyasuScraper.Name;
    public ExternalIdMediaType? Type => null;
    public string? UrlFormatString => null;
}