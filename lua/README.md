# Compiling liblua52.so

Simply run `./build-linux.sh` to (re)build liblua52.so and copy it over to Yafc.

It will take the following steps
1. Download the source, if not available (from https://www.lua.org/ftp/)
2. Check integrity of the source archive
3. Apply the patches (build-linux.shared object, and Factorio fixes)
4. Build the library
5. Copy into Yafc/lib/linux

## Dependencies
Running the build-linux.sh requires
* `curl` to download the source (if available it is not used)
* `sha256` to check the integrity of the download
* `make` to build Lua
* `gcc` to actually build Lua
