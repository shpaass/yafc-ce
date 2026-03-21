# Compiling liblua52.so

Simply run `./build-linux.sh` to (re)build liblua52.so and copy it over to Yafc. The script will compile 
lua with your CFLAGS and LDFLAGS, so feel free to use them if you need debug symbols or security tweaks, for instance.

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

# Compiling lua52.dll

Run `./build-windows.sh` at a Git Bash prompt to (re)build lua52.dll and copy it over to Yafc.

It will take the following steps
1. Download the source, if not available (from https://www.lua.org/ftp/)
2. Check integrity of the source archive
3. Apply the patches (Factorio fixes)
4. Build the library
5. Copy into Yafc/lib/windows

The build is not deterministic; each build generates a new guid and timestamp.

## Dependencies
Running build-windows.sh requires
* Git for Windows (https://git-scm.com/install/windows)
* MSBuild, installed by any edition of Visual Studio 2026, including Visual Studio Build Tools 2026.
[The build tools installer](https://aka.ms/vs/stable/vs_buildtools.exe) can be found on [this page](https://learn.microsoft.com/en-us/visualstudio/releases/2026/release-history#installation-of-visual-studio).
* The "Desktop Development with C++" workload found in the above installers.

If you wish to use MSBuild and the C compiler from VS 2022 instead, change the paths in build-windows.sh from `18` and `v180` to `2022` and `v170`, and change lua52.vcxproj from `v145` to `v143`.
