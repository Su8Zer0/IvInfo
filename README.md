<p style="text-align: center"><img src="logo.png" alt="IvInfo logo" style="height: 100px"/></p>

# IvInfo plugin for <a href="https://jellyfin.org"><img style="height: 45px; vertical-align: middle" src="https://jellyfin.org/images/logo.svg" alt="Jellyfin logo"/></a>

IV/JAV video info metadata provider.

![Release](https://img.shields.io/github/v/release/Su8Zer0/IvInfo)

Uses data from
<a href="https://javlibrary.com" target="_blank"><img style="height: 18px; vertical-align: middle" src="https://www.javlibrary.com/favicon.ico" alt="Javlibrary icon"/>&nbsp;Javlibrary</a>,
<a href="https://dmm.co.jp" target="_blank"><img style="height: 18px; vertical-align: middle" src="https://p.dmm.co.jp/p/common/pinned/favicon.ico" alt="Javlibrary icon"/>&nbsp;DMM</a> and
<a href="https://www.gekiyasu-dvdshop.jp/" target="_blank"><img style="height: 18px; vertical-align: middle" src="https://www.gekiyasu-dvdshop.jp/favicon.ico" alt="Javlibrary icon"/>&nbsp;Gekiyasu</a>.

Fetches video metadata (description, cast, studio, tags, director, rating - depending on provider) and cover/box/screenshot images.

Data from DMM and Gekiyasu is in japanese, for Javlibrary it can fetch also english translations/cast names.

Also fetches trailers from DMM (trailer feature currently is unusable because there is no possibility to play trailers from remote URLs).

To use the plugin in Jellyfin add a new repository with URL:  
https://raw.githubusercontent.com/Su8Zer0/IvInfo/master/manifest.json  
and then install plugin from Catalogue &#8594; Metadata.
