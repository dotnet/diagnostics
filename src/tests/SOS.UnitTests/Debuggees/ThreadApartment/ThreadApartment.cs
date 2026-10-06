// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.CompilerServices;
using System.Threading;

internal sealed class ThreadApartment
{
    private static readonly ManualResetEventSlim s_staReady = new();
    private static readonly ManualResetEventSlim s_mtaReady = new();

    private static void Main()
    {
        // Create an STA thread using SetApartmentState before Start.
        // The runtime will call CoInitializeEx(COINIT_APARTMENTTHREADED) when the thread starts.
        Thread staThread = new Thread(StaWorker);
        staThread.SetApartmentState(ApartmentState.STA);
        staThread.IsBackground = true;
        staThread.Start();

        // Create an MTA thread using SetApartmentState before Start.
        Thread mtaThread = new Thread(MtaWorker);
        mtaThread.SetApartmentState(ApartmentState.MTA);
        mtaThread.IsBackground = true;
        mtaThread.Start();

        s_staReady.Wait();
        s_mtaReady.Wait();

        throw new Exception("ThreadApartment test complete");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void StaWorker()
    {
        s_staReady.Set();
        Thread.Sleep(Timeout.Infinite);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void MtaWorker()
    {
        s_mtaReady.Set();
        Thread.Sleep(Timeout.Infinite);
    }
}
