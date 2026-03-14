setlocal
set _REPOROOT_=%~dp0..\..
set _VERSION60_=6.0.0-rc.2.21464.21
call writexml_x86.cmd %_REPOROOT_%\artifacts\tmp\Debug\dumps\ProjectK\3.1.18\netcoreapp3.1\portable\SOS.StackAndOtherTests.Heap.portable.dmp %_REPOROOT_%\artifacts\Debuggees\portable\SymbolTestApp\SymbolTestApp\bin\Debug\netcoreapp3.1\publish
call writexml_x86.cmd %_REPOROOT_%\artifacts\tmp\Debug\dumps\ProjectK\5.0.9\net5.0\portable\SOS.StackAndOtherTests.Heap.portable.dmp %_REPOROOT_%\artifacts\Debuggees\portable\SymbolTestApp\SymbolTestApp\bin\Debug\net5.0\publish
call writexml_x86.cmd %_REPOROOT_%\artifacts\tmp\Debug\dumps\ProjectK\5.0.9\net5.0\SOS.DualRuntimes.Heap.dmp %_REPOROOT_%\artifacts\bin\WebApp3\Debug\net5.0
call writexml_x86.cmd %_REPOROOT_%\artifacts\tmp\Debug\dumps\ProjectK\5.0.9\net5.0\SOS.LineNums.Heap.dmp %_REPOROOT_%\artifacts\bin\LineNums\Debug\net5.0
call writexml_x86.cmd %_REPOROOT_%\artifacts\tmp\Debug\dumps\ProjectK\%_VERSION60_%\net6.0\SOS.DivZero.Heap.dmp %_REPOROOT_%\artifacts\bin\DivZero\Debug\net6.0
call writexml_x86.cmd %_REPOROOT_%\artifacts\tmp\Debug\dumps\ProjectK\%_VERSION60_%\net6.0\SOS.DivZero.Triage.dmp %_REPOROOT_%\artifacts\bin\DivZero\Debug\net6.0
call writexml_x86.cmd %_REPOROOT_%\artifacts\tmp\Debug\dumps\ProjectK\%_VERSION60_%\net6.0\SOS.WebApp3.Heap.dmp %_REPOROOT_%\artifacts\bin\WebApp3\Debug\net6.0
