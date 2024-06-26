﻿using IvInfo.Providers.Scrapers;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
// ReSharper disable UnusedType.Global

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
    public string? UrlFormatString => "https://www.javlibrary.com/en/?v={0}";
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
    public string? UrlFormatString => "{0}";
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
    public string? UrlFormatString => "https://www.gekiyasu-dvdshop.jp/{0}";
}

public class R18DevExternalId : IExternalId
{
    public bool Supports(IHasProviderIds item)
    {
        return item is Movie;
    }

    public string ProviderName => R18DevScraper.Name;
    public string Key => R18DevScraper.Name;
    public ExternalIdMediaType? Type => null;
    public string? UrlFormatString => "https://r18.dev/videos/vod/movies/detail/-/id={0}/";
}