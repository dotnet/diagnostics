// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

//=========================================================================

//
// HillClimbing.h
//
// Defines classes for the ThreadPool's HillClimbing concurrency-optimization
// algorithm.
//

//=========================================================================

#ifndef _HILLCLIMBING_H
#define _HILLCLIMBING_H

enum HillClimbingStateTransition 
{
    Warmup,
    Initializing,
    RandomMove,
    ClimbingMove,
    ChangePoint,
    Stabilizing,
    Starvation, //used by ThreadpoolMgr
    ThreadTimedOut, //used by ThreadpoolMgr
    Undefined,
};

#define HillClimbingLogCapacity 200

struct HillClimbingLogEntry
{
    DWORD TickCount;
    HillClimbingStateTransition Transition;
    int NewControlSetting;
    int LastHistoryCount;
    float LastHistoryMean;
};

typedef DPTR(HillClimbingLogEntry) PTR_HillClimbingLogEntry;

#endif
