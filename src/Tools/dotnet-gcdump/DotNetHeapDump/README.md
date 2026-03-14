# DotNetHeapDump

The following code files were copied in their entirety from the Microsoft/Perfview repository on github: https://github.com/Microsoft/PerfView.

This was done simply because refactoring the TraceEvent library to include these classes proved to be too disruptive. Diamond dependency refactoring and
mismatched target frameworks made the refactoring too disruptive to the TraceEvent library.

This code should be treated as read-only.  Any changes that _do_ need to be made should be mirrored to Microsoft/PerfView _and_ documented here.

The intent is to unify these code files with the code in Microsoft/PerfView, either via automated mirroring, or exposing the logic through TraceEvent.

## Files:

* DotNetHeapInfo.cs (https://github.com/microsoft/perfview/blob/76dc28af873e27aa8c4f9ce8efa0971a2c738165/src/HeapDumpCommon/DotNetHeapInfo.cs)
* GCHeapDump.cs (https://github.com/microsoft/perfview/blob/76dc28af873e27aa8c4f9ce8efa0971a2c738165/src/HeapDump/GCHeapDump.cs)
* DotNetHeapDumpGraphReader.cs (https://github.com/microsoft/perfview/blob/76dc28af873e27aa8c4f9ce8efa0971a2c738165/src/EtwHeapDump/DotNetHeapDumpGraphReader.cs)
* MemoryGraph.cs (https://github.com/microsoft/perfview/blob/76dc28af873e27aa8c4f9ce8efa0971a2c738165/src/MemoryGraph/MemoryGraph.cs)
* Graph.cs (https://github.com/microsoft/perfview/blob/76dc28af873e27aa8c4f9ce8efa0971a2c738165/src/MemoryGraph/graph.cs)

## Changes:

There is a baseline commit that contains an exact copy of the code files. All changes in this repo will be separate commits on top of that.

## License from Microsoft/PerfView

The MIT License (MIT)

Copyright (c) .NET Foundation and Contributors

All rights reserved.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
