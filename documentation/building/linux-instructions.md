Linux Prerequisites 
===================

These instructions will lead you through preparing to build the diagnostics repo on Linux. We'll start by showing how to set up your environment from scratch.

Environment
===========

These instructions are written assuming the Ubuntu 14.04 LTS, since that's the distro the team uses. Pull Requests are welcome to address other environments as long as they don't break the ability to use Ubuntu 14.04 LTS.

There have been reports of issues when using other distros or versions of Ubuntu (e.g. [Issue 95](https://github.com/dotnet/coreclr/issues/95)). If you're on another distribution, consider using docker's `ubuntu:14.04` image.

Minimum RAM required to build is 1GB. The build is known to fail on 512 MB VMs ([Issue 536](https://github.com/dotnet/coreclr/issues/536)).

Toolchain Setup
---------------

#### Ubuntu (14.04, 16.04, 16.10, 17.10, 18.04) ####

Install the following packages: 

- cmake
- make 
- llvm-3.9
- clang-3.9
- lldb-3.9
- liblldb-3.9-dev
- wget
- gettext
- python27

In order to get clang-3.9, llvm-3.9 and lldb-3.9 on Ubuntu 14.04 (see [http://llvm.org/apt/](http://llvm.org/apt/) for the other versions), we need to add an additional package source:

    ~$ echo "deb http://llvm.org/apt/trusty/ llvm-toolchain-trusty-3.9 main" | sudo tee /etc/apt/sources.list.d/llvm.list
    ~$ wget -O - http://llvm.org/apt/llvm-snapshot.gpg.key | sudo apt-key add -
    ~$ sudo apt-get update

Note: arm clang has a known issue with CompareExchange (#15074), so for arm you have to use clang-4.0 or higher, the official build uses clang-5.0.
    
For other version of Debian/Ubuntu, please visit http://apt.llvm.org/.

Then install the packages you need:

    ~$ sudo apt-get install make wget cmake llvm-3.9 clang-3.9 lldb-3.9 liblldb-3.9-dev gettext

The lldb 3.9 package needs a lib file symbolic link fixed:

    cd /usr/lib/llvm-3.9/lib
    sudo ln -s ../../x86_64-linux-gnu/liblldb-3.9.so.1 liblldb-3.9.so.1

You now have all the required components.

#### Alpine (3.6) ####


```sh
~$ sudo apk add clang clang-dev cmake coreutils gcc gettext-dev linux-headers llvm lldb-dev make python

```

#### CentOS (6, 7) ####

llvm, clang and lldb 3.9 will have to be built for this distro ([TBD] add instructions).

```sh
~$ sudo yum install centos-release-SCL epel-release
~$ sudo yum install which wget make cmake cmake3 gcc gcc-c++ python27 python-argparse python-devel

```

#### Debian (8.2) ####

[TBD]

#### Fedora (23, 24, 27, 28) ####

```sh
~$ sudo dnf install which wget cmake make clang llvm-devel lldb-devel python27 

```

#### OpenSuse (42.1, 42.3) ####

[TBD]

Set the maximum number of file-handles
--------------------------------------

To ensure that your system can allocate enough file-handles for the corefx build run `sysctl fs.file-max`. If it is less than 100000, add `fs.file-max = 100000` to `/etc/sysctl.conf`, and then run `sudo sysctl -p`.

