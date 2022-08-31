Linux Prerequisites
===================

These instructions will lead you through preparing to build and test the diagnostics repo on Linux. We'll start by showing how to set up your environment from scratch. In some cases, a version lldb/llvm that works the best on the distro will have to be built and installed.

Toolchain Setup
---------------

SOS plugin requires lldb v3.4 and above. It is recommeneded to use latest version of llvm toolchain, but any version >= 3.4 should work.

To build or cross build for ARM on Windows or Linux see the instructions [here](https://github.com/dotnet/runtime/blob/main/docs/workflow/building/coreclr/cross-building.md#generating-the-rootfs) in the runtime repo. You will need to clone the runtime [repo](https://github.com/dotnet/runtime.git) and build the appropriate "rootfs" for arm or arm64 using these instructions. You only need to do this once.

Other prerequisites are:

* cmake
* python
* icu

Examples
--------

Debian/Ubuntu:

```sh
sudo apt install cmake clang curl gdb gettext git libicu-dev lldb liblldb-dev libunwind8 llvm make python python-lldb tar wget zip
```

Alpine:

```sh
sudo apk add autoconf bash clang clang-dev cmake coreutils curl gcc gettext-dev git icu-dev krb5-dev libunwind-dev llvm make openssl openssl-dev python which
```

Fedora/CentOS:

```sh
sudo dnf install clang cmake openssl findutils gdb git libicu libunwind lldb-devel llvm-devel make python python2-lldb tar wget which zip
```

OpenSuse:

```sh
sudo zypper install cmake gcc-c++ gdb git hostname libicu libunwind lldb-devel llvm-clang llvm-devel make python python-xml tar wget which zip
```

Clone, build and test the repo
------------------------------

You now have all the required components. To clone, build and test the repo:

```sh
cd $HOME
git clone https://github.com/dotnet/diagnostics.git
cd diagnostics
./build.sh
./test.sh
```
