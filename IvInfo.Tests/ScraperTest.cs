using System.Collections.Immutable;
using IvInfo.Configuration;
using IvInfo.Providers;
using IvInfo.Providers.Scrapers;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Logging;
using Moq;

namespace IvInfo.Tests;

[TestFixture]
public class ScraperTest
{
    public enum Scraper
    {
        JavlibraryScraper,
        DmmScraper,
        GekiyasuScraper,
        R18DevScraper
    }

    private static readonly Dictionary<Scraper, IScraper> Scrapers = new();

    private static IScraper GetScraper(Scraper scraper)
    {
        return Scrapers.GetValueOrDefault(scraper)!;
    }

    [OneTimeSetUp]
    public void SetUpFixture()
    {
        var logger = new LoggerFactory().CreateLogger<IvInfoProvider>();
        Scrapers.Add(Scraper.JavlibraryScraper, new JavlibraryScraper(logger));
        Scrapers.Add(Scraper.DmmScraper, new DmmScraper(logger));
        Scrapers.Add(Scraper.GekiyasuScraper, new GekiyasuScraper(logger));
        Scrapers.Add(Scraper.R18DevScraper, new R18DevScraper(logger));
    }

    [Test]
    [TestCase(Scraper.JavlibraryScraper, 2)]
    [TestCase(Scraper.DmmScraper, 3)]
    [TestCase(Scraper.GekiyasuScraper, 2)]
    [TestCase(Scraper.R18DevScraper, 3)]
    public void HandledImageTypesShouldReturnXEntries(Scraper scraper, int entries)
    {
        var types = GetScraper(scraper).HandledImageTypes();
        Assert.That(types.Count(), Is.EqualTo(entries));
    }

    [Test]
    [TestCase(Scraper.JavlibraryScraper, new[] { ImageType.Primary, ImageType.Box })]
    [TestCase(Scraper.DmmScraper, new[] { ImageType.Primary, ImageType.Box, ImageType.Screenshot })]
    [TestCase(Scraper.GekiyasuScraper, new[] { ImageType.Primary, ImageType.Box })]
    [TestCase(Scraper.R18DevScraper, new[] { ImageType.Primary, ImageType.Box, ImageType.Screenshot })]
    public void HandledImageTypesShouldReturnSpecificTypes(Scraper scraper, ImageType[] imageTypes)
    {
        var types = GetScraper(scraper).HandledImageTypes().ToList();
        foreach (var imageType in imageTypes)
        {
            Assert.That(types, Contains.Item(imageType));
        }
    }

    [Test]
    [TestCase(Scraper.JavlibraryScraper)]
    [TestCase(Scraper.DmmScraper), Category("GithubSkip")]
    [TestCase(Scraper.GekiyasuScraper)]
    [TestCase(Scraper.R18DevScraper)]
    public async Task GetSearchResultsShouldReturnNoResultsForWrongGlobalId(Scraper scraper)
    {
        var resultList = ImmutableList<RemoteSearchResult>.Empty;
        var movieInfo = new MovieInfo
        {
            Name = TestConsts.Name,
            Path = TestConsts.BadPath,
            MetadataCountryCode = TestConsts.Lang.ToUpper(),
            MetadataLanguage = TestConsts.Lang
        };

        var results = await GetScraper(scraper).GetSearchResults(resultList, movieInfo, CancellationToken.None);
        Assert.That(results, Is.Empty);
    }

    [Test]
    [TestCase(Scraper.JavlibraryScraper)]
    [TestCase(Scraper.DmmScraper), Category("GithubSkip")]
    [TestCase(Scraper.GekiyasuScraper)]
    [TestCase(Scraper.R18DevScraper)]
    public async Task GetSearchResultsShouldReturnSingleResult(Scraper scraper)
    {
        var resultList = ImmutableList<RemoteSearchResult>.Empty;
        var movieInfo = new MovieInfo
        {
            Name = TestConsts.Name,
            Path = TestConsts.GoodPath,
            MetadataCountryCode = TestConsts.Lang.ToUpper(),
            MetadataLanguage = TestConsts.Lang
        };

        var results = await GetScraper(scraper).GetSearchResults(resultList, movieInfo, CancellationToken.None);
        Assert.That(results.Count(), Is.EqualTo(1));
    }

    [Test]
    [TestCase(Scraper.JavlibraryScraper)]
    [TestCase(Scraper.GekiyasuScraper)]
    public async Task GetSearchResultsShouldReturnMultipleResults(Scraper scraper)
    {
        var resultList = ImmutableList<RemoteSearchResult>.Empty;
        var movieInfo = new MovieInfo
        {
            Name = TestConsts.Name,
            Path = TestConsts.MultipleResultsPath,
            MetadataCountryCode = TestConsts.Lang.ToUpper(),
            MetadataLanguage = TestConsts.Lang
        };

        var results = await GetScraper(scraper).GetSearchResults(resultList, movieInfo, CancellationToken.None);
        Assert.That(results.Count(), Is.GreaterThan(1));
    }

    [Test]
    [TestCase(Scraper.JavlibraryScraper)]
    [TestCase(Scraper.DmmScraper), Category("GithubSkip")]
    [TestCase(Scraper.GekiyasuScraper)]
    [TestCase(Scraper.R18DevScraper)]
    public async Task FillMetadataShouldReturnFalseWithoutIds(Scraper scraper)
    {
        var info = new ItemLookupInfo
        {
            IsAutomated = false
        };
        var movie = new Movie
        {
            Name = TestConsts.Name
        };
        var metadata = new MetadataResult<Movie>
        {
            Item = movie
        };

        await GetScraper(scraper).FillMetadata(metadata, info, CancellationToken.None);
        Assert.That(metadata.HasMetadata, Is.False);
    }

    [Test]
    [TestCase(Scraper.JavlibraryScraper, JavlibraryScraper.Name, TestConsts.JavlibraryScraperId)]
    [TestCase(Scraper.DmmScraper, DmmScraper.Name, TestConsts.DmmScraperId), Category("GithubSkip")]
    [TestCase(Scraper.GekiyasuScraper, GekiyasuScraper.Name, TestConsts.GekiyasuScraperId)]
    [TestCase(Scraper.R18DevScraper, R18DevScraper.Name, TestConsts.R18DevScraperId)]
    public async Task FillMetadataShouldReturnTrueWithScraperId(Scraper scraper, string scraperName, string scraperId)
    {
        var info = new ItemLookupInfo
        {
            IsAutomated = false
        };
        info.SetProviderId(IvInfoConstants.Name, TestConsts.GoodGlobalId);
        info.SetProviderId(scraperName, scraperId);
        var movie = new Movie
        {
            Name = TestConsts.Name
        };
        movie.SetProviderId(IvInfoConstants.Name, TestConsts.GoodGlobalId);
        movie.SetProviderId(scraperName, scraperId);
        var metadata = new MetadataResult<Movie>
        {
            Item = movie
        };

        var ret = await GetScraper(scraper).FillMetadata(metadata, info, CancellationToken.None);
        Assert.That(ret, Is.True);
    }

    [Test]
    [TestCase(Scraper.JavlibraryScraper, JavlibraryScraper.Name, TestConsts.JavlibraryScraperId)]
    [TestCase(Scraper.R18DevScraper, R18DevScraper.Name, TestConsts.R18DevKanjiTitleScraperId)]
    public async Task FillMetadataShouldFillEnglishData(Scraper scraper, string scraperName, string scraperId)
    {
        var info = new ItemLookupInfo
        {
            IsAutomated = false
        };
        info.SetProviderId(IvInfoConstants.Name, TestConsts.GoodGlobalId);
        info.SetProviderId(scraperName, scraperId);
        var movie = new Movie
        {
            Name = TestConsts.Name
        };
        movie.SetProviderId(IvInfoConstants.Name, TestConsts.GoodGlobalId);
        movie.SetProviderId(scraperName, scraperId);
        var metadata = new MetadataResult<Movie>
        {
            Item = movie
        };

        var paths = new Mock<IApplicationPaths>();
        paths.Setup(p => p.PluginsPath).Returns(".");
        paths.Setup(p => p.PluginConfigurationsPath).Returns(".");
        var config = new IvInfoPluginConfiguration
        {
            R18DevTitles = true,
            R18DevCast = true,
            R18DevTags = true
        };
        var xml = new Mock<IXmlSerializer>();
        xml.Setup(x => x.DeserializeFromFile(It.IsAny<Type>(), It.IsAny<string>())).Returns(config);

        var ivInfo = new IvInfo(paths.Object, xml.Object);

        var ret = await GetScraper(scraper).FillMetadata(metadata, info, CancellationToken.None);
        Assert.That(ret, Is.True);
        Assert.That(metadata.Item.OriginalTitle, Is.EqualTo(TestConsts.EngTitle));
    }

    [Test]
    [TestCase(Scraper.DmmScraper, DmmScraper.Name, TestConsts.DmmScraperId), Category("GithubSkip")]
    [TestCase(Scraper.R18DevScraper, R18DevScraper.Name, TestConsts.R18DevScraperId)]
    public async Task FillMetadataShouldSetTrailerUrl(Scraper scraper, string scraperName, string scraperId)
    {
        var info = new ItemLookupInfo
        {
            IsAutomated = false
        };
        info.SetProviderId(IvInfoConstants.Name, TestConsts.GoodGlobalId);
        info.SetProviderId(scraperName, scraperId);
        var movie = new Movie
        {
            Name = TestConsts.Name
        };
        movie.SetProviderId(IvInfoConstants.Name, TestConsts.GoodGlobalId);
        movie.SetProviderId(scraperName, scraperId);
        var metadata = new MetadataResult<Movie>
        {
            Item = movie
        };
        
        var paths = new Mock<IApplicationPaths>();
        paths.Setup(p => p.PluginsPath).Returns(".");
        paths.Setup(p => p.PluginConfigurationsPath).Returns(".");
        var config = new IvInfoPluginConfiguration
        {
            R18DevGetTrailers = true
        };
        var xml = new Mock<IXmlSerializer>();
        xml.Setup(x => x.DeserializeFromFile(It.IsAny<Type>(), It.IsAny<string>())).Returns(config);

        var ivInfo = new IvInfo(paths.Object, xml.Object);

        var ret = await GetScraper(scraper).FillMetadata(metadata, info, CancellationToken.None);
        Assert.That(metadata.Item.RemoteTrailers.Count, Is.EqualTo(1));
    }
    
    [Test]
    [TestCase(Scraper.JavlibraryScraper)]
    [TestCase(Scraper.DmmScraper), Category("GithubSkip")]
    [TestCase(Scraper.GekiyasuScraper)]
    [TestCase(Scraper.R18DevScraper)]
    public async Task GetImagesShouldReturnEmptyListForMissingScraperId(Scraper scraper)
    {
        var info = new ItemLookupInfo
        {
            IsAutomated = false
        };
        info.SetProviderId(IvInfoConstants.Name, TestConsts.GoodGlobalId);
        var movie = new Movie
        {
            Name = TestConsts.Name,
            PreferredMetadataLanguage = TestConsts.Lang,
            PreferredMetadataCountryCode = TestConsts.Lang.ToUpper()
        };
        movie.SetProviderId(IvInfoConstants.Name, TestConsts.GoodGlobalId);

        var ret = await GetScraper(scraper).GetImages(movie, CancellationToken.None);
        Assert.That(ret, Is.Empty);
    }

    [Test]
    [TestCase(Scraper.JavlibraryScraper, JavlibraryScraper.Name, TestConsts.JavlibraryScraperId)]
    [TestCase(Scraper.DmmScraper, DmmScraper.Name, TestConsts.DmmScraperId), Category("GithubSkip")]
    [TestCase(Scraper.GekiyasuScraper, GekiyasuScraper.Name, TestConsts.GekiyasuScraperId)]
    [TestCase(Scraper.R18DevScraper, R18DevScraper.Name, TestConsts.R18DevScraperId)]
    public async Task GetImagesShouldReturnSingleItemForPrimaryImage(Scraper scraper, string scraperName,
        string scraperId)
    {
        var info = new ItemLookupInfo
        {
            IsAutomated = false
        };
        info.SetProviderId(IvInfoConstants.Name, TestConsts.GoodGlobalId);
        info.SetProviderId(scraperName, scraperId);
        var movie = new Movie
        {
            Name = TestConsts.Name,
            PreferredMetadataLanguage = TestConsts.Lang,
            PreferredMetadataCountryCode = TestConsts.Lang.ToUpper()
        };
        movie.SetProviderId(IvInfoConstants.Name, TestConsts.GoodGlobalId);
        movie.SetProviderId(scraperName, scraperId);

        var ret = await GetScraper(scraper).GetImages(movie, CancellationToken.None);
        Assert.That(ret.Count(), Is.EqualTo(1));
    }

    [Test]
    [TestCase(Scraper.JavlibraryScraper, JavlibraryScraper.Name, TestConsts.JavlibraryScraperId)]
    [TestCase(Scraper.DmmScraper, DmmScraper.Name, TestConsts.DmmScraperId), Category("GithubSkip")]
    [TestCase(Scraper.GekiyasuScraper, GekiyasuScraper.Name, TestConsts.GekiyasuScraperId)]
    [TestCase(Scraper.R18DevScraper, R18DevScraper.Name, TestConsts.R18DevScraperId)]
    public async Task GetImagesShouldReturnSingleItemForBoxImage(Scraper scraper, string scraperName, string scraperId)
    {
        var info = new ItemLookupInfo
        {
            IsAutomated = false
        };
        info.SetProviderId(IvInfoConstants.Name, TestConsts.GoodGlobalId);
        info.SetProviderId(scraperName, scraperId);
        var movie = new Movie
        {
            Name = TestConsts.Name,
            PreferredMetadataLanguage = TestConsts.Lang,
            PreferredMetadataCountryCode = TestConsts.Lang.ToUpper()
        };
        movie.SetProviderId(IvInfoConstants.Name, TestConsts.GoodGlobalId);
        movie.SetProviderId(scraperName, scraperId);

        var ret = await GetScraper(scraper).GetImages(movie, CancellationToken.None, ImageType.Box);
        Assert.That(ret.Count(), Is.EqualTo(1));
    }

    [Test]
    [TestCase(Scraper.DmmScraper, DmmScraper.Name, TestConsts.DmmScraperId), Category("GithubSkip")]
    [TestCase(Scraper.R18DevScraper, R18DevScraper.Name, TestConsts.R18DevScraperId)]
    public async Task GetImagesShouldReturnItemsForScreenshotImage(Scraper scraper, string scraperName,
        string scraperId)
    {
        var info = new ItemLookupInfo
        {
            IsAutomated = false
        };
        info.SetProviderId(IvInfoConstants.Name, TestConsts.GoodGlobalId);
        info.SetProviderId(scraperName, scraperId);
        var movie = new Movie
        {
            Name = TestConsts.Name
        };
        movie.SetProviderId(IvInfoConstants.Name, TestConsts.GoodGlobalId);
        movie.SetProviderId(scraperName, scraperId);
        var metadata = new MetadataResult<Movie>
        {
            Item = movie
        };

        var ret = await GetScraper(scraper).FillMetadata(metadata, info, CancellationToken.None);
        Assert.That(ret, Is.True);
    }
    
    [Test]
    [TestCase(Scraper.JavlibraryScraper, JavlibraryScraper.Name, TestConsts.JavlibraryScraperId)]
    [TestCase(Scraper.DmmScraper, DmmScraper.Name, TestConsts.DmmScraperId), Category("GithubSkip")]
    [TestCase(Scraper.GekiyasuScraper, GekiyasuScraper.Name, TestConsts.GekiyasuScraperId)]
    [TestCase(Scraper.R18DevScraper, R18DevScraper.Name, TestConsts.R18DevScraperId)]
    public async Task GetImagesShouldReturnEmptyListForOtherImageTypes(Scraper scraper, string scraperName,
        string scraperId)
    {
        var info = new ItemLookupInfo
        {
            IsAutomated = false
        };
        info.SetProviderId(IvInfoConstants.Name, TestConsts.GoodGlobalId);
        info.SetProviderId(scraperName, scraperId);
        var movie = new Movie
        {
            Name = TestConsts.Name,
            PreferredMetadataLanguage = TestConsts.Lang,
            PreferredMetadataCountryCode = TestConsts.Lang.ToUpper()
        };
        movie.SetProviderId(IvInfoConstants.Name, TestConsts.GoodGlobalId);
        movie.SetProviderId(scraperName, scraperId);

        var types = new[]
        {
            ImageType.Backdrop, ImageType.Art, ImageType.Disc, ImageType.Banner, ImageType.Chapter, ImageType.Logo,
            ImageType.Menu, ImageType.Profile, ImageType.Thumb, ImageType.BoxRear
        };
        foreach (var type in types)
        {
            var ret = await GetScraper(scraper).GetImages(movie, CancellationToken.None, type);
            Assert.That(ret, Is.Empty);
        }
    }
}