MacOS Prerequisites 
===================

These instructions will lead you through preparing to build the diagnostics repo on macOS. We'll start by showing how to set up your environment from scratch.

Environment
===========

These instructions were validated on macOS 10.12.6 (Sierra) and Xcode 9.2.

Git Setup
---------

Clone the diagnostics repository (either upstream or a fork).

```sh
git clone https://github.com/dotnet/diagnostics
```

CMake
-----

This repo has a dependency on CMake for the build. You can download it from [CMake downloads](http://www.cmake.org/download/).

Alternatively, you can install CMake from [Homebrew](http://brew.sh/).

```sh
brew install cmake
```


Building
--------

In the root of the diagnostics repo run:

```sh
./build.sh
```

This will build SOS, the tests and the SOS plugin (libsosplugin.dylib) for the Xcode (9.2) version of lldb (swift 4.0) on the device.

For later versions of macOS/Xcode/lldb is TBD.

