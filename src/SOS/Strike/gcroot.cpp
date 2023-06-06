// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// ==++==
//

//
// ==--==

/*
 * This file defines the support classes that allow us to operate on the object graph of the process that SOS
 * is analyzing.
 *
 * The GCRoot algorithm is based on three simple principles:
 *      1.  Only consider an object once.  When we inspect an object, read its references and don't ever touch
 *          it again.  This ensures that our upper bound on the amount of time we spend walking the object
 *          graph very quickly reaches resolution.  The objects we've already inspected (and thus won't inspect
 *          again) is tracked by the mConsidered variable.
 *      2.  Be extremely careful about reads from the target process.  We use a linear cache for reading from
 *          object data.  We also cache everything about the method tables we read out of, as well as caching
 *          the GCDesc which is required to walk the object's references.
 *      3.  Use O(1) data structures for anything perf-critical.  Almost all of the data structures we use to
 *          keep track of data have very fast lookups.  For example, to keep track of the objects we've considered
 *          we use a unordered_set.  Similarly to keep track of MethodTable data we use a unordered_map to track the
 *          mt -> mtinfo mapping.
 */

#include "sos.h"
#include "disasm.h"

#ifdef _ASSERTE
#undef _ASSERTE
#endif

#define _ASSERTE(a) {;}

#include "gcdesc.h"

#include "safemath.h"


#undef _ASSERTE

#ifdef _DEBUG
#define _ASSERTE(expr)         \
    do { if (!(expr) ) { ExtErr("_ASSERTE fired:\n\t%s\n", #expr); if (IsDebuggerPresent()) DebugBreak(); } } while (0)
#else
#define _ASSERTE(x)
#endif

inline size_t ALIGN_DOWN( size_t val, size_t alignment )
{
    // alignment must be a power of 2 for this implementation to work (need modulo otherwise)
    _ASSERTE( 0 == (alignment & (alignment - 1)) );
    size_t result = val & ~(alignment - 1);
    return result;
}

inline void* ALIGN_DOWN( void* val, size_t alignment )
{
    return (void*) ALIGN_DOWN( (size_t)val, alignment );
}

LinearReadCache::LinearReadCache(ULONG pageSize)
    : mCurrPageStart(0), mPageSize(pageSize), mCurrPageSize(0), mPage(0)
{
    mPage = new BYTE[pageSize];
    ClearStats();
}

LinearReadCache::~LinearReadCache()
{
    if (mPage)
        delete [] mPage;
}

bool LinearReadCache::MoveToPage(TADDR addr, unsigned int size)
{
    if (size > mPageSize)
        size = mPageSize;

    mCurrPageStart = addr;
    HRESULT hr = g_ExtData->ReadVirtual(mCurrPageStart, mPage, size, &mCurrPageSize);

    if (hr != S_OK)
    {
        mCurrPageStart = 0;
        mCurrPageSize = 0;
        return false;
    }

#ifdef _DEBUG
    mMisses++;
#endif
    return true;
}


///////////////////////////////////////////////////////////////////////////////

UINT FindAllPinnedAndStrong(DWORD_PTR handlearray[], UINT arraySize)
{
    unsigned int fetched = 0;
    SOSHandleData data[64];
    UINT pos = 0;

    // We do not call GetHandleEnumByType here with a list of strong handles since we would be
    // statically setting the list of strong handles, which could change in a future release.
    // Instead we rely on the dac to provide whether a handle is strong or not.
    ToRelease<ISOSHandleEnum> handles;
    HRESULT hr = g_sos->GetHandleEnum(&handles);
    if (FAILED(hr))
    {
        // This should basically never happen unless there's an OOM.
        ExtOut("Failed to enumerate GC handles.  HRESULT=%x.\n", hr);
        return 0;
    }

    do
    {
        hr = handles->Next(ARRAY_SIZE(data), data, &fetched);

        if (FAILED(hr))
        {
            ExtOut("Failed to enumerate GC handles.  HRESULT=%x.\n", hr);
            break;
        }

        for (unsigned int i = 0; i < fetched; ++i)
        {
            if (pos >= arraySize)
            {
                ExtOut("Buffer overflow while enumerating handles.\n");
                return pos;
            }

            if (data[i].StrongReference)
            {
                handlearray[pos++] = (DWORD_PTR)data[i].Handle;
            }
        }
    } while (fetched == ARRAY_SIZE(data));

    return pos;
}
