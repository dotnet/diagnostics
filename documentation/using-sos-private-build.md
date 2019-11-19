# Using your own SOS private build

This document is written for people who need to use the bleeding edge SOS freshly built from the enlistment. You may need to do it because you want the latest feature, or you are developing your own SOS command and you want to try it out. Regardless of the reason, here is how you can do it.

Before we can use the private build, of course, we must build it first. [Here](https://github.com/dotnet/diagnostics/tree/master/documentation/building) is the documentation on how to do that.

## Windows

On Windows, [Debugging Tools for Windows](https://docs.microsoft.com/en-us/windows-hardware/drivers/debugger/debugger-download-tools) (aka WinDBG) automatically loads a shipped version of sos.dll from the extension gallery, and we need to make sure that is undone so that we can load ours.

To do that, we will unload sos as soon as it is loaded and load our own. WinDBG detects and loads the sos as soon as it discover it has a managed runtime in it, therefore we need to stop at the moment when coreclr loads.

```
0:000> sxe ld coreclr
0:000> g
```

It will stop at the point when `coreclr.dll` is loaded, and then we issue these commands to refresh the loaded sos instance.

```
0:000> .unload sos
0:000> .load C:\Dev\diagnostics\artifacts\bin\dotnet-sos\Debug\netcoreapp2.1\publish\win-x64\sos.dll
```

This will ensure you are using your own sos.dll, of course you might have a different full path to `sos.dll`.

## Linux

On Linux, we have to manually load `libsosplugin.so`, so we can simply load it from the expected location ourselves.

```
(lldb) plugin load /home/andrewau/git/diagnostics/artifacts/bin/dotnet-sos/Debug/netcoreapp2.1/publish/linux-x64/libsosplugin.so
```

Then we are using our own sos and we can do whatever we want with it!

