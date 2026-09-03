// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using SOS.TestHarness;

// Out-of-process desktop-dump capturer. Runs the risky in-process dbgeng live-debugging work in
// a short-lived child process so it can never crash the test host. See CaptureCli for usage.
return CaptureCli.Run(args);
