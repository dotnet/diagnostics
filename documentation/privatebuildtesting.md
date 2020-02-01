Private runtime build testing
=============================

Here are some instructions on how to run the diagnostics repo's tests against a locally build private .NET Core runtime. These directions will work on Windows, Linux and MacOS. On Windows there are a couple of registry keys that need to be added so the tests can create proper mini-dumps.

1. Build the runtime repo (see [Workflow Guide](https://github.com/dotnet/runtime/blob/master/docs/workflow/README.md)).
2. Build the diagnostics repo (see [Building the Repository](../README.md)).
3. Run the diagnostics repo tests with the -privatebuildpath option.

On Windows:
```
C:\diagnostics> test -privatebuildpath c:\runtime\artifacts\bin\coreclr\Windows_NT.x64.Debug
```

There will be some popups from regedit asking for administrator permission to edit the registry (press Yes), warning about adding registry keys from PrivateTesting.reg (press Yes) and that the edit was successful (press OK).

On Linux/MacOS:
```
~/diagnostics$ ./test.sh --privatebuildpath /home/user/runtime/artifacts/bin/coreclr/Linux.x64.Debug
```
The private runtime will be copied to the diagnostics repo and the tests started. It can be just the coreclr binaries but this assumes that the private build is close to the latest published master build. If not, you can pass the runtime's testhost directory containing all the shared runtime bits i.e. `c:\runtime\artifacts\bin\testhost\netcoreapp5.0-Windows_NT-Debug-x64\shared\Microsoft.NETCore.App\5.0.0`.

The an example of the registry key values added on Windows:

```
[HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\KnownManagedDebuggingDlls]
"C:\diagnostics\.dotnet\shared\Microsoft.NETCore.App\5.0.0-alpha.1.20102.3\mscordaccore.dll"=dword:0

[HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\MiniDumpAuxiliaryDlls]
"C:\diagnostics\.dotnet\shared\Microsoft.NETCore.App\5.0.0-alpha.1.20102.3\coreclr.dll"="C:\diagnostics\.dotnet\shared\Microsoft.NETCore.App\5.0.0-alpha.1.20102.3\mscordaccore.dll"
```