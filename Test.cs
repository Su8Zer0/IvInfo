using System;
using System.Threading;
using Jellyfin.Plugin.IvInfo.Providers;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.IvInfo
{
    internal static class Test
    {
        [STAThread]
        private static void Main()
        {
            Console.Out.WriteLine("Test");
            var l = new NullLoggerFactory().CreateLogger<IvInfoProvider>();
            var p = new IvInfoProvider(null!, l);
            var mi = new MovieInfo
            {
                Name = "Name",
                Path = "/media/iv/[REBD-149].mp4"
            };
            // mi.SetProviderId("JavlibraryScraper", "javme3a55i");
            mi.SetProviderId(IvInfoConstants.Name, "REBD-149");
            //p.GetSearchResults(mi, CancellationToken.None).ConfigureAwait(false).GetAwaiter().GetResult();
            var mr = p.GetMetadata(mi, CancellationToken.None).ConfigureAwait(false).GetAwaiter().GetResult();
            mr.Item.PreferredMetadataLanguage = "en";
            p.GetImages(mr.Item, CancellationToken.None).ConfigureAwait(false).GetAwaiter().GetResult();
        }
    }
}