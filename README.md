# IvInfo plugin for [Jellyfin](https://jellyfin.org).
Idol/JAV video info metadata provider.

Uses data from [Javlibrary](https://javlibrary.com), [DMM](https://dmm.co.jp) and [Gekiyasu](https://www.gekiyasu-dvdshop.jp/).

Fetches video metadata (description, cast, studio, tags, director, rating - depending on provider) and cover/box/screenshot images.

Data from DMM and Gekiyasu is in japanese, for Javlibrary it can fetch also english translations/cast names.

Also fetches trailers from DMM (trailer feature currently is unusable because there is no possibility to play trailers from remote URLs).

To use the plugin in Jellyfin add a new repository with URL: https://raw.githubusercontent.com/Su8Zer0/IvInfo/master/manifest.json and then install plugin from Catalogue -> Metadata.
