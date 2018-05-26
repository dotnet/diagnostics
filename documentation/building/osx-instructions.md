MacOS Prerequisites 
===================

These instructions will lead you through preparing to build the diagnostics repo on macOS. We'll start by showing how to set up your environment from scratch.

Environment
===========

These instructions were validated on macOS 10.12. Sierra.

If your machine has Command Line Tools for XCode 6.3 installed, you'll need to update them to the 6.3.1 version or higher in order to successfully build. There was an issue with the headers that shipped with version 6.3 that was subsequently fixed in 6.3.1.

Git Setup
---------

Clone the diagnostics repository (either upstream or a fork).

```sh
$ git clone https://github.com/dotnet/diagnostics
```


CMake
-----

This repo has a dependency on CMake for the build. You can download it from [CMake downloads](http://www.cmake.org/download/).

Alternatively, you can install CMake from [Homebrew](http://brew.sh/).

```sh
brew install cmake
```
