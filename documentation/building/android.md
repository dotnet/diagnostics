Cross Compilation for Android on Linux
======================================

Through cross compilation, on Linux it is possible to build for arm64 Android.

Requirements
------------

You'll need to generate a toolchain and a sysroot for Android. There's a script which takes care of the required steps.

Generating the rootfs
---------------------

To generate the rootfs, run the following command in the `diagnostics` folder:

```
cross/init-android-rootfs.sh
```

This will download the NDK and any packages required to compile Android on your system. It's over 1 GB of data, so it may take a while.

Cross compiling
---------------
Once the rootfs has been generated, it will be possible to cross compile the repo. 

When cross compiling, you need to set both the `CONFIG_DIR` and `ROOTFS_DIR` variables.

To compile for arm64, run:

```
CONFIG_DIR=`realpath cross/android/arm64` ROOTFS_DIR=`realpath cross/android-rootfs/toolchain/arm64/sysroot` ./build.sh cross arm64 skipgenerateversion skipmscorlib cmakeargs -DENABLE_LLDBPLUGIN=0
```

The resulting binaries will be found in `bin/Product/Linux.BuildArch.BuildType/`

Debugging on Android
--------------------

You can debug on Android using a remote lldb server which you run on your Android device.

First, push the lldb server to Android:

```
adb push cross/android/lldb/2.2/android/arm64-v8a/lldb-server /data/local/tmp/
```

Then, launch the lldb server on the Android device. Open a shell using `adb shell` and run:

```
adb shell
cd /data/local/tmp
./lldb-server platform --listen *:1234
```

After that, you'll need to forward port 1234 from your Android device to your PC:
```
adb forward tcp:1234 tcp:1234
```

Finally, install lldb on your PC and connect to the debug server running on your Android device:

```
lldb-3.9
(lldb) platform select remote-android
  Platform: remote-android
 Connected: no
(lldb) platform connect connect://localhost:1234
  Platform: remote-android
    Triple: aarch64-*-linux-android
OS Version: 23.0.0 (3.10.84-perf-gf38969a)
    Kernel: #1 SMP PREEMPT Fri Sep 16 11:29:29 2016
  Hostname: localhost
 Connected: yes
WorkingDir: /data/local/tmp

(lldb) target create coreclr/pal/tests/palsuite/file_io/CopyFileA/test4/paltest_copyfilea_test4
(lldb) env LD_LIBRARY_PATH=/data/local/tmp/coreclr/lib
(lldb) run
```
