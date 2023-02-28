// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Xunit.Abstractions;

namespace Microsoft.Diagnostics.TestHelpers
{
    /// <summary>
    /// An incremental atomic unit of work in the process of running a test. A test
    /// can consist of multiple processes running across different machines at
    /// different times. The TestStep supports:
    /// 1) coordination between test processes to ensure each step runs only once
    /// 2) disk based persistence so that later steps in different processes can
    ///    reload the state of earlier steps
    /// 3) Pretty printing logs
    /// 4) TODO: Dependency analysis to determine if the cached output of a previous step
    ///    execution is still valid
    /// </summary>
    public class TestStep
    {
        string _logFilePath;
        string _stateFilePath;
        TimeSpan _timeout;

        public TestStep(string logFilePath, string friendlyName)
        {
            _logFilePath = logFilePath;
            _stateFilePath = Path.ChangeExtension(_logFilePath, "state.txt");
            _timeout = TimeSpan.FromMinutes(20);
            FriendlyName = friendlyName;
        }

        public string FriendlyName { get; private set; }

        async public Task Execute(ITestOutputHelper output)
        {
            // if this step is in progress on another thread, wait for it
            TestStepState stepState = await AcquireStepStateLock(output);

            //if this thread wins the race we do the work on this thread, otherwise
            //we log the winner's saved output
            if (stepState.RunState != TestStepRunState.InProgress)
            {
                LogHeader(stepState, true, output);
                LogPreviousResults(stepState, output);
                LogFooter(stepState, output);
                ThrowExceptionIfFaulted(stepState);
            }
            else
            {
                await UncachedExecute(stepState, output);
            }
        }

        protected virtual Task DoWork(ITestOutputHelper output)
        {
            output.WriteLine("Overload the default DoWork implementation in order to run useful work");
            return Task.Delay(0);
        }

        private async Task UncachedExecute(TestStepState stepState, ITestOutputHelper output)
        {
            using (FileTestOutputHelper stepLog = new FileTestOutputHelper(_logFilePath))
            {
                try
                {
                    LogHeader(stepState, false, output);
                    MultiplexTestOutputHelper mux = new MultiplexTestOutputHelper(new IndentedTestOutputHelper(output), stepLog);
                    await DoWork(mux);
                    stepState = stepState.Complete();
                }
                catch (Exception e)
                {
                    stepState = stepState.Fault(e.Message, e.StackTrace);
                }
                finally
                {
                    LogFooter(stepState, output);
                    await WriteFinalStepState(stepState, output);
                    ThrowExceptionIfFaulted(stepState);
                }
            }
        }

        private bool TryWriteInitialStepState(TestStepState state, ITestOutputHelper output)
        {
            // To ensure the file is atomically updated we write the contents to a temporary
            // file, then move it to the final location
            try
            {
                string tempPath = Path.GetTempFileName();
                try
                {
                    File.WriteAllText(tempPath, state.SerializeInitialState());
                    Directory.CreateDirectory(Path.GetDirectoryName(_stateFilePath));
                    File.Move(tempPath, _stateFilePath);
                    return true;
                }
                finally
                {
                    File.Delete(tempPath);
                }
                
            }
            catch (IOException ex)
            {
                output.WriteLine("Exception writing state file {0} {1}", _stateFilePath, ex.ToString());
                return false;
            }
        }

        private bool TryOpenExistingStepStateFile(out TestStepState stepState, ITestOutputHelper output)
        {
            stepState = null;
            try
            {
                if (!Directory.Exists(Path.GetDirectoryName(_stateFilePath)))
                {
                    return false;
                }
                bool result = TestStepState.TryParse(File.ReadAllText(_stateFilePath), out stepState);
                if (!result)
                {
                    output.WriteLine("TryParse failed on opening existing state file {0}", _stateFilePath);
                }
                return result;
            }
            catch (IOException ex)
            {
                output.WriteLine("Exception opening existing state file {0} {1}", _stateFilePath, ex.ToString());
                return false;
            }
        }

        async private Task WriteFinalStepState(TestStepState stepState, ITestOutputHelper output)
        {
            const int NumberOfRetries = 5;
            FileStream stepStateStream = null;

            // Retry few times because the state file may be open temporarily by another thread or process.
            for (int retries = 0; retries < NumberOfRetries; retries++)
            {
                try
                {
                    stepStateStream = File.Open(_stateFilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
                    break;
                }
                catch (IOException ex)
                {
                    output.WriteLine("WriteFinalStepState exception {0} retry #{1}", ex.ToString(), retries);
                    if (retries >= (NumberOfRetries - 1))
                    {
                        throw;
                    }
                }
            }

            using (stepStateStream)
            {
                stepStateStream.Seek(0, SeekOrigin.End);
                StreamWriter writer = new StreamWriter(stepStateStream);
                await writer.WriteAsync(Environment.NewLine + stepState.SerializeFinalState());
                await writer.FlushAsync();
            }
        }

        private void LogHeader(TestStepState stepState, bool cached, ITestOutputHelper output)
        {
            string cachedText = cached ? " (CACHED)" : "";
            output.WriteLine("[" + stepState.StartTime + "] " + FriendlyName + cachedText);
            output.WriteLine("Process: " + stepState.ProcessName + "(ID: 0x" + stepState.ProcessID.ToString("x") + ") on " + stepState.Machine);
            output.WriteLine("{");
        }

        private void LogFooter(TestStepState stepState, ITestOutputHelper output)
        {
            output.WriteLine("}");
            string elapsedTime = null;
            if (stepState.RunState == TestStepRunState.InProgress)
            {
                output.WriteLine(FriendlyName + " Not Complete");
                output.WriteLine(stepState.ErrorMessage);
            }
            else
            {
                elapsedTime = (stepState.CompleteTime.Value - stepState.StartTime).ToString("mm\\:ss\\.fff");
            }
            if (stepState.RunState == TestStepRunState.Complete)
            {
                output.WriteLine(FriendlyName + " Complete (" + elapsedTime + " elapsed)");
            }
            else if (stepState.RunState == TestStepRunState.Faulted)
            {
                output.WriteLine(FriendlyName + " Faulted (" + elapsedTime + " elapsed)");
                output.WriteLine(stepState.ErrorMessage);
                output.WriteLine(stepState.ErrorStackTrace);
            }
            output.WriteLine("");
            output.WriteLine("");
        }

        private async Task<TestStepState> AcquireStepStateLock(ITestOutputHelper output)
        {
            TestStepState initialStepState = new TestStepState();
            
            bool stepStateFileExists = false;
            while (true)
            {
                TestStepState openedStepState = null;
                stepStateFileExists = File.Exists(_stateFilePath);
                if (!stepStateFileExists && TryWriteInitialStepState(initialStepState, output))
                {
                    // this thread gets to do the work, persist the initial lock state
                    return initialStepState;
                }

                if (stepStateFileExists && TryOpenExistingStepStateFile(out openedStepState, output))
                {
                    if (!ShouldReuseCachedStepState(openedStepState))
                    {
                        try
                        {
                            File.Delete(_stateFilePath);
                            continue;
                        }
                        catch (IOException ex)
                        {
                            output.WriteLine("Exception deleting state file {0} {1}", _stateFilePath, ex.ToString());
                        }
                    }
                    else if (openedStepState.RunState != TestStepRunState.InProgress)
                    {
                        // we can reuse the work and it is finished - stop waiting and return it
                        return openedStepState;
                    }
                }

                // If we get here we are either:
                // a) Waiting for some other thread (potentially in another process) to complete the work
                // b) Waiting for a hopefully transient IO issue to resolve so that we can determine whether or not the work has already been claimed
                //
                // If we wait for too long in either case we will eventually timeout.
                ThrowExceptionForIncompleteWorkIfNeeded(initialStepState, openedStepState, stepStateFileExists, output);
                await Task.Delay(TimeSpan.FromSeconds(1));
            }
        }

        private void ThrowExceptionForIncompleteWorkIfNeeded(TestStepState initialStepState, TestStepState openedStepState, bool stepStateFileExists, ITestOutputHelper output)
        {
            bool timeout = (DateTimeOffset.Now - initialStepState.StartTime > _timeout);
            bool notFinishable = openedStepState != null &&
                                 ShouldReuseCachedStepState(openedStepState) &&
                                 openedStepState.RunState == TestStepRunState.InProgress &&
                                 !IsOpenedStateChangeable(openedStepState);
            if (timeout || notFinishable)
            {
                TestStepState currentState = openedStepState != null ? openedStepState : initialStepState;
                LogHeader(currentState, true, output);
                StringBuilder errorMessage = new StringBuilder();
                if (timeout)
                {
                    errorMessage.Append("Timeout after " + _timeout + ". ");
                }
                if (!stepStateFileExists)
                {
                    errorMessage.Append("Unable to create file:" + Environment.NewLine +
                        _stateFilePath);
                }
                else if (openedStepState == null)
                {
                    errorMessage.AppendLine("Unable to parse file:" + Environment.NewLine +
                        _stateFilePath);
                }
                else
                {
                    // these error cases should have a valid previous log we can restore
                    Debug.Assert(currentState == openedStepState);
                    LogPreviousResults(currentState, output);

                    errorMessage.AppendLine("This step was not marked complete in: " + Environment.NewLine +
                                            _stateFilePath);

                    if (!IsPreviousMachineSame(openedStepState))
                    {
                        errorMessage.AppendLine("The current machine (" + Environment.MachineName + ") differs from the one which ran the step originally (" + currentState.Machine + ")." + Environment.NewLine +
                                                "Perhaps the original process (ID: 0x" + currentState.ProcessID.ToString("x") + ") executing the work exited unexpectedly or the file was" + Environment.NewLine +
                                                "copied to this machine before the work was complete?");
                    }
                    else if (IsPreviousMachineSame(openedStepState) && !IsPreviousProcessRunning(openedStepState))
                    {
                        errorMessage.AppendLine("As of " + DateTimeOffset.Now + " the process executing this step (ID: 0x" + currentState.ProcessID.ToString("x") + ")" + Environment.NewLine +
                                                "is no longer running. Perhaps it was killed or exited unexpectedly?");
                    }
                    else if (openedStepState.ProcessID != Environment.ProcessId)
                    {
                        errorMessage.AppendLine("As of " + DateTimeOffset.Now + " the process executing this step (ID: 0x" + currentState.ProcessID.ToString("x") + ")" + Environment.NewLine +
                                                "is still running. The process may have stopped responding or is running more slowly than expected?");
                    }
                    else
                    {
                        errorMessage.AppendLine("As of " + DateTimeOffset.Now + " this step should still be running on some other thread in this process (ID: 0x" + currentState.ProcessID.ToString("x") + ")" + Environment.NewLine +
                                                "Perhaps the work has deadlocked or is running more slowly than expected?");
                    }

                    string reuseMessage = GetReuseStepStateReason(openedStepState);
                    if (reuseMessage == null)
                    {
                        reuseMessage = "Deleting the file to retry the test step was attempted automatically, but failed.";
                    }
                    else
                    {
                        reuseMessage = "Deleting the file to retry the test step was not attempted automatically because " + reuseMessage + ".";
                    }
                    errorMessage.Append(reuseMessage);
                }
                currentState = currentState.Incomplete(errorMessage.ToString());
                LogFooter(currentState, output);
                if (timeout)
                {
                    throw new TestStepException("Timeout waiting for " + FriendlyName + " step to complete." + Environment.NewLine + errorMessage.ToString());
                }
                else
                {
                    throw new TestStepException(FriendlyName + " step can not be completed." + Environment.NewLine + errorMessage.ToString());
                }
            }
        }

        private static bool ShouldReuseCachedStepState(TestStepState openedStepState)
        {
            return (GetReuseStepStateReason(openedStepState) != null);
        }

        private static string GetReuseStepStateReason(TestStepState openedStepState)
        {
            //This heuristic may need to change, in some cases it is probably too eager to
            //reuse past results when we wanted to retest something. 

            if (openedStepState.RunState == TestStepRunState.Complete)
            {
                return "successful steps are always reused";
            }
            else if(!IsPreviousMachineSame(openedStepState))
            {
                return "steps on run on other machines are always reused, regardless of success";
            }
            else if(IsPreviousProcessRunning(openedStepState))
            {
                return "steps run in currently executing processes are always reused, regardless of success";
            }
            else
            {
                return null;
            }
        }

        private static bool IsPreviousMachineSame(TestStepState openedStepState)
        {
            return Environment.MachineName == openedStepState.Machine;
        }

        private static bool IsPreviousProcessRunning(TestStepState openedStepState)
        {
            Debug.Assert(IsPreviousMachineSame(openedStepState));
            return (Process.GetProcesses().Any(p => p.Id == openedStepState.ProcessID && p.ProcessName == openedStepState.ProcessName));
        }

        private static bool IsOpenedStateChangeable(TestStepState openedStepState)
        {
            return (openedStepState.RunState == TestStepRunState.InProgress && 
                    IsPreviousMachineSame(openedStepState) &&
                    IsPreviousProcessRunning(openedStepState));
        }

        private void LogPreviousResults(TestStepState cachedTaskState, ITestOutputHelper output)
        {
            ITestOutputHelper indentedOutput = new IndentedTestOutputHelper(output);
            try
            {
                string[] lines = File.ReadAllLines(_logFilePath);
                foreach (string line in lines)
                {
                    indentedOutput.WriteLine(line);
                }
            }
            catch (IOException e)
            {
                string errorMessage = "Error accessing task log file: " + _logFilePath + Environment.NewLine +
                                      e.GetType().FullName + ": " + e.Message;
                indentedOutput.WriteLine(errorMessage);
            }
        }

        private void ThrowExceptionIfFaulted(TestStepState cachedStepState)
        {
            if(cachedStepState.RunState == TestStepRunState.Faulted)
            {
                throw new TestStepException(FriendlyName, cachedStepState.ErrorMessage, cachedStepState.ErrorStackTrace);
            }
        }

        enum TestStepRunState
        {
            InProgress,
            Complete,
            Faulted
        }

        class TestStepState
        {
            public TestStepState()
            {
                RunState = TestStepRunState.InProgress;
                Machine = Environment.MachineName;
                ProcessID = Environment.ProcessId;
                ProcessName = Process.GetCurrentProcess().ProcessName;
                StartTime = DateTimeOffset.Now;
            }
            public TestStepState(TestStepRunState runState,
                                 string machine,
                                 int pid,
                                 string processName,
                                 DateTimeOffset startTime,
                                 DateTimeOffset? completeTime,
                                 string errorMessage,
                                 string errorStackTrace)
            {
                RunState = runState;
                Machine = machine;
                ProcessID = pid;
                ProcessName = processName;
                StartTime = startTime;
                CompleteTime = completeTime;
                ErrorMessage = errorMessage;
                ErrorStackTrace = errorStackTrace;
            }
            public TestStepRunState RunState { get; private set; }
            public string Machine { get; private set; }
            public int ProcessID { get; private set; }
            public string ProcessName { get; private set; }
            public string ErrorMessage { get; private set; }
            public string ErrorStackTrace { get; private set; }
            public DateTimeOffset StartTime { get; private set; }
            public DateTimeOffset? CompleteTime { get; private set; }

            public TestStepState Incomplete(string errorMessage)
            {
                return WithFinalState(TestStepRunState.InProgress, null, errorMessage, null);
            }

            public TestStepState Fault(string errorMessage, string errorStackTrace)
            {
                return WithFinalState(TestStepRunState.Faulted, DateTimeOffset.Now, errorMessage, errorStackTrace);
            }

            public TestStepState Complete()
            {
                return WithFinalState(TestStepRunState.Complete, DateTimeOffset.Now, null, null);
            }

            TestStepState WithFinalState(TestStepRunState runState, DateTimeOffset? taskCompleteTime, string errorMessage, string errorStackTrace)
            {
                return new TestStepState(runState, Machine, ProcessID, ProcessName, StartTime, taskCompleteTime, errorMessage, errorStackTrace);
            }

            public string SerializeInitialState()
            {
                XElement initState = new XElement("InitialStepState",
                    new XElement("Machine", Machine),
                    new XElement("ProcessID", "0x" + ProcessID.ToString("x")),
                    new XElement("ProcessName", ProcessName),
                    new XElement("StartTime", StartTime)
                    );
                return initState.ToString();
            }

            public string SerializeFinalState()
            {
                XElement finalState = new XElement("FinalStepState",
                    new XElement("RunState", RunState)
                    );
                if (CompleteTime != null)
                {
                    finalState.Add(new XElement("CompleteTime", CompleteTime.Value));
                }
                if (ErrorMessage != null)
                {
                    finalState.Add(new XElement("ErrorMessage", ErrorMessage));
                }
                if (ErrorStackTrace != null)
                {
                    finalState.Add(new XElement("ErrorStackTrace", ErrorStackTrace));
                }
                return finalState.ToString();
            }

            public static bool TryParse(string text, out TestStepState parsedState)
            {
                parsedState = null;
                try
                {
                    // The XmlReader is not happy with two root nodes so we crudely split them.
                    int indexOfInitialStepStateElementEnd = text.IndexOf("</InitialStepState>");
                    if(indexOfInitialStepStateElementEnd == -1)
                    {
                        return false;
                    }
                    int splitIndex = indexOfInitialStepStateElementEnd + "</InitialStepState>".Length;
                    string initialStepStateText = text.Substring(0, splitIndex);
                    string finalStepStateText = text.Substring(splitIndex);

                    XElement initialStepStateElement = XElement.Parse(initialStepStateText);
                    if (initialStepStateElement == null || initialStepStateElement.Name != "InitialStepState")
                    {
                        return false;
                    }
                    XElement machineElement = initialStepStateElement.Element("Machine");
                    if (machineElement == null || string.IsNullOrWhiteSpace(machineElement.Value))
                    {
                        return false;
                    }
                    string machine = machineElement.Value;
                    XElement processIDElement = initialStepStateElement.Element("ProcessID");
                    int processID;
                    if (processIDElement == null ||
                        !processIDElement.Value.StartsWith("0x"))
                    {
                        return false;
                    }
                    string processIdNumberText = processIDElement.Value.Substring("0x".Length);
                    if (!int.TryParse(processIdNumberText, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out processID))
                    {
                        return false;
                    }
                    string processName = null;
                    XElement processNameElement = initialStepStateElement.Element("ProcessName");
                    if (processNameElement != null)
                    {
                        processName = processNameElement.Value;
                    }
                    DateTimeOffset startTime;
                    XElement startTimeElement = initialStepStateElement.Element("StartTime");
                    if (startTimeElement == null || !DateTimeOffset.TryParse(startTimeElement.Value, out startTime))
                    {
                        return false;
                    }
                    parsedState = new TestStepState(TestStepRunState.InProgress, machine, processID, processName, startTime, null, null, null);
                    TryParseFinalState(finalStepStateText, ref parsedState);
                    return true;
                }
                catch (XmlException)
                {
                    return false;
                }
            }

            private static void TryParseFinalState(string text, ref TestStepState taskState)
            {
                // If there are errors reading the final state portion of the stream we need to treat it
                // as if the stream had terminated at the end of the InitialTaskState node.
                // This covers a small window of time when appending the FinalTaskState node is in progress.
                //
                if(string.IsNullOrWhiteSpace(text))
                {
                    return;
                }
                try
                {
                    XElement finalTaskStateElement = XElement.Parse(text);
                    if (finalTaskStateElement == null || finalTaskStateElement.Name != "FinalStepState")
                    {
                        return;
                    }
                    XElement runStateElement = finalTaskStateElement.Element("RunState");
                    TestStepRunState runState;
                    if (runStateElement == null || !Enum.TryParse<TestStepRunState>(runStateElement.Value, out runState))
                    {
                        return;
                    }
                    DateTimeOffset? completeTime = null;
                    XElement completeTimeElement = finalTaskStateElement.Element("CompleteTime");
                    if (completeTimeElement != null)
                    {
                        DateTimeOffset tempCompleteTime;
                        if (!DateTimeOffset.TryParse(completeTimeElement.Value, out tempCompleteTime))
                        {
                            return;
                        }
                        else
                        {
                            completeTime = tempCompleteTime;
                        }
                    }
                    XElement errorMessageElement = finalTaskStateElement.Element("ErrorMessage");
                    string errorMessage = null;
                    if (errorMessageElement != null)
                    {
                        errorMessage = errorMessageElement.Value;
                    }
                    XElement errorStackTraceElement = finalTaskStateElement.Element("ErrorStackTrace");
                    string errorStackTrace = null;
                    if (errorStackTraceElement != null)
                    {
                        errorStackTrace = errorStackTraceElement.Value;
                    }

                    taskState = taskState.WithFinalState(runState, completeTime, errorMessage, errorStackTrace);
                }
                catch (XmlException) { }
            }
        }
    }

    public class TestStepException : Exception
    {
        public TestStepException(string errorMessage) :
            base(errorMessage)
        { }

        public TestStepException(string stepName, string errorMessage, string stackTrace) :
            base("The " + stepName + " test step failed." + Environment.NewLine +
                 "Original Error: " + errorMessage + Environment.NewLine +
                 stackTrace)
        { }
    }
}
