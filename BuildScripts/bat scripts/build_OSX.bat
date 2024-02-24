del /s /q Build 
dotnet publish YAFC/YAFC.csproj -r osx-x64 --self-contained false -c Release -o Build/OSX

cd Build
%SystemRoot%\System32\tar.exe -czf OSX.tar.gz OSX

pause;

