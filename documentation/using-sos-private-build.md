# Using your own SOS private build

This document is written for people who need to use the bleeding edge SOS freshly built from the enlistment. You may need to do it because you want the latest feature, or you are developing your own SOS command and you want to try it out. Regardless of the reason, here is how you can do it.

Before we can use the private build, of course, we must build it first. [Here](https://github.com/dotnet/diagnostics/tree/main/documentation/building) is the documentation on how to do that.

## Windows

On Windows, [Debugging Tools for Windows](https://docs.microsoft.com/en-us/windows-hardware/drivers/debugger/debugger-download-tools) (aka WinDBG) automatically loads a shipped version of sos.dll from the extension gallery whenever it notices a managed runtime is loaded. In order to avoid that behavior, we need to make sure sos is loaded before it encounters the managed runtime, for the launch scenario, we can do this before running anything.

```
0:000> .load <reporoot>\artifacts\bin\Windows_NT.x64.Debug\sos.dll
```

In the attach scenario, we need to do things differently. We couldn't stop WinDBG from loading the shipped sos, but we can replace it.

```
0:000> .unload sos
0:000> .load <reporoot>\artifacts\bin\Windows_NT.x64.Debug\sos.dll
```

This will ensure you are using your own sos.dll, of course you might have a different full path to `sos.dll`.

## Linux

On Linux, we have to manually load `libsosplugin.so`, so we can simply load it from the expected location ourselves.

```
(lldb) plugin load <reporoot>/artifacts/bin/Linux.x64.Debug/libsosplugin.so
```

Then we are using our own sos and we can do whatever we want with it!
