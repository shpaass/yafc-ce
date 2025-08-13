#! /bin/bash

set -e

if [ ! -f "lua-5.2.1.tar.gz" ]; then
  curl -o "lua-5.2.1.tar.gz" "https://www.lua.org/ftp/lua-5.2.1.tar.gz"
else
  echo "Using available download"
fi

EXPECTED_SHA256="64304da87976133196f9e4c15250b70f444467b6ed80d7cfd7b3b982b5177be5"
if [ "$EXPECTED_SHA256" != "$(sha256sum "lua-5.2.1.tar.gz" | cut -d' ' -f1)" ]; then
  echo "lua-5.2.1.tar.gz has the wrong checksum!"
  exit 1
fi
echo "Found correct checksum"

if [ -d "lua-5.2.1" ]; then
  echo "Cleaning old build"
  rm -r "lua-5.2.1"
fi

echo "Extracting archive"
tar xf "lua-5.2.1.tar.gz"

echo "Applying patches"
patch -p0 -i "liblua.so.patch" || exit 1
patch -d "lua-5.2.1/src" -p1 -i "../../lua-5.2.1.patch" || exit 1

echo "Compiling Lua 5.2.1"
make -C "lua-5.2.1" linux MYCFLAGS="-fPIC"

echo "Copying liblua.so to Yafc"
cp "lua-5.2.1/src/liblua.so.5.2.1"  ../Yafc/lib/linux/liblua52.so

