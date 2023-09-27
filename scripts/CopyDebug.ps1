$JF_DIR = "e:\jellyfin"
$CUR_DIR = Get-Location
Set-Location ..
dotnet publish --configuration Debug
Start-Process "pskill" -Wait -NoNewWindow -ArgumentList "-nobanner jellyfin.exe"
Start-Sleep -Seconds 3
New-Item -Path "$( $JF_DIR )\data\plugins\IvInfo\" -ItemType Directory -ErrorAction SilentlyContinue
Copy-Item ".\bin\Debug\net6.0\Jellyfin.Plugin.IvInfo.dll" -Destination "$( $JF_DIR )\data\plugins\IvInfo\"
Copy-Item ".\bin\Debug\net6.0\Jellyfin.Plugin.IvInfo.pdb" -Destination "$( $JF_DIR )\data\plugins\IvInfo\"
Copy-Item ".\bin\Debug\net6.0\publish\HtmlAgilityPack.dll" -Destination "$( $JF_DIR )\data\plugins\IvInfo\"
Copy-Item ".\bin\Debug\net6.0\publish\F23.StringSimilarity.dll" -Destination "$( $JF_DIR )\data\plugins\IvInfo\"
Copy-Item ".\meta.json" -Destination "$( $JF_DIR )\data\plugins\IvInfo\"
Set-Location $CUR_DIR
Start-Process "$( $JF_DIR )\jellyfin.exe" -Wait -WorkingDirectory $JF_DIR -NoNewWindow -ArgumentList "--datadir $( $JF_DIR )\data"