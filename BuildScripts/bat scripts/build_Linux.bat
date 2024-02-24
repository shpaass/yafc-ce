del /s /q Build 
dotnet publish YAFC/YAFC.csproj -r linux-x64 --self-contained false -c Release -o Build/Linux

cd Build
%SystemRoot%\System32\tar.exe -czf Linux.tar.gz Linux

pause;

