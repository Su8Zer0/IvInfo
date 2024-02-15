@echo off
set JF_DIR=e:\jellyfin
dotnet publish --configuration Debug
pskill -nobanner jellyfin.exe
sleep 3
md $JF_DIR$\jellyfin\data\plugins\IvInfo\
cp bin\Debug\net6.0\Jellyfin.Plugin.IvInfo.dll $JF_DIR$\data\plugins\IvInfo\
cp bin\Debug\net6.0\Jellyfin.Plugin.IvInfo.pdb $JF_DIR$\data\plugins\IvInfo\
cp bin\Debug\net6.0\publish\HtmlAgilityPack.dll $JF_DIR$\data\plugins\IvInfo\
cp bin\Debug\net6.0\publish\F23.StringSimilarity.dll $JF_DIR$\data\plugins\IvInfo\
cp meta.json $JF_DIR$\data\plugins\IvInfo\
start $JF_DIR$\start.cmd
