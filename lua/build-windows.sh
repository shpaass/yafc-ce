#! /bin/bash

set -e

if [ ! -f "lua-5.2.2.tar.gz" ]; then
  curl -o "lua-5.2.2.tar.gz" "https://www.lua.org/ftp/lua-5.2.2.tar.gz"
else
  echo "Using available download"
fi

EXPECTED_SHA256="3fd67de3f5ed133bf312906082fa524545c6b9e1b952e8215ffbd27113f49f00"
if [ "$EXPECTED_SHA256" != "$(sha256sum "lua-5.2.2.tar.gz" | cut -d' ' -f1)" ]; then
  echo "lua-5.2.2.tar.gz has the wrong checksum!"
  exit 1
fi
echo "Found correct checksum"

if [ -d "lua-5.2.2" -o -d "lua52" ]; then
  echo "Cleaning old build"
  rm -rf "lua-5.2.2"
  rm -rf "lua52"
fi

echo "Extracting archive"
tar xf "lua-5.2.2.tar.gz"

echo "Applying patches"
patch -d "lua-5.2.2/src" -p1 -i "../../lua-5.2.2.patch" || exit 1

addToPath() {
  echo MSBuild found in $1
  export "PATH=$PATH:$1"
}

# Set the path to msbuild

# VS 2026
if [ -e "/c/Program Files/Microsoft Visual Studio/18/Enterprise/MSBuild/Microsoft/VC/v180/Microsoft.Cpp.Default.props" ]; then
  addToPath "/c/Program Files/Microsoft Visual Studio/18/Enterprise/MSBuild/Current/Bin"
elif [ -e "/c/Program Files/Microsoft Visual Studio/18/Professional/MSBuild/Microsoft/VC/v180/Microsoft.Cpp.Default.props" ]; then
  addToPath "/c/Program Files/Microsoft Visual Studio/18/Professional/MSBuild/Current/Bin"
elif [ -e "/c/Program Files/Microsoft Visual Studio/18/Community/MSBuild/Microsoft/VC/v180/Microsoft.Cpp.Default.props" ]; then
  addToPath "/c/Program Files/Microsoft Visual Studio/18/Community/MSBuild/Current/Bin"
elif [ -e "/c/Program Files/Microsoft Visual Studio/18/Insiders/MSBuild/Microsoft/VC/v180/Microsoft.Cpp.Default.props" ]; then
  addToPath "/c/Program Files/Microsoft Visual Studio/18/Insiders/MSBuild/Current/Bin"

# VS 2026 Build Tools
elif [ -e "/c/Program Files (x86)/Microsoft Visual Studio/18/BuildTools/MSBuild/Microsoft/VC/v180/Microsoft.Cpp.Default.props" ]; then
  addToPath "/c/Program Files (x86)/Microsoft Visual Studio/18/BuildTools/MSBuild/Current/Bin"

else
  echo ERROR: Could not find an MSBuild with an installed C++ compiler. && exit 1
fi

echo "Compiling Lua 5.2.2"
msbuild.exe -t:Build -p:Configuration=Release

echo "Copying lua52.dll to Yafc"
cp x64/Release/lua52.dll  ../Yafc/lib/windows/lua52.dll
