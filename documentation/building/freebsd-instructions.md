FreeBSD Prerequisites 
===================

These instructions will lead you through preparing to build the diagnostics repo on FreeBSD. We'll start by showing how to set up your environment from scratch.

Environment
===========

These instructions are written assuming FreeBSD 10.1-RELEASE, since that's the release the team uses.

These instructions assume you use the binary package tool `pkg` (analog to `apt-get` or `yum` on Linux) to install the environment. Compiling the dependencies from source using the ports tree might work too, but is untested.

Minimum RAM required to build is 1GB. The build is known to fail on 512 MB VMs ([Issue 536](https://github.com/dotnet/coreclr/issues/536)).

Toolchain Setup
---------------

Install the following packages for the toolchain: 

- bash
- cmake
- llvm39 (includes LLVM 3.9, Clang 3.9 and LLDB 3.9)
- gettext
- ninja (optional)
- python27

To install the packages you need:

```sh
sudo pkg install bash cmake llvm39 gettext python27
```
