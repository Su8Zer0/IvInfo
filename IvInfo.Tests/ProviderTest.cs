using IvInfo.Providers;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using Microsoft.Extensions.Logging;

namespace IvInfo.Tests;

[TestFixture]
public class ProviderTest
{
    [Test]
    public void ShouldSupportMovies()
    {
        var logger = new LoggerFactory().CreateLogger<IvInfoProvider>();
        var provider = new IvInfoProvider(null!, logger);
        var item = new Movie();
        Assert.That(provider.Supports(item), Is.True);
    }
    
    [Test]
    public void ShouldNotSupportAudio()
    {
        var logger = new LoggerFactory().CreateLogger<IvInfoProvider>();
        var provider = new IvInfoProvider(null!, logger);
        var item = new Audio();
        Assert.That(provider.Supports(item), Is.False);
    }
    
    [Test]
    public void ShouldNotSupportTv()
    {
        var logger = new LoggerFactory().CreateLogger<IvInfoProvider>();
        var provider = new IvInfoProvider(null!, logger);
        var items = new BaseItem[] { new Episode(), new Season(), new Series() };
        foreach (var item in items)
        {
            Assert.That(provider.Supports(item), Is.False);   
        }
    }
}