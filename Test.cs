using System;
using System.Threading;
using Jellyfin.Plugin.IvInfo.Providers;
using MediaBrowser.Controller.Providers;
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
                Path = "/media/iv/[REBD-700].mkv",
                MetadataCountryCode = "EN",
                MetadataLanguage = "en"
            };
            // mi.SetProviderId("JavlibraryScraper", "javme3a55i");
            // mi.SetProviderId(IvInfoConstants.Name, "OAE-205");
            // var sr = p.GetSearchResults(mi, CancellationToken.None).ConfigureAwait(false).GetAwaiter().GetResult();
            // foreach (var r in sr)
            // {
            //     foreach (var id in r.ProviderIds)
            //     {
            //         if (!mi.ProviderIds.ContainsKey(id.Key))
            //         {
            //             mi.ProviderIds.Add(id.Key, id.Value);
            //         }
            //     }
            // }
            var mr = p.GetMetadata(mi, CancellationToken.None).ConfigureAwait(false).GetAwaiter().GetResult();
            mr.Item.PreferredMetadataLanguage = "en";
            mr.Item.PreferredMetadataCountryCode = "EN";
            // p.GetImages(mr.Item, CancellationToken.None).ConfigureAwait(false).GetAwaiter().GetResult();
        }
    }
}