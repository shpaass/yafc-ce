del /s /q Build 
dotnet publish YAFC/YAFC.csproj -r win-x64 -c Release -o Build/Windows -p:PublishTrimmed=true

cd Build
powershell Compress-Archive Windows Windows.zip

pause;

