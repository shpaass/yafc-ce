rm -rf Build
dotnet publish YAFC/YAFC.csproj -r win-x64 -c Release -o Build/Windows -p:PublishTrimmed=true

pushd Build
# The following command may not work on the usual installation of Windows 
# due to the lack of the zip command.
zip -r Windows.zip Windows
popd

