Testing libsosplugin
=====================================

**Running tests**
  
The test.sh and testsos.sh scripts launches these tests and makes the following a lot easier.


Make sure that python's lldb module is accessible. To run the tests manually, use the following command:
  
`python2 test_libsosplugin.py --lldb <path-to-lldb> --host <path-to-host> --plugin <path-to-sosplugin> --logfiledir <path-to-logdir> --assembly <path-to-testdebuggee>`

- `lldb` is a path to `lldb` to run  
- `host` is a path to .NET Core host like `corerun` or `dotnet`
- `plugin` is the path to the lldb sos plugin
- `logfiledir` is the path to put the log files
- `assembly` is a compiled test assembly (e.g. TestDebuggee.dll)  
- `timeout` is a deadline for a single test (in seconds)  
- `regex` is a regular expression matching tests to run  
- `repeat` is a number of passes for each test

Log files for both failed and passed tests are `*.log` and `*.log.2` for standard output and error correspondingly.


**Writing tests**  
Tests start with the `TestSosCommands` class defined in `test_libsosplugin.py`. To add a test to the suite, start with implementing a new method inside this class whose name begins with `test_`. Most new commands will require only one line of code in this method: `self.do_test("scenarioname")`. This command will launch a new `lldb` instance, which in turn will call the `runScenario` method from `scenarioname` module. `scenarioname` is the name of the python module that will be running the scenario inside `lldb` (found in `tests` folder alongside with `test_libsosplugin.py` and named `scenarioname.py`). 
An example of a scenario looks like this:

	import lldb
	def runScenario(assemblyName, debugger, target):
		process = target.GetProcess()

		# do some work

		process.Continue()
		return True

 `runScenario` method does all the work related to running the scenario: setting breakpoints, running SOS commands and examining their output. It should return a boolean value indicating a success or a failure.  
***Note:*** `testutils.py` defines some useful commands that can be reused in many scenarios.


**Useful links**  
[Python scripting in LLDB](http://lldb.llvm.org/python-reference.html)  
[Python unittest framework](https://docs.python.org/2.7/library/unittest.html)
