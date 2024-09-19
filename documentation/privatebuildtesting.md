Private runtime build testing
=============================

Here are some instructions on how to run the diagnostics repo's tests against a locally built private .NET Core runtime based on CoreCLR. These directions will work on Windows, Linux and MacOS. The testing is currently scoped to just the runtime not the libraries and not single-file apps.

1. Build the runtime repo (see [Workflow Guide](https://github.com/dotnet/runtime/blob/main/docs/workflow/README.md)) with `-configuration release -subset clr`. A release build is highly recommended.
2. Build the diagnostics repo (see [Building the Repository](../README.md)).
3. Run the `eng\privatebuild.cmd` or `eng/privatebuild.sh` test runtime install script. This installs and sets up to run (just) the latest test runtimes (currently 9.0) into the `.dotnet-test` directory.
4. On Windows 11 (this doesn't work on Windows 10), add the following DWORD registry key: `HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\MiniDumpSettings\DisableAuxProviderSignatureCheck` and set it to 1. This allows the unsigned privately built DAC to be used to generate dumps.
5. Copy the private runtime binaries over the test SDK/runtimes installed in `.dotnet-test`. This step is hard to automate because there are usually 3 versions of the runtime installed: one as part of the .NET SDK, one as part of the AspNetCore runtime and one from latest runtime DARC update.
6. Run the diagnostics repo tests either:
   a. `test.cmd` or `test.sh` - this runs all the diagnostics tests including SOS's. A html test report will be generated in `artifacts/TestResults/{Debug,Release}/SOS.UnitTests_net6.0_x64.html`.
   b. Use the VS Test Explorer to run all the SOS tests or a specific one.
   c. Use `eng\testsos.cmd` or `eng/testsos.sh` to run just the SOS tests. The html test report isn't generated in this case, but the SOS test logs are in artifacts/TestResults/{Debug,Release}/sos_*.

The following examples assume a release build of the runtime. The runtime version numbers will vary.

#### Windows x64 Example Script

```
copy c:\src\runtime\artifacts\bin\coreclr\windows.x64.Release\sharedFramework\* c:\src\diagnostics\.dotnet-test\shared\Microsoft.NETCore.App\9.0.0-preview.7.24366.18
copy c:\src\runtime\artifacts\bin\coreclr\windows.x64.Release\System.Private.CoreLib.dll c:\src\diagnostics\.dotnet-test\shared\Microsoft.NETCore.App\9.0.0-preview.7.24366.18

copy c:\src\runtime\artifacts\bin\coreclr\windows.x64.Release\sharedFramework\* c:\src\diagnostics\.dotnet-test\shared\Microsoft.NETCore.App\9.0.0-preview.7.24365.2
copy c:\src\runtime\artifacts\bin\coreclr\windows.x64.Release\System.Private.CoreLib.dll c:\src\diagnostics\.dotnet-test\shared\Microsoft.NETCore.App\9.0.0-preview.7.24365.2

copy c:\src\runtime\artifacts\bin\coreclr\windows.x64.Release\sharedFramework\* c:\src\diagnostics\.dotnet-test\shared\Microsoft.NETCore.App\9.0.0-preview.4.24251.3
copy c:\src\runtime\artifacts\bin\coreclr\windows.x64.Release\System.Private.CoreLib.dll c:\src\diagnostics\.dotnet-test\shared\Microsoft.NETCore.App\9.0.0-preview.4.24251.3
```

#### Linux x64 Example Script

```
cp -v $HOME/runtime/artifacts/bin/coreclr/linux.x64.Release/sharedFramework/* $HOME/diagnostics/.dotnet-test/shared/Microsoft.NETCore.App/9.0.0-preview.7.24366.18
cp -v $HOME/runtime/artifacts/bin/coreclr/linux.x64.Release/System.Private.CoreLib.dll $HOME/diagnostics/.dotnet-test/shared/Microsoft.NETCore.App/9.0.0-preview.7.24366.18

cp -v $HOME/runtime/artifacts/bin/coreclr/linux.x64.Release/sharedFramework/* $HOME/diagnostics/.dotnet-test/shared/Microsoft.NETCore.App/9.0.0-preview.7.24365.2
cp -v $HOME/runtime/artifacts/bin/coreclr/linux.x64.Release/System.Private.CoreLib.dll $HOME/diagnostics/.dotnet-test/shared/Microsoft.NETCore.App/9.0.0-preview.7.24365.2

cp -v $HOME/runtime/artifacts/bin/coreclr/linux.x64.Release/sharedFramework/* $HOME/diagnostics/.dotnet-test/shared/Microsoft.NETCore.App/9.0.0-preview.4.24251.3
cp -v $HOME/runtime/artifacts/bin/coreclr/linux.x64.Release/System.Private.CoreLib.dll $HOME/diagnostics/.dotnet-test/shared/Microsoft.NETCore.App/9.0.0-preview.4.24251.3
```

On Linux/MacOS it is recommended to test against Release runtime builds because of a benign assert in DAC (tracked by issue #[31897](https://github.com/dotnet/runtime/issues/31897)) that causes the tests to fail.

#### Note for Windows 10

Because the DAC is not properly signed for a private runtime build there are a couple of registry keys that need to be added so Windows will load the DAC and the tests can create proper mini-dumps. An example of the registry key values added are:

```
[HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\KnownManagedDebuggingDlls]
"C:\diagnostics\.dotnet\shared\Microsoft.NETCore.App\5.0.0-alpha.1.20102.3\mscordaccore.dll"=dword:0

[HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\MiniDumpAuxiliaryDlls]
"C:\diagnostics\.dotnet\shared\Microsoft.NETCore.App\5.0.0-alpha.1.20102.3\coreclr.dll"="C:\diagnostics\.dotnet\shared\Microsoft.NETCore.App\5.0.0-alpha.1.20102.3\mscordaccore.dll"
```
