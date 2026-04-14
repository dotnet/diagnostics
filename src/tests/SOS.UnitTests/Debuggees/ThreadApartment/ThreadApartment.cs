// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;
using System.Threading;

internal sealed class ThreadApartment
{
    private static readonly ManualResetEventSlim s_staReady = new();
    private static readonly ManualResetEventSlim s_mtaReady = new();

    private static void Main()
    {
        // Create an STA thread using SetApartmentState before Start.
        // The runtime will call CoInitializeEx(COINIT_APARTMENTTHREADED) when the thread starts.
        Thread staThread = new Thread(() =>
        {
            s_staReady.Set();
            Thread.Sleep(Timeout.Infinite);
        });
        staThread.SetApartmentState(ApartmentState.STA);
        staThread.IsBackground = true;
        staThread.Start();

        // Create an MTA thread using SetApartmentState before Start.
        Thread mtaThread = new Thread(() =>
        {
            s_mtaReady.Set();
            Thread.Sleep(Timeout.Infinite);
        });
        mtaThread.SetApartmentState(ApartmentState.MTA);
        mtaThread.IsBackground = true;
        mtaThread.Start();

        s_staReady.Wait();
        s_mtaReady.Wait();

        Debugger.Break();

        throw new Exception("ThreadApartment test complete");
    }
}
