﻿using MediaBrowser.Model.Plugins;
// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace IvInfo.Configuration;

// ReSharper disable once ClassNeverInstantiated.Global
public class IvInfoPluginConfiguration : BasePluginConfiguration
{
    public bool FirstOnly { get; set; }
    public bool Overwriting { get; set; }
    public bool JavlibraryScraperEnabled { get; set; }
    public bool JavlibraryImgEnabled { get; set; }
    public bool JavlibraryTitles { get; set; }
    public bool JavlibraryCast { get; set; }
    public bool JavlibraryTags { get; set; }
    public bool DmmScraperEnabled { get; set; }
    public bool DmmImgEnabled { get; set; }
    public bool DmmGetTrailers { get; set; }
    public bool GekiyasuScraperEnabled { get; set; }
    public bool GekiyasuImgEnabled { get; set; }
    public int JavLibraryScraperPriority { get; set; }
    public int DmmScraperPriority { get; set; }
    public int GekiyasuScraperPriority { get; set; }
}