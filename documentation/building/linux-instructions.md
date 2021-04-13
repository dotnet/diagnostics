Linux Prerequisites 
===================

These instructions will lead you through preparing to build and test the diagnostics repo on Linux. We'll start by showing how to set up your environment from scratch. In some cases, a version lldb/llvm that works the best on the distro will have to be built and installed.

Toolchain Setup
---------------

The following instructions will install the required packages. This only needs to be done once per machine. These instructions assume that you already have "sudo" installed. It is also recommended to create a github fork of the diagnostics repo and cloning that instead of https://github.com/dotnet/diagnostics.git directly.

To build or cross build for ARM on Windows or Linux see the instructions [here](https://github.com/dotnet/runtime/blob/main/docs/workflow/building/coreclr/cross-building.md#generating-the-rootfs) in the runtime repo. You will need to clone the runtime [repo](https://github.com/dotnet/runtime.git) and build the appropriate "rootfs" for arm or arm64 using these instructions. You only need to do this once.

#### Ubuntu 14.04 ####

In order to get clang-3.9, llvm-3.9 and lldb-3.9, we need to add additional package sources (see [http://llvm.org/apt/](http://llvm.org/apt/) for the other Ubuntu versions not listed here):

    sudo apt-get update
    sudo apt-get install wget
    echo "deb http://llvm.org/apt/trusty/ llvm-toolchain-trusty main" | sudo tee /etc/apt/sources.list.d/llvm.list
    echo "deb http://llvm.org/apt/trusty/ llvm-toolchain-trusty-3.9 main" | sudo tee -a /etc/apt/sources.list.d/llvm.list
    wget -O - http://llvm.org/apt/llvm-snapshot.gpg.key | sudo apt-key add -
    sudo apt-get update

Then install the required packages:

    sudo apt-get install cmake clang-3.9 gdb gettext git libicu-dev lldb-3.9 liblldb-3.9-dev libunwind8 llvm-3.9 make python python-lldb-3.9 tar zip

The lldb 3.9 package needs a lib file symbolic link fixed:

    cd /usr/lib/llvm-3.9/lib
    sudo ln -s ../../x86_64-linux-gnu/liblldb-3.9.so.1 liblldb-3.9.so.1

See the section below on how to clone, build and test the repo.

#### Ubuntu 16.04 ####

Add the additional package sources:

    sudo apt-get update
    sudo apt-get install wget
    echo "deb http://llvm.org/apt/xenial/ llvm-toolchain-xenial main" | sudo tee /etc/apt/sources.list.d/llvm.list
    echo "deb http://llvm.org/apt/xenial/ llvm-toolchain-xenial-3.9 main" | sudo tee -a /etc/apt/sources.list.d/llvm.list
    wget -O - http://llvm.org/apt/llvm-snapshot.gpg.key | sudo apt-key add -
    sudo apt-get update

Then install the required packages:

    sudo apt-get install cmake clang-3.9 gdb gettext git libicu-dev lldb-3.9 liblldb-3.9-dev libunwind8 llvm-3.9 make python python-lldb-3.9 tar zip

The lldb 3.9 package needs a lib file symbolic link fixed:

    cd /usr/lib/llvm-3.9/lib
    sudo ln -s ../../x86_64-linux-gnu/liblldb-3.9.so.1 liblldb-3.9.so.1

See the section below on how to clone, build and test the repo.

#### Ubuntu 17.10 ####

Add the additional package sources:

    sudo apt-get update
    sudo apt-get install wget
    echo "deb http://llvm.org/apt/artful/ llvm-toolchain-artful main" | sudo tee /etc/apt/sources.list.d/llvm.list
    echo "deb http://llvm.org/apt/artful/ llvm-toolchain-artful-3.9 main" | sudo tee -a /etc/apt/sources.list.d/llvm.list
    wget -O - http://llvm.org/apt/llvm-snapshot.gpg.key | sudo apt-key add -
    sudo apt-get update

Then install the required packages:

    sudo apt-get install cmake clang-3.9 gdb gettext git libicu-dev lldb-3.9 liblldb-3.9-dev libunwind8 llvm-3.9 make python python-lldb-3.9 tar zip

#### Ubuntu 18.04 ####

Install the required packages:

    sudo apt-get update
    sudo apt-get install cmake clang-3.9 curl gdb gettext git libicu-dev libssl1.0-dev lldb-3.9 liblldb-3.9-dev libunwind8 llvm-3.9 make python-lldb-3.9 tar wget zip 

See the section below on how to clone, build and test the repo.

#### Ubuntu 20.04 ####

Install the required packages:

    sudo apt update
    sudo apt install cmake clang curl gdb gettext git libicu-dev lldb liblldb-dev libunwind8 llvm make python python-lldb tar wget zip 

See the section below on how to clone, build and test the repo.
#### Alpine 3.8/3.9 ####

Install the required packages:

    sudo apk add autoconf bash clang clang-dev cmake coreutils curl gcc gettext-dev git icu-dev krb5-dev libunwind-dev llvm make openssl openssl-dev python which

#### CentOS 6 ####

[TBD]

#### CentOS 7 ####

llvm, clang and lldb 3.9 will have to be built for this distro.

First the prerequisites:

    sudo yum install centos-release-SCL epel-release
    sudo yum install cmake cmake3 gcc gcc-c++ gdb git libicu libunwind make python27 tar wget which zip

Now build and install llvm/lldb 3.9 using the script provided here: [build-install-lldb.sh](../lldb/centos7/build-install-lldb.sh).

WARNING: this script installs llvm and lldb and may overwrite any previously installed versions.

    cd $HOME
    git clone https://github.com/dotnet/diagnostics.git
    $HOME/diagnostics/documentation/lldb/centos7/build-install-lldb.sh

This will take some time to complete, but after it is finished all the required components will be built and installed. To build and test the repo:

    cd diagnostics
    ./build.sh
    ./test.sh

#### Debian 8.2/8.7 ####

In order to get lldb-5.0 (3.9 doesn't seem to work that well on 8.x), we need to add additional package sources:

    sudo apt-get update
    sudo apt-get install wget
    echo "deb http://llvm.org/apt/jessie/ llvm-toolchain-jessie main" | sudo tee /etc/apt/sources.list.d/llvm.list
    echo "deb http://llvm.org/apt/jessie/ llvm-toolchain-jessie-5.0 main" | sudo tee -a /etc/apt/sources.list.d/llvm.list
    wget -O - http://llvm.org/apt/llvm-snapshot.gpg.key | sudo apt-key add -
    sudo apt-get update

Then install the packages you need:

    sudo apt-get install cmake clang-5.0 gdb gettext git libicu-dev liblldb-5.0-dev libunwind8 lldb-5.0 llvm make python-lldb-5.0 tar wget zip

See the section below on how to clone, build and test the repo. Add "--clang5.0" to the ./build.sh command.

#### Debian 9 (Stretch) ####

    sudo apt-get install cmake clang-3.9 gdb gettext git libicu-dev liblldb-3.9-dev libunwind8 lldb-3.9 llvm make python-lldb-3.9 tar wget zip

See the section below on how to clone, build and test the repo.

#### Fedora 24 ####

    sudo dnf install clang cmake findutils gdb git libicu libunwind make python tar wget which zip

Now build and install llvm/lldb 3.9 using the script provided here: [build-install-lldb.sh](../lldb/fedora24/build-install-lldb.sh).

WARNING: this script installs llvm and lldb and may overwrite any previously installed versions.

    cd $HOME
    git clone https://github.com/dotnet/diagnostics.git
    $HOME/diagnostics/documentation/lldb/fedora24/build-install-lldb.sh

This will take some time to complete, but after it is finished all the required components will be built and installed. To build and test the repo:

    cd diagnostics
    ./build.sh
    ./test.sh

#### Fedora 27, 28, 29 ####

    sudo dnf install clang cmake compat-openssl10 findutils gdb git libicu libunwind lldb-devel llvm-devel make python python2-lldb tar wget which zip

See the section below on how to clone, build and test the repo.

#### OpenSuse 42.1, 42.3 ####

    sudo zypper install cmake gcc-c++ gdb git hostname libicu libunwind lldb-devel llvm-clang llvm-devel make python python-xml tar wget which zip
    ln -s /usr/bin/clang++ /usr/bin/clang++-3.5

Now build and install llvm/lldb 3.9 using the script provided here: [build-install-lldb.sh](../lldb/opensuse/build-install-lldb.sh).

WARNING: this script installs llvm and lldb and may overwrite any previously installed versions.

    cd $HOME
    git clone https://github.com/dotnet/diagnostics.git
    $HOME/diagnostics/documentation/lldb/opensuse/build-install-lldb.sh

This will take some time to complete, but after it is finished all the required components will be built and installed. To build and test the repo:

    cd diagnostics
    ./build.sh
    ./test.sh

#### RHEL 7.5 ####

[TBD]

#### SLES ####

[TBD]

Set the maximum number of file-handles
--------------------------------------

To ensure that your system can allocate enough file-handles for the build run `sysctl fs.file-max`. If it is less than 100000, add `fs.file-max = 100000` to `/etc/sysctl.conf`, and then run `sudo sysctl -p`.

Clone, build and test the repo
------------------------------

You now have all the required components. To clone, build and test the repo:

    cd $HOME
    git clone https://github.com/dotnet/diagnostics.git
    cd diagnostics
    ./build.sh
    ./test.sh
