rm -rf Build
dotnet publish YAFC/YAFC.csproj -r osx-x64 --self-contained false -c Release -o Build/OSX

pushd Build
tar czf OSX.tar.gz OSX
popd

