MacOS Prerequisites 
===================

These instructions will lead you through preparing to build the diagnostics repo on macOS. We'll start by showing how to set up your environment from scratch.

Environment
===========

Install full Xcode or Apple's Command Line Tools compatible with your macOS version.

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

This builds SOS, the tests, and the SOS plugin (`libsosplugin.dylib`) using the
selected developer tools' LLDB framework. The build and SOS test harness honor
`DEVELOPER_DIR`, falling back to `xcode-select -p`.

To use Command Line Tools without changing the machine-wide Xcode selection:

```sh
export DEVELOPER_DIR=/Library/Developer/CommandLineTools
./build.sh
```

Loading solution file
---------------------

For a better dev inner loop experience, load `build.sln` at the root of the repository into VSCode.

This file is generated from the `build.proj` traversal project and can be regenerated/updated using:

```sh
./eng/generate-sln.sh
```
