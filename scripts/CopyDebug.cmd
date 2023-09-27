@echo off
set JF_DIR=e:\jellyfin
for /f %%d in ('cd') do set CUR_DIR=%%d
cd ..
dotnet publish --configuration Debug
pskill -nobanner jellyfin.exe
sleep 3
md $JF_DIR$\jellyfin\data\plugins\IvInfo\
cp bin\Debug\net6.0\Jellyfin.Plugin.IvInfo.dll $JF_DIR$\data\plugins\IvInfo\
cp bin\Debug\net6.0\Jellyfin.Plugin.IvInfo.pdb $JF_DIR$\data\plugins\IvInfo\
cp bin\Debug\net6.0\publish\HtmlAgilityPack.dll $JF_DIR$\data\plugins\IvInfo\
cp bin\Debug\net6.0\publish\F23.StringSimilarity.dll $JF_DIR$\data\plugins\IvInfo\
cp meta.json $JF_DIR$\data\plugins\IvInfo\
cd %CUR_DIR%
start $JF_DIR$\jellyfin.exe --datadir $JF_DIR\data