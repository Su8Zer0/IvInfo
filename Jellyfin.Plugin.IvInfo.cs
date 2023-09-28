using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using Jellyfin.Plugin.IvInfo.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.IvInfo;

// ReSharper disable once ClassNeverInstantiated.Global
public class IvInfo : BasePlugin<IvInfoPluginConfiguration>, IHasWebPages
{
    public IvInfo(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer) : base(applicationPaths,
        xmlSerializer)
    {
        Instance = this;
    }

    /// <summary>
    ///     Gets the current plugin instance.
    /// </summary>
    public static IvInfo? Instance { get; private set; }

    public override string Name => IvInfoConstants.Name;

    public override Guid Id => Guid.Parse(IvInfoConstants.Guid);

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        yield return new PluginPageInfo
        {
            Name = Name,
            EmbeddedResourcePath = GetType().Namespace + ".Configuration.config.html"
        };
        yield return new PluginPageInfo
        {
            Name = "config.js",
            EmbeddedResourcePath = GetType().Namespace + ".Configuration.config.js"
        };
    }

    public override PluginInfo GetPluginInfo()
    {
        try
        {
            var thisAssem = typeof(IvInfo).Assembly;
            var thisAssemName = thisAssem.GetName();
            var ver = thisAssemName.Version ?? new Version("");
            return new PluginInfo(Name, ver, IvInfoConstants.Description, Id, true);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"Couldn't read version info from assembly\n{e.Message}");
        }
        
        return new PluginInfo(Name, Version, IvInfoConstants.Description, Id, true);
    }
}