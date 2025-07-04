﻿using System;
using System.Collections.Generic;
using IvInfo.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace IvInfo;

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
            EmbeddedResourcePath = GetType().Namespace + ".Configuration.ivinfo-config.html"
        };
        yield return new PluginPageInfo
        {
            Name = "ivinfo-config.js",
            EmbeddedResourcePath = GetType().Namespace + ".Configuration.ivinfo-config.js"
        };
    }

    public override PluginInfo GetPluginInfo()
    {
        return new PluginInfo(Name, Version, IvInfoConstants.Description, Id, true);
    }
}