Private runtime build testing
=============================

Here are some instructions on how to run the diagnostics repo's tests against a locally build private .NET Core runtime based on CoreCLR. These directions will work on Windows, Linux and MacOS. 

1. Build the runtime repo (see [Workflow Guide](https://github.com/dotnet/runtime/blob/main/docs/workflow/README.md)).
2. Build the diagnostics repo (see [Building the Repository](../README.md)).
3. Run the diagnostics repo tests with the -privatebuildpath option.

The following examples assume a Debug build of the runtime and diagnsotics. However any flavor can be used with the following steps:

On Windows:
```
C:\diagnostics> test -privatebuildpath c:\runtime\artifacts\bin\coreclr\Windows_NT.x64.Debug
```

When you are all done with the private runtime testing, run this command to remove the Windows registry entries added in the above steps.

```
C:\diagnostics> test -cleanupprivatebuild
```

There will be some popups from regedit asking for administrator permission to edit the registry (press Yes), warning about adding registry keys from AddPrivateTesting.reg (press Yes) and that the edit was successful (press OK).

On Linux/MacOS:
```
~/diagnostics$ ./test.sh --privatebuildpath /home/user/runtime/artifacts/bin/coreclr/Linux.x64.Debug
```
The private runtime will be copied to the diagnostics repo and the tests started. It can be just the runtime binaries but this assumes that the private build is close to the latest published main build. If not, you can pass the runtime's testhost directory containing all the shared runtime bits i.e. `c:\runtime\artifacts\bin\coreclr\testhost\netcoreapp5.0-Windows_NT-Debug-x64\shared\Microsoft.NETCore.App\5.0.0` or `/home/user/runtime/artifacts/bin/coreclr/testhost/netcoreapp5.0-Linux-Release-x64/shared/Microsoft.NETCore.App/5.0.0`

On Linux/MacOS it is recommended to test against Release runtime builds because of a benign assert in DAC (tracked by issue #[31897](https://github.com/dotnet/runtime/issues/31897)) that causes the tests to fail.

On Windows the DAC is not properly signed for a private runtime build so there are a couple of registry keys that need to be added so Windows will load the DAC and the tests can create proper mini-dumps. An example of the registry key values added are:

```
[HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\KnownManagedDebuggingDlls]
"C:\diagnostics\.dotnet\shared\Microsoft.NETCore.App\5.0.0-alpha.1.20102.3\mscordaccore.dll"=dword:0

[HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\MiniDumpAuxiliaryDlls]
"C:\diagnostics\.dotnet\shared\Microsoft.NETCore.App\5.0.0-alpha.1.20102.3\coreclr.dll"="C:\diagnostics\.dotnet\shared\Microsoft.NETCore.App\5.0.0-alpha.1.20102.3\mscordaccore.dll"
```
