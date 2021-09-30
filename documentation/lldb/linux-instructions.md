Installing LLDB on Linux
========================

These instructions will lead you through installing or building the best version of lldb for your distro to use with SOS. If you have already followed the diagnostics repo build [prerequisites](../building/linux-instructions.md) and built the diagnostics repo, then the best version of lldb is already installed.

SOS needs at least lldb 3.9 or greater. Some distros only have older versions available by default so there are directions and scripts to build lldb 3.9 for that platform. These instructions assume that you have dotnet cli and its prerequisites installed.

The libsosplugin.so built for lldb 3.9 does work with lldb 4.0 and greater but most of the testing has been on lldb 3.9. 

lldb 10.0 or greater is recommended if available for the distro version. For arm32, it is recommended to debug on Ubuntu 18.04 if possible with lldb 10.0 which is the only version of lldb found that works with SOS on arm32.

#### Ubuntu 14.04 ####

In order to get lldb-3.9, we need to add additional package sources (see [http://llvm.org/apt/](http://llvm.org/apt/) for the other Ubuntu versions not listed here):

    sudo apt-get update
    sudo apt-get install wget
    echo "deb http://llvm.org/apt/trusty/ llvm-toolchain-trusty main" | sudo tee /etc/apt/sources.list.d/llvm.list
    echo "deb http://llvm.org/apt/trusty/ llvm-toolchain-trusty-3.9 main" | sudo tee -a /etc/apt/sources.list.d/llvm.list
    wget -O - http://llvm.org/apt/llvm-snapshot.gpg.key | sudo apt-key add -
    sudo apt-get update

Install the lldb packages:

    sudo apt-get install lldb-3.9 python-lldb-3.9

To launch lldb:

    lldb-3.9

#### Ubuntu 16.04 ####

Add the additional package sources:

    sudo apt-get update
    sudo apt-get install wget
    echo "deb http://llvm.org/apt/xenial/ llvm-toolchain-xenial main" | sudo tee /etc/apt/sources.list.d/llvm.list
    echo "deb http://llvm.org/apt/xenial/ llvm-toolchain-xenial-3.9 main" | sudo tee -a /etc/apt/sources.list.d/llvm.list
    wget -O - http://llvm.org/apt/llvm-snapshot.gpg.key | sudo apt-key add -
    sudo apt-get update

Install the lldb packages:

    sudo apt-get install lldb-3.9 python-lldb-3.9

To launch lldb:

    lldb-3.9

#### Ubuntu 17.10 ####

Add the additional package sources:

    sudo apt-get update
    sudo apt-get install wget
    echo "deb http://llvm.org/apt/artful/ llvm-toolchain-artful main" | sudo tee /etc/apt/sources.list.d/llvm.list
    echo "deb http://llvm.org/apt/artful/ llvm-toolchain-artful-3.9 main" | sudo tee -a /etc/apt/sources.list.d/llvm.list
    wget -O - http://llvm.org/apt/llvm-snapshot.gpg.key | sudo apt-key add -
    sudo apt-get update

Install the lldb packages:

    sudo apt-get install lldb-3.9 python-lldb-3.9

To launch lldb:

    lldb-3.9

#### Ubuntu 18.04 ####

To install the lldb packages:

    sudo apt-get update
    sudo apt-get install lldb-3.9 llvm-3.9 python-lldb-3.9

To launch lldb:

    lldb-3.9

10.0 is the only version of lldb found that works with SOS for arm32 on Ubuntu 18.04.

#### Ubuntu 20.04 ####

To install the lldb packages:

    sudo get-get update
    sudo apt-get install lldb

This installs lldb version 10.0.0.

#### Alpine 3.9 to 3.12 ####

lldb 10.0 is available and works for these Apline versions.

#### CentOS 6 ####

[TBD]

#### CentOS 7 ####

lldb 3.9 will have to be built for this distro.

First the prerequisites:

    sudo yum install centos-release-SCL epel-release
    sudo yum install cmake cmake3 gcc gcc-c++ git libicu libunwind make python27 tar wget which zip
    sudo yum install doxygen libedit-devel libxml2-devel python-argparse python-devel readline-devel swig xz

Now build and install llvm/lldb 3.9 using the script provided here: [build-install-lldb.sh](../lldb/centos7/build-install-lldb.sh).

WARNING: This script installs llvm and lldb as root (via sudo) and may overwrite any previously installed versions.

    cd $HOME
    git clone https://github.com/dotnet/diagnostics.git
    $HOME/diagnostics/documentation/lldb/centos7/build-install-lldb.sh

This will take some time to complete. After the build is finished, run these commands to remove the no longer needed packages:

    sudo yum remove doxygen libedit-devel libxml2-devel python-argparse python-devel readline-devel swig xz
    sudo yum clean all

To launch lldb:

    lldb-3.9.1

#### Debian 8.2/8.7 ####

In order to get lldb-5.0 (3.9 doesn't seem to work that well), we need to add additional package sources:

    sudo apt-get update
    sudo apt-get install wget
    echo "deb http://llvm.org/apt/jessie/ llvm-toolchain-jessie main" | sudo tee /etc/apt/sources.list.d/llvm.list
    echo "deb http://llvm.org/apt/jessie/ llvm-toolchain-jessie-5.0 main" | sudo tee -a /etc/apt/sources.list.d/llvm.list
    wget -O - http://llvm.org/apt/llvm-snapshot.gpg.key | sudo apt-key add -
    sudo apt-get update

Install the lldb packages:

    sudo apt-get install lldb-5.0 python-lldb-5.0

To launch lldb:

    lldb-5.0

#### Debian 9 (Stretch) ####

    sudo apt-get install lldb-3.9 python-lldb-3.9

To launch lldb:

    lldb-3.9

#### Fedora 24 ####

    sudo dnf install clang cmake findutils git libicu libunwind make python tar wget which zip
    sudo dnf install doxygen libedit-devel libxml2-devel python-argparse python-devel readline-devel swig xz

Now build and install llvm/lldb 3.9 using the script provided here: [build-install-lldb.sh](../lldb/fedora24/build-install-lldb.sh).

WARNING: This script installs llvm and lldb as root (via sudo) and may overwrite any previously installed versions.

    cd $HOME
    git clone https://github.com/dotnet/diagnostics.git
    $HOME/diagnostics/documentation/lldb/fedora24/build-install-lldb.sh

This will take some time to complete. After the build is finished, run these commands to remove the no longer needed packages:

    sudo dnf remove doxygen libedit-devel libxml2-devel readline-devel swig
    sudo dnf clean all

To launch lldb:

    lldb

#### Fedora 27, 28, 29 ####

    sudo dnf install lldb python2-lldb

To launch lldb:

    lldb

#### OpenSuse 42.1, 42.3 ####

    sudo zypper install cmake gcc-c++ git hostname libicu libunwind lldb-devel llvm-clang llvm-devel make python python-xml tar wget which zip
    sudo zypper install doxygen libedit-devel libxml2-devel ncurses-devel python-argparse python-devel readline-devel swig

Now build and install llvm/lldb 3.9 using the script provided here: [build-install-lldb.sh](../lldb/opensuse/build-install-lldb.sh).

WARNING: This script installs llvm and lldb as root (via sudo) and may overwrite any previously installed versions.

    cd $HOME
    git clone https://github.com/dotnet/diagnostics.git
    $HOME/diagnostics/documentation/lldb/opensuse/build-install-lldb.sh

This will take some time to complete. After the build is finished, run these commands to remove the no longer needed packages:

    sudo zypper rm doxygen libedit-devel libxml2-devel ncurses-devel python-argparse python-devel readline-devel swig
    sudo zypper clean -a

To launch lldb:

    lldb-3.9.1

#### RHEL 7.5 ####

See [LLDB](https://access.redhat.com/documentation/en-us/red_hat_developer_tools/2018.2/html/using_clang_and_llvm_toolset/chap-lldb) on RedHat's web site.

#### SLES ####

[TBD]
