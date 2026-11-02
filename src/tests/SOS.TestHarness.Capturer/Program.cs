// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using SOS.TestHarness;

// Dump-bundle producer and out-of-process Windows capturer. Keeps risky in-process dbgeng
// live-debugging work outside the test host. See CaptureCli for usage.
return CaptureCli.Run(args);
