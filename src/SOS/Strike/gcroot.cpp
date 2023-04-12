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


////////////////////////////////////////////////////////////////////////////////
//
// Some defines for cards taken from gc code
//
#define card_word_width ((size_t)32)

//
// The value of card_size is determined empirically according to the average size of an object
// In the code we also rely on the assumption that one card_table entry (DWORD) covers an entire os page
//
#if defined (_TARGET_WIN64_)
#define card_size ((size_t)(2*DT_GC_PAGE_SIZE/card_word_width))
#else
#define card_size ((size_t)(DT_GC_PAGE_SIZE/card_word_width))
#endif //_TARGET_WIN64_

// so card_size = 128 on x86, 256 on x64

inline
size_t card_word (size_t card)
{
    return card / card_word_width;
}

inline
unsigned card_bit (size_t card)
{
    return (unsigned)(card % card_word_width);
}

inline
size_t card_of ( BYTE* object)
{
    return (size_t)(object) / card_size;
}

BOOL CardIsSet(const GCHeapDetails &heap, TADDR objAddr)
{
    // The card table has to be translated to look at the refcount, etc.
    // g_card_table[card_word(card_of(g_lowest_address))].

    TADDR card_table = TO_TADDR(heap.card_table);
    card_table = card_table + card_word(card_of((BYTE *)heap.lowest_address))*sizeof(DWORD);

    do
    {
        TADDR card_table_lowest_addr;
        TADDR card_table_next;

        if (MOVE(card_table_lowest_addr, ALIGN_DOWN(card_table, 0x1000) + sizeof(PVOID)) != S_OK)
        {
            ExtErr("Error getting card table lowest address\n");
            return FALSE;
        }

        if (MOVE(card_table_next, card_table - sizeof(PVOID)) != S_OK)
        {
            ExtErr("Error getting next card table\n");
            return FALSE;
        }

        size_t card = (objAddr - card_table_lowest_addr) / card_size;
        TADDR card_addr = card_table + (card_word(card) * sizeof(DWORD));
        DWORD value;
        if (MOVE(value, card_addr) != S_OK)
        {
            ExtErr("Error reading card bits - obj %p card %08x card_addr %p card_table %p\n", objAddr, card, card_addr, card_table);
            return FALSE;
        }

        if (value & 1<<card_bit(card))
            return TRUE;

        card_table = card_table_next;
    }
    while(card_table);

    return FALSE;
}

BOOL NeedCard(TADDR parent, TADDR child)
{
    int iChildGen = g_snapshot.GetGeneration(child);

    if (iChildGen == 2)
        return FALSE;

    int iParentGen = g_snapshot.GetGeneration(parent);

    return (iChildGen < iParentGen);
}

////////////////////////////////////////////////////////////////////////////////
//
// Some defines for mark_array taken from gc code
//

#define mark_bit_pitch 8
#define mark_word_width 32
#define mark_word_size (mark_word_width * mark_bit_pitch)
#define heap_segment_flags_swept 16

inline
size_t mark_bit_bit_of(CLRDATA_ADDRESS add)
{
    return  (size_t)((add / mark_bit_pitch) % mark_word_width);
}

inline
size_t mark_word_of(CLRDATA_ADDRESS add)
{
    return (size_t)(add / mark_word_size);
}

inline BOOL mark_array_marked(const GCHeapDetails &heap, CLRDATA_ADDRESS add)
{

    DWORD entry = 0;
    HRESULT hr = MOVE(entry, heap.mark_array + sizeof(DWORD) * mark_word_of(add));

    if (FAILED(hr))
        ExtOut("Failed to read card table entry.\n");

    return entry & (1 << mark_bit_bit_of(add));
}

BOOL background_object_marked(const GCHeapDetails &heap, CLRDATA_ADDRESS o)
{
    BOOL m = TRUE;

    if ((o >= heap.background_saved_lowest_address) && (o < heap.background_saved_highest_address))
        m = mark_array_marked(heap, o);

    return m;
}

BOOL fgc_should_consider_object(const GCHeapDetails &heap,
                                CLRDATA_ADDRESS o,
                                const DacpHeapSegmentData &seg,
                                BOOL consider_bgc_mark_p,
                                BOOL check_current_sweep_p,
                                BOOL check_saved_sweep_p)
{
    // the logic for this function must be kept in sync with the analogous function in gc.cpp
    BOOL no_bgc_mark_p = FALSE;

    if (consider_bgc_mark_p)
    {
        if (check_current_sweep_p && (o < heap.next_sweep_obj))
        {
            no_bgc_mark_p = TRUE;
        }

        if (!no_bgc_mark_p)
        {
            if(check_saved_sweep_p && (o >= heap.saved_sweep_ephemeral_start))
            {
                no_bgc_mark_p = TRUE;
            }

            if (!check_saved_sweep_p)
            {
                CLRDATA_ADDRESS background_allocated = seg.background_allocated;
                if (o >= background_allocated)
                {
                    no_bgc_mark_p = TRUE;
                }
            }
        }
    }
    else
    {
        no_bgc_mark_p = TRUE;
    }

    return no_bgc_mark_p ? TRUE : background_object_marked(heap, o);
}

enum c_gc_state
{
    c_gc_state_marking,
    c_gc_state_planning,
    c_gc_state_free
};

inline BOOL in_range_for_segment(const DacpHeapSegmentData &seg, CLRDATA_ADDRESS addr)
{
    return (addr >= seg.mem) && (addr < seg.reserved);
}

void should_check_bgc_mark(const GCHeapDetails &heap,
                           const DacpHeapSegmentData &seg,
                           BOOL* consider_bgc_mark_p,
                           BOOL* check_current_sweep_p,
                           BOOL* check_saved_sweep_p)
{
    // the logic for this function must be kept in sync with the analogous function in gc.cpp
    *consider_bgc_mark_p = FALSE;
    *check_current_sweep_p = FALSE;
    *check_saved_sweep_p = FALSE;

    if (heap.current_c_gc_state == c_gc_state_planning)
    {
        // We are doing the next_sweep_obj comparison here because we have yet to
        // turn on the swept flag for the segment but in_range_for_segment will return
        // FALSE if the address is the same as reserved.
        if ((seg.flags & heap_segment_flags_swept) || (heap.next_sweep_obj == seg.reserved))
        {
            // this seg was already swept.
        }
        else
        {
            *consider_bgc_mark_p = TRUE;

            if ((heap.saved_sweep_ephemeral_seg != -1) && (seg.segmentAddr == heap.saved_sweep_ephemeral_seg))
            {
                *check_saved_sweep_p = TRUE;
            }

            if (in_range_for_segment(seg, heap.next_sweep_obj))
            {
                *check_current_sweep_p = TRUE;
            }
        }
    }
}

// TODO: FACTOR TOGETHER THE OBJECT MEMBER WALKING CODE FROM
// TODO: VerifyObjectMember(), GetListOfRefs(), HeapTraverser::PrintRefs()
BOOL VerifyObjectMember(const GCHeapDetails &heap, DWORD_PTR objAddr)
{
    BOOL ret = TRUE;
    BOOL bCheckCard = TRUE;
    size_t size = 0;
    {
        DWORD_PTR dwAddrCard = objAddr;
        while (dwAddrCard < objAddr + size)
        {
            if (CardIsSet(heap, dwAddrCard))
            {
                bCheckCard = FALSE;
                break;
            }
            dwAddrCard += card_size;
        }

        if (bCheckCard)
        {
            dwAddrCard = objAddr + size - 2*sizeof(PVOID);
            if (CardIsSet(heap, dwAddrCard))
            {
                bCheckCard = FALSE;
            }
        }
    }

    for (sos::RefIterator itr(TO_TADDR(objAddr)); itr; ++itr)
    {
        TADDR dwAddr1 = (DWORD_PTR)*itr;
        if (dwAddr1)
        {
           TADDR dwChild = dwAddr1;
           // Try something more efficient than IsObject here. Is the methodtable valid?
           size_t s;
           BOOL bPointers;
           TADDR dwAddrMethTable;
           if (FAILED(GetMTOfObject(dwAddr1, &dwAddrMethTable)) ||
                (GetSizeEfficient(dwAddr1, dwAddrMethTable, FALSE, s, bPointers) == FALSE))
           {
               DMLOut("object %s: bad member %p at %p\n", DMLObject(objAddr), SOS_PTR(dwAddr1), SOS_PTR(itr.GetOffset()));
               ret = FALSE;
           }

           if (IsMTForFreeObj(dwAddrMethTable))
           {
               DMLOut("object %s contains free object %p at %p\n", DMLObject(objAddr),
                      SOS_PTR(dwAddr1), SOS_PTR(objAddr+itr.GetOffset()));
              ret = FALSE;
           }

           // verify card table
           if (bCheckCard && NeedCard(objAddr+itr.GetOffset(), dwAddr1))
           {
               DMLOut("object %s:%s missing card_table entry for %p\n",
                      DMLObject(objAddr), (dwChild == dwAddr1) ? "" : " maybe",
                      SOS_PTR(objAddr+itr.GetOffset()));
               ret = FALSE;
           }
        }
    }

    return ret;
}

// search for can_verify_deep in gc.cpp for examples of how these functions are used.
BOOL VerifyObject(const GCHeapDetails &heap, const DacpHeapSegmentData &seg, DWORD_PTR objAddr, DWORD_PTR MTAddr, size_t objSize,
    BOOL bVerifyMember)
{
    if (IsMTForFreeObj(MTAddr))
    {
        return TRUE;
    }

    if (objSize < min_obj_size)
    {
        DMLOut("object %s: size %d too small\n", DMLObject(objAddr), objSize);
        return FALSE;
    }

    // If we requested to verify the object's members, the GC may be in a state where that's not possible.
    // Here we check to see if the object in question needs to have its members updated.  If so, we turn off
    // verification for the object.
    if (bVerifyMember)
    {
        BOOL consider_bgc_mark = FALSE, check_current_sweep = FALSE, check_saved_sweep = FALSE;
        should_check_bgc_mark(heap, seg, &consider_bgc_mark, &check_current_sweep, &check_saved_sweep);
        bVerifyMember = fgc_should_consider_object(heap, objAddr, seg, consider_bgc_mark, check_current_sweep, check_saved_sweep);
    }

    return bVerifyMember ? VerifyObjectMember(heap, objAddr) : TRUE;
}


BOOL FindSegment(const GCHeapDetails &heap, DacpHeapSegmentData &seg, CLRDATA_ADDRESS addr)
{
    if (heap.has_regions)
    {
        CLRDATA_ADDRESS dwAddrSeg;
        for (UINT n = 0; n <= GetMaxGeneration(); n++)
        {
            dwAddrSeg = (DWORD_PTR)heap.generation_table[n].start_segment;
            while (dwAddrSeg != 0)
            {
                if (IsInterrupt())
                    return FALSE;
                if (seg.Request(g_sos, dwAddrSeg, heap.original_heap_details) != S_OK)
                {
                    ExtOut("Error requesting heap segment %p\n", SOS_PTR(dwAddrSeg));
                    return FALSE;
                }
                if (addr >= TO_TADDR(seg.mem) && addr < seg.highAllocMark)
                {
                    return TRUE;
                }
                dwAddrSeg = (DWORD_PTR)seg.next;
            }
        }
        return FALSE;
    }
    else
    {
        CLRDATA_ADDRESS dwAddrSeg = heap.generation_table[GetMaxGeneration()].start_segment;

        // Request the initial segment.
        if (seg.Request(g_sos, dwAddrSeg, heap.original_heap_details) != S_OK)
        {
            ExtOut("Error requesting heap segment %p.\n", SOS_PTR(dwAddrSeg));
            return FALSE;
        }

        // Loop while the object is not in range of the segment.
        while (addr < TO_TADDR(seg.mem) ||
            addr >= (dwAddrSeg == heap.ephemeral_heap_segment ? heap.alloc_allocated : TO_TADDR(seg.allocated)))
        {
            // get the next segment
            dwAddrSeg = seg.next;

            // We reached the last segment without finding the object.
            if (dwAddrSeg == NULL)
                return FALSE;

            if (seg.Request(g_sos, dwAddrSeg, heap.original_heap_details) != S_OK)
            {
                ExtOut("Error requesting heap segment %p.\n", SOS_PTR(dwAddrSeg));
                return FALSE;
            }
        }
    }

    return TRUE;
}

BOOL VerifyObject(const GCHeapDetails &heap, DWORD_PTR objAddr, DWORD_PTR MTAddr, size_t objSize, BOOL bVerifyMember)
{
    // This is only used by the other VerifyObject function if bVerifyMember is true,
    // so we only initialize it if we need it for verifying object members.
    DacpHeapSegmentData seg;

    if (bVerifyMember)
    {
        // if we fail to find the segment, we cannot verify the object's members
        bVerifyMember = FindSegment(heap, seg, objAddr);
    }

    return VerifyObject(heap, seg, objAddr, MTAddr, objSize, bVerifyMember);
}

void sos::ObjectIterator::BuildError(char *out, size_t count, const char *format, ...) const
{
    if (out == NULL || count == 0)
        return;

    va_list args;
    va_start(args, format);

    int written = vsprintf_s(out, count, format, args);
    if (written > 0 && mLastObj)
        sprintf_s(out+written, count-written, "\nLast good object: %p.\n", (int*)mLastObj);

    va_end(args);
}

bool sos::ObjectIterator::VerifyObjectMembers(char *reason, size_t count) const
{
    if (!mCurrObj.HasPointers())
        return true;

    size_t size = mCurrObj.GetSize();
    size_t objAddr = (size_t)mCurrObj.GetAddress();
    TADDR mt = mCurrObj.GetMT();

    INT_PTR nEntries;
    MOVE(nEntries, mt-sizeof(PVOID));
    if (nEntries < 0)
        nEntries = -nEntries;

    size_t nSlots = 1 + nEntries * sizeof(CGCDescSeries)/sizeof(DWORD_PTR);
    ArrayHolder<DWORD_PTR> buffer = new DWORD_PTR[nSlots];

    if (FAILED(g_ExtData->ReadVirtual(TO_CDADDR(mt - nSlots*sizeof(DWORD_PTR)),
                                      buffer, (ULONG) (nSlots*sizeof(DWORD_PTR)), NULL)))
    {
        BuildError(reason, count, "Object %s has a bad GCDesc.", DMLObject(objAddr));
        return false;
    }

    CGCDesc *map = (CGCDesc *)(buffer+nSlots);
    CGCDescSeries* cur = map->GetHighestSeries();
    CGCDescSeries* last = map->GetLowestSeries();

    const size_t bufferSize = sizeof(size_t)*128;
    size_t objBuffer[bufferSize/sizeof(size_t)];
    size_t dwBeginAddr = (size_t)objAddr;
    size_t bytesInBuffer = bufferSize;
    if (size < bytesInBuffer)
        bytesInBuffer = size;


    if (FAILED(g_ExtData->ReadVirtual(TO_CDADDR(dwBeginAddr), objBuffer, (ULONG) bytesInBuffer,NULL)))
    {
        BuildError(reason, count, "Object %s: Failed to read members.", DMLObject(objAddr));
        return false;
    }

    BOOL bCheckCard = TRUE;
    {
        DWORD_PTR dwAddrCard = (DWORD_PTR)objAddr;
        while (dwAddrCard < objAddr + size)
        {
            if (CardIsSet (mHeaps[mCurrHeap], dwAddrCard))
            {
                bCheckCard = FALSE;
                break;
            }
            dwAddrCard += card_size;
        }
        if (bCheckCard)
        {
            dwAddrCard = objAddr + size - 2*sizeof(PVOID);
            if (CardIsSet (mHeaps[mCurrHeap], dwAddrCard))
            {
                bCheckCard = FALSE;
            }
        }
    }

    if (cur >= last)
    {
        do
        {
            BYTE** parm = (BYTE**)((objAddr) + cur->GetSeriesOffset());
            BYTE** ppstop =
                (BYTE**)((BYTE*)parm + cur->GetSeriesSize() + (size));
            while (parm < ppstop)
            {
                CheckInterrupt();
                size_t dwAddr1;

                // Do we run out of cache?
                if ((size_t)parm >= dwBeginAddr+bytesInBuffer)
                {
                    // dwBeginAddr += bytesInBuffer;
                    dwBeginAddr = (size_t)parm;
                    if (dwBeginAddr >= objAddr + size)
                    {
                        return true;
                    }
                    bytesInBuffer = bufferSize;
                    if (objAddr+size-dwBeginAddr < bytesInBuffer)
                    {
                        bytesInBuffer = objAddr+size-dwBeginAddr;
                    }
                    if (FAILED(g_ExtData->ReadVirtual(TO_CDADDR(dwBeginAddr), objBuffer, (ULONG) bytesInBuffer, NULL)))
                    {
                       BuildError(reason, count, "Object %s: Failed to read members.", DMLObject(objAddr));
                       return false;
                    }
                }
                dwAddr1 = objBuffer[((size_t)parm-dwBeginAddr)/sizeof(size_t)];
                if (dwAddr1) {
                    DWORD_PTR dwChild = dwAddr1;
                    // Try something more efficient than IsObject here. Is the methodtable valid?
                    size_t s;
                    BOOL bPointers;
                    DWORD_PTR dwAddrMethTable;
                    if (FAILED(GetMTOfObject(dwAddr1, &dwAddrMethTable)) ||
                         (GetSizeEfficient(dwAddr1, dwAddrMethTable, FALSE, s, bPointers) == FALSE))
                    {
                        BuildError(reason, count, "object %s: bad member %p at %p", DMLObject(objAddr),
                               SOS_PTR(dwAddr1), SOS_PTR(objAddr+(size_t)parm-objAddr));

                        return false;
                    }

                    if (IsMTForFreeObj(dwAddrMethTable))
                    {
                        sos::Throw<HeapCorruption>("object %s contains free object %p at %p", DMLObject(objAddr),
                               SOS_PTR(dwAddr1), SOS_PTR(objAddr+(size_t)parm-objAddr));
                    }

                    // verify card table
                    if (bCheckCard &&
                        NeedCard(objAddr+(size_t)parm-objAddr,dwChild))
                    {
                        BuildError(reason, count, "Object %s: %s missing card_table entry for %p",
                                DMLObject(objAddr), (dwChild == dwAddr1)? "" : " maybe",
                                SOS_PTR(objAddr+(size_t)parm-objAddr));

                        return false;
                    }
                }
                parm++;
            }
            cur--;
            CheckInterrupt();

        } while (cur >= last);
    }
    else
    {
        int cnt = (int) map->GetNumSeries();
        BYTE** parm = (BYTE**)((objAddr) + cur->startoffset);
        while ((BYTE*)parm < (BYTE*)((objAddr)+(size)-plug_skew))
        {
            for (int __i = 0; __i > cnt; __i--)
            {
                CheckInterrupt();

                unsigned skip =  cur->val_serie[__i].skip;
                unsigned nptrs = cur->val_serie[__i].nptrs;
                BYTE** ppstop = parm + nptrs;
                do
                {
                    size_t dwAddr1;
                    // Do we run out of cache?
                    if ((size_t)parm >= dwBeginAddr+bytesInBuffer)
                    {
                        // dwBeginAddr += bytesInBuffer;
                        dwBeginAddr = (size_t)parm;
                        if (dwBeginAddr >= objAddr + size)
                            return true;

                        bytesInBuffer = bufferSize;
                        if (objAddr+size-dwBeginAddr < bytesInBuffer)
                            bytesInBuffer = objAddr+size-dwBeginAddr;

                        if (FAILED(g_ExtData->ReadVirtual(TO_CDADDR(dwBeginAddr), objBuffer, (ULONG) bytesInBuffer, NULL)))
                        {
                            BuildError(reason, count, "Object %s: Failed to read members.", DMLObject(objAddr));
                            return false;
                        }
                    }
                    dwAddr1 = objBuffer[((size_t)parm-dwBeginAddr)/sizeof(size_t)];
                    {
                         if (dwAddr1)
                         {
                             DWORD_PTR dwChild = dwAddr1;
                             // Try something more efficient than IsObject here. Is the methodtable valid?
                             size_t s;
                             BOOL bPointers;
                             DWORD_PTR dwAddrMethTable;
                             if (FAILED(GetMTOfObject(dwAddr1, &dwAddrMethTable)) ||
                                  (GetSizeEfficient(dwAddr1, dwAddrMethTable, FALSE, s, bPointers) == FALSE))
                             {
                                 BuildError(reason, count, "Object %s: Bad member %p at %p.\n", DMLObject(objAddr),
                                         SOS_PTR(dwAddr1), SOS_PTR(objAddr+(size_t)parm-objAddr));

                                 return false;
                             }

                             if (IsMTForFreeObj(dwAddrMethTable))
                             {
                                 BuildError(reason, count, "Object %s contains free object %p at %p.", DMLObject(objAddr),
                                        SOS_PTR(dwAddr1), SOS_PTR(objAddr+(size_t)parm-objAddr));
                                 return false;
                             }

                             // verify card table
                             if (bCheckCard &&
                                 NeedCard (objAddr+(size_t)parm-objAddr,dwAddr1))
                             {
                                 BuildError(reason, count, "Object %s:%s missing card_table entry for %p",
                                        DMLObject(objAddr), (dwChild == dwAddr1) ? "" : " maybe",
                                        SOS_PTR(objAddr+(size_t)parm-objAddr));

                                 return false;
                             }
                         }
                    }
                   parm++;
                   CheckInterrupt();
                } while (parm < ppstop);
                parm = (BYTE**)((BYTE*)parm + skip);
            }
        }
    }

    return true;
}

bool sos::ObjectIterator::Verify(char *reason, size_t count) const
{
    try
    {
        TADDR mt = mCurrObj.GetMT();

        if (MethodTable::GetFreeMT() == mt)
        {
            return true;
        }

        size_t size = mCurrObj.GetSize();
        if (size < min_obj_size)
        {
            BuildError(reason, count, "Object %s: Size %d is too small.", DMLObject(mCurrObj.GetAddress()), size);
            return false;
        }

        if (mCurrObj.GetAddress() + mCurrObj.GetSize() > mSegmentEnd)
        {
            BuildError(reason, count, "Object %s is too large.  End of segment at %p.", DMLObject(mCurrObj), mSegmentEnd);
            return false;
        }

        BOOL bVerifyMember = TRUE;

        // If we requested to verify the object's members, the GC may be in a state where that's not possible.
        // Here we check to see if the object in question needs to have its members updated.  If so, we turn off
        // verification for the object.
        BOOL consider_bgc_mark = FALSE, check_current_sweep = FALSE, check_saved_sweep = FALSE;
        should_check_bgc_mark(mHeaps[mCurrHeap], mSegment, &consider_bgc_mark, &check_current_sweep, &check_saved_sweep);
        bVerifyMember = fgc_should_consider_object(mHeaps[mCurrHeap], mCurrObj.GetAddress(), mSegment,
                                                   consider_bgc_mark, check_current_sweep, check_saved_sweep);

        if (bVerifyMember)
            return VerifyObjectMembers(reason, count);
    }
    catch(const sos::Exception &e)
    {
        BuildError(reason, count, e.GetMesssage());
        return false;
    }

    return true;
}

bool sos::ObjectIterator::Verify() const
{
    char *c = NULL;
    return Verify(c, 0);
}
