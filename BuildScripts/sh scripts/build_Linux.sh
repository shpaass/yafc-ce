rm -rf Build
dotnet publish YAFC/YAFC.csproj -r linux-x64 --self-contained false -c Release -o Build/Linux

pushd Build
tar czf Linux.tar.gz Linux
popd

