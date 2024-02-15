using System.Collections.Immutable;
using IvInfo.Providers;
using IvInfo.Providers.Scrapers;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace IvInfo.Tests;

/**
 * Tests in <c>GithubSkip</c> category will not be executed in Github action because Github actions hosts cannot connect to DMM pages.
 */
[TestFixture]
[TestOf("DmmScraper")]
public class DmmScraperTest
{
    private IScraper _scraper = null!;

    [SetUp]
    public void SetUp()
    {
        var logger = new LoggerFactory().CreateLogger<IvInfoProvider>();
        _scraper = new DmmScraper(logger);
    }

    [Test]
    public void HandledImageTypesShouldReturnThreeEntries()
    {
        var types = _scraper.HandledImageTypes();
        Assert.That(types.Count(), Is.EqualTo(3));
    }

    [Test]
    public void HandledImageTypesShouldReturnPrimaryBoxAndScreenshot()
    {
        var types = _scraper.HandledImageTypes().ToList();
        Assert.That(types, Contains.Item(ImageType.Primary));
        Assert.That(types, Contains.Item(ImageType.Box));
        Assert.That(types, Contains.Item(ImageType.Screenshot));
    }

    [Test, Category("GithubSkip")]
    public async Task GetSearchResultsShouldReturnNoResultsForWrongGlobalId()
    {
        var resultList = ImmutableList<RemoteSearchResult>.Empty;
        var movieInfo = new MovieInfo
        {
            Name = TestConsts.Name,
            Path = TestConsts.BadPath,
            MetadataCountryCode = TestConsts.Lang.ToUpper(),
            MetadataLanguage = TestConsts.Lang
        };

        var results = await _scraper.GetSearchResults(resultList, movieInfo, CancellationToken.None);
        Assert.That(results, Is.Empty);
    }

    [Test, Category("GithubSkip")]
    public async Task GetSearchResultsShouldReturnSingleResult()
    {
        var resultList = ImmutableList<RemoteSearchResult>.Empty;
        var movieInfo = new MovieInfo
        {
            Name = TestConsts.Name,
            Path = TestConsts.GoodPath,
            MetadataCountryCode = TestConsts.Lang.ToUpper(),
            MetadataLanguage = TestConsts.Lang
        };

        var results = await _scraper.GetSearchResults(resultList, movieInfo, CancellationToken.None);
        Assert.That(results.Count(), Is.EqualTo(1));
    }

    [Test, Category("GithubSkip")]
    public async Task FillMetadataShouldReturnFalseWithoutIds()
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

        await _scraper.FillMetadata(metadata, info, CancellationToken.None);
        Assert.That(metadata.HasMetadata, Is.False);
    }

    [Test, Category("GithubSkip")]
    public async Task FillMetadataShouldReturnTrueWithScraperId()
    {
        var info = new ItemLookupInfo
        {
            IsAutomated = false
        };
        info.SetProviderId(IvInfoConstants.Name, TestConsts.GoodGlobalId);
        info.SetProviderId(DmmScraper.Name, TestConsts.DmmScraperId);
        var movie = new Movie
        {
            Name = TestConsts.Name
        };
        movie.SetProviderId(IvInfoConstants.Name, TestConsts.GoodGlobalId);
        movie.SetProviderId(DmmScraper.Name, TestConsts.DmmScraperId);
        var metadata = new MetadataResult<Movie>
        {
            Item = movie
        };

        var ret = await _scraper.FillMetadata(metadata, info, CancellationToken.None);
        Assert.That(ret, Is.True);
    }

    [Test, Category("GithubSkip")]
    public async Task GetImagesShouldReturnEmptyListForMissingScraperId()
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

        var ret = await _scraper.GetImages(movie, CancellationToken.None);
        Assert.That(ret, Is.Empty);
    }

    [Test, Category("GithubSkip")]
    public async Task GetImagesShouldReturnSingleItemForPrimaryImage()
    {
        var info = new ItemLookupInfo
        {
            IsAutomated = false
        };
        info.SetProviderId(IvInfoConstants.Name, TestConsts.GoodGlobalId);
        info.SetProviderId(DmmScraper.Name, TestConsts.DmmScraperId);
        var movie = new Movie
        {
            Name = TestConsts.Name,
            PreferredMetadataLanguage = TestConsts.Lang,
            PreferredMetadataCountryCode = TestConsts.Lang.ToUpper()
        };
        movie.SetProviderId(IvInfoConstants.Name, TestConsts.GoodGlobalId);
        movie.SetProviderId(DmmScraper.Name, TestConsts.DmmScraperId);

        var ret = await _scraper.GetImages(movie, CancellationToken.None);
        Assert.That(ret.Count(), Is.EqualTo(1));
    }

    [Test, Category("GithubSkip")]
    public async Task GetImagesShouldReturnSingleItemForBoxImage()
    {
        var info = new ItemLookupInfo
        {
            IsAutomated = false
        };
        info.SetProviderId(IvInfoConstants.Name, TestConsts.GoodGlobalId);
        info.SetProviderId(DmmScraper.Name, TestConsts.DmmScraperId);
        var movie = new Movie
        {
            Name = TestConsts.Name,
            PreferredMetadataLanguage = TestConsts.Lang,
            PreferredMetadataCountryCode = TestConsts.Lang.ToUpper()
        };
        movie.SetProviderId(IvInfoConstants.Name, TestConsts.GoodGlobalId);
        movie.SetProviderId(DmmScraper.Name, TestConsts.DmmScraperId);

        var ret = await _scraper.GetImages(movie, CancellationToken.None, ImageType.Box);
        Assert.That(ret.Count(), Is.EqualTo(1));
    }

    [Test, Category("GithubSkip")]
    public async Task GetImagesShouldReturnEmptyListForOtherImageTypes()
    {
        var info = new ItemLookupInfo
        {
            IsAutomated = false
        };
        info.SetProviderId(IvInfoConstants.Name, TestConsts.GoodGlobalId);
        info.SetProviderId(DmmScraper.Name, TestConsts.DmmScraperId);
        var movie = new Movie
        {
            Name = TestConsts.Name,
            PreferredMetadataLanguage = TestConsts.Lang,
            PreferredMetadataCountryCode = TestConsts.Lang.ToUpper()
        };
        movie.SetProviderId(IvInfoConstants.Name, TestConsts.GoodGlobalId);
        movie.SetProviderId(DmmScraper.Name, TestConsts.DmmScraperId);

        var ret = await _scraper.GetImages(movie, CancellationToken.None, ImageType.Backdrop);
        Assert.That(ret, Is.Empty);
        ret = await _scraper.GetImages(movie, CancellationToken.None, ImageType.Art);
        Assert.That(ret, Is.Empty);
        ret = await _scraper.GetImages(movie, CancellationToken.None, ImageType.Disc);
        Assert.That(ret, Is.Empty);
        ret = await _scraper.GetImages(movie, CancellationToken.None, ImageType.Banner);
        Assert.That(ret, Is.Empty);
        ret = await _scraper.GetImages(movie, CancellationToken.None, ImageType.Chapter);
        Assert.That(ret, Is.Empty);
        ret = await _scraper.GetImages(movie, CancellationToken.None, ImageType.Logo);
        Assert.That(ret, Is.Empty);
        ret = await _scraper.GetImages(movie, CancellationToken.None, ImageType.Menu);
        Assert.That(ret, Is.Empty);
        ret = await _scraper.GetImages(movie, CancellationToken.None, ImageType.Profile);
        Assert.That(ret, Is.Empty);
        ret = await _scraper.GetImages(movie, CancellationToken.None, ImageType.Thumb);
        Assert.That(ret, Is.Empty);
        ret = await _scraper.GetImages(movie, CancellationToken.None, ImageType.BoxRear);
        Assert.That(ret, Is.Empty);
    }
}