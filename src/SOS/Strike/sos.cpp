// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#include "strike.h"
#include "util.h"
#include "sos.h"
#include "gcdesc.h"

namespace sos
{
    template <class T>
    static bool MemOverlap(T beg1, T end1, // first range
                           T beg2, T end2) // second range
    {
        if (beg1 >= end1 || beg2 >= end2)       // one of the ranges is empty
            return false;
        if (beg2 >= beg1 && beg2 <= end1)       // second range starts within first range
            return true;
        else if (end2 >= beg1 && end2 <= end1)  // second range ends within first range
            return true;
        else if (beg1 >= beg2 && beg1 <= end2)  // first range starts within second range
            return true;
        else if (end1 >= beg2 && end1 <= end2)  // first range ends within second range
            return true;
        else
            return false;
    }


    Object::Object(TADDR addr)
        : mAddress(addr), mMT(0), mSize(~0), mPointers(false), mMTData(0), mTypeName(0)
    {
        if ((mAddress & ~ALIGNCONST) != mAddress)
            sos::Throw<Exception>("Object %p is misaligned.", mAddress);
    }

    Object::Object(TADDR addr, TADDR mt)
        : mAddress(addr), mMT(mt & ~3), mSize(~0), mPointers(false), mMTData(0), mTypeName(0)
    {
        if ((mAddress & ~ALIGNCONST) != mAddress)
            sos::Throw<Exception>("Object %p is misaligned.", mAddress);
    }


    Object::Object(const Object &rhs)
        : mAddress(rhs.mAddress), mMT(rhs.mMT), mSize(rhs.mSize), mPointers(rhs.mPointers), mMTData(rhs.mMTData), mTypeName(rhs.mTypeName)
    {
        rhs.mMTData = 0;
        rhs.mTypeName = 0;
    }

    const Object &Object::operator=(TADDR addr)
    {
        if (mMTData)
            delete mMTData;

        if (mTypeName)
            delete mTypeName;

        mAddress = addr;
        mMT = 0;
        mSize = ~0;
        mMTData = 0;
        mTypeName = 0;

        return *this;
    }

    bool Object::TryGetHeader(ULONG &outHeader) const
    {
        struct ObjectHeader
        {
    #ifdef _WIN64
            ULONG _alignpad;
    #endif
            ULONG SyncBlockValue;      // the Index and the Bits
        };

        ObjectHeader header;

        if (SUCCEEDED(rvCache->Read(TO_TADDR(GetAddress() - sizeof(ObjectHeader)), &header, sizeof(ObjectHeader), NULL)))
        {
            outHeader = header.SyncBlockValue;
            return true;
        }

        return false;
    }


    ULONG Object::GetHeader() const
    {
        ULONG toReturn = 0;
        if (!TryGetHeader(toReturn))
            sos::Throw<DataRead>("Failed to get header for object %p.", GetAddress());

        return toReturn;
    }

    TADDR Object::GetMT() const
    {
        if (mMT == NULL)
        {
            TADDR temp;
            if (FAILED(MOVE(temp, mAddress)))
                sos::Throw<DataRead>("Object %s has an invalid method table.", DMLListNearObj(mAddress));

            if (temp == NULL)
                sos::Throw<HeapCorruption>("Object %s has an invalid method table.", DMLListNearObj(mAddress));

            mMT = temp & ~METHODTABLE_PTR_LOW_BITMASK;
        }

        return mMT;
    }

    TADDR Object::GetComponentMT() const
    {
        if (mMT != NULL && mMT != sos::MethodTable::GetArrayMT())
            return NULL;

        DacpObjectData objData;
        if (FAILED(objData.Request(g_sos, TO_CDADDR(mAddress))))
            sos::Throw<DataRead>("Failed to request object data for %s.", DMLListNearObj(mAddress));

        if (mMT == NULL)
            mMT = TO_TADDR(objData.MethodTable) & ~METHODTABLE_PTR_LOW_BITMASK;

        return TO_TADDR(objData.ElementTypeHandle);
    }

    const WCHAR *Object::GetTypeName() const
    {
        if (mTypeName == NULL)
            mTypeName = CreateMethodTableName(GetMT(), GetComponentMT());


        if (mTypeName == NULL)
            return W("<error>");

        return mTypeName;
    }

    void Object::FillMTData() const
    {
        if (mMTData == NULL)
        {
            mMTData = new DacpMethodTableData;
            if (FAILED(mMTData->Request(g_sos, GetMT())))
            {
                delete mMTData;
                mMTData = NULL;
                sos::Throw<DataRead>("Could not request method table data for object %p (MethodTable: %p).", mAddress, mMT);
            }
        }
    }


    void Object::CalculateSizeAndPointers() const
    {
        TADDR mt = GetMT();
        MethodTableInfo* info = g_special_mtCache.Lookup((DWORD_PTR)mt);
        if (!info->IsInitialized())
        {
            // this is the first time we see this method table, so we need to get the information
            // from the target
            FillMTData();

            info->BaseSize = mMTData->BaseSize;
            info->ComponentSize = mMTData->ComponentSize;
            info->bContainsPointers = mMTData->bContainsPointers;

            // The following request doesn't work on older runtimes. For those, the
            // objects would just look like non-collectible, which is acceptable.
            DacpMethodTableCollectibleData mtcd;
            if (SUCCEEDED(mtcd.Request(g_sos, GetMT())))
            {
                info->bCollectible = mtcd.bCollectible;
                info->LoaderAllocatorObjectHandle = TO_TADDR(mtcd.LoaderAllocatorObjectHandle);
            }
        }

        if (mSize == (size_t)~0)
        {
            mSize = info->BaseSize;
            if (info->ComponentSize)
            {
                // this is an array, so the size has to include the size of the components. We read the number
                // of components from the target and multiply by the component size to get the size.
                mSize += info->ComponentSize * GetNumComponents(GetAddress());
            }

            // On x64 we do an optimization to save 4 bytes in almost every string we create.
        #ifdef _WIN64
            // Pad to min object size if necessary
            if (mSize < min_obj_size)
                mSize = min_obj_size;
        #endif // _WIN64
        }

        mPointers = info->bContainsPointers != FALSE;
    }

    size_t Object::GetSize() const
    {
        if (mSize == (size_t)~0) // poison value
        {
            CalculateSizeAndPointers();
        }

        SOS_Assert(mSize != (size_t)~0);
        return mSize;
    }


    bool Object::HasPointers() const
    {
        if (mSize == (size_t)~0)
            CalculateSizeAndPointers();

        SOS_Assert(mSize != (size_t)~0);
        return mPointers;
    }


    bool Object::VerifyMemberFields(TADDR pMT, TADDR obj)
    {
        WORD numInstanceFields = 0;
        return VerifyMemberFields(pMT, obj, numInstanceFields);
    }


    bool Object::VerifyMemberFields(TADDR pMT, TADDR obj, WORD &numInstanceFields)
    {
        DacpMethodTableData vMethTable;
        if (FAILED(vMethTable.Request(g_sos, pMT)))
            return false;

        // Recursively verify the parent (this updates numInstanceFields)
        if (vMethTable.ParentMethodTable)
        {
            if (!VerifyMemberFields(TO_TADDR(vMethTable.ParentMethodTable), obj, numInstanceFields))
                return false;
        }

        DacpMethodTableFieldData vMethodTableFields;

        // Verify all fields on the object.
        CLRDATA_ADDRESS dwAddr = vMethodTableFields.FirstField;
        DacpFieldDescData vFieldDesc;

        while (numInstanceFields < vMethodTableFields.wNumInstanceFields)
        {
            CheckInterrupt();

            if (FAILED(vFieldDesc.Request(g_sos, dwAddr)))
                return false;

            if (vFieldDesc.Type >= ELEMENT_TYPE_MAX)
                return false;

            dwAddr = vFieldDesc.NextField;

            if (!vFieldDesc.bIsStatic)
            {
                numInstanceFields++;
                TADDR dwTmp = TO_TADDR(obj + vFieldDesc.dwOffset + sizeof(BaseObject));
                if (vFieldDesc.Type == ELEMENT_TYPE_CLASS)
                {
                    // Is it a valid object?
                    if (FAILED(MOVE(dwTmp, dwTmp)))
                        return false;

                    if (dwTmp != NULL)
                    {
                        DacpObjectData objData;
                        if (FAILED(objData.Request(g_sos, TO_CDADDR(dwTmp))))
                            return false;
                    }
                }
            }
        }

        return true;
    }

    bool MethodTable::IsZombie(TADDR addr)
    {
        // Zombie objects are objects that reside in an unloaded AppDomain.
        MethodTable mt = addr;
        return _wcscmp(mt.GetName(), W("<Unloaded Type>")) == 0;
    }

    void MethodTable::Clear()
    {
        if (mName)
        {
            delete [] mName;
            mName = NULL;
        }
    }

    const WCHAR *MethodTable::GetName() const
    {
        if (mName == NULL)
            mName = CreateMethodTableName(mMT);

        if (mName == NULL)
            return W("<error>");

        return mName;
    }

    bool Object::IsValid(TADDR address, bool verifyFields)
    {
        DacpObjectData objectData;
        if (FAILED(objectData.Request(g_sos, TO_CDADDR(address))))
            return false;

        if (verifyFields &&
            objectData.MethodTable != g_special_usefulGlobals.FreeMethodTable &&
            !MethodTable::IsZombie(TO_TADDR(objectData.MethodTable)))
        {
            return VerifyMemberFields(TO_TADDR(objectData.MethodTable), address);
        }

        return true;
    }

    bool Object::GetThinLock(ThinLockInfo &out) const
    {
        ULONG header = GetHeader();
        if (header & (BIT_SBLK_IS_HASH_OR_SYNCBLKINDEX | BIT_SBLK_SPIN_LOCK))
        {
            return false;
        }

        out.ThreadId = header & SBLK_MASK_LOCK_THREADID;
        out.Recursion = (header & SBLK_MASK_LOCK_RECLEVEL) >> SBLK_RECLEVEL_SHIFT;

        CLRDATA_ADDRESS threadPtr = NULL;
        if (g_sos->GetThreadFromThinlockID(out.ThreadId, &threadPtr) != S_OK)
        {
            out.ThreadPtr = NULL;
        }
        else
        {
            out.ThreadPtr = TO_TADDR(threadPtr);
        }

        return out.ThreadId != 0 && out.ThreadPtr != NULL;
    }

    bool Object::GetStringData(__out_ecount(size) WCHAR *buffer, size_t size) const
    {
        SOS_Assert(IsString());
        SOS_Assert(buffer);
        SOS_Assert(size > 0);

        return SUCCEEDED(g_sos->GetObjectStringData(mAddress, (ULONG32)size, buffer, NULL));
    }

    size_t Object::GetStringLength() const
    {
        SOS_Assert(IsString());

        strobjInfo stInfo;
        if (FAILED(MOVE(stInfo, mAddress)))
            sos::Throw<DataRead>("Failed to read object data at %p.", mAddress);

        // We get the method table for free here, if we don't have it already.
        SOS_Assert((mMT == NULL) || (mMT == TO_TADDR(stInfo.methodTable)));
        if (mMT == NULL)
            mMT = TO_TADDR(stInfo.methodTable);

        return (size_t)stInfo.m_StringLength;
    }

    // SyncBlk class
    SyncBlk::SyncBlk()
        : mIndex(0)
    {
    }

    SyncBlk::SyncBlk(int index)
    : mIndex(index)
    {
        Init();
    }

    const SyncBlk &SyncBlk::operator=(int index)
    {
        mIndex = index;
        Init();

        return *this;
    }

    void SyncBlk::Init()
    {
        if (FAILED(mData.Request(g_sos, mIndex)))
            sos::Throw<DataRead>("Failed to request SyncBlk at index %d.", mIndex);
    }

    TADDR SyncBlk::GetAddress() const
    {
        SOS_Assert(mIndex);
        return TO_TADDR(mData.SyncBlockPointer);
    }

    TADDR SyncBlk::GetObject() const
    {
        SOS_Assert(mIndex);
        return TO_TADDR(mData.Object);
    }

    int SyncBlk::GetIndex() const
    {
        return mIndex;
    }

    bool SyncBlk::IsFree() const
    {
        SOS_Assert(mIndex);
        return mData.bFree != FALSE;
    }

    unsigned int SyncBlk::GetMonitorHeldCount() const
    {
        SOS_Assert(mIndex);
        return mData.MonitorHeld;
    }

    unsigned int SyncBlk::GetRecursion() const
    {
        SOS_Assert(mIndex);
        return mData.Recursion;
    }

    DWORD SyncBlk::GetCOMFlags() const
    {
        SOS_Assert(mIndex);
    #ifdef FEATURE_COMINTEROP
        return mData.COMFlags;
    #else
        return 0;
    #endif
    }

    unsigned int SyncBlk::GetAdditionalThreadCount() const
    {
        SOS_Assert(mIndex);
        return mData.AdditionalThreadCount;
    }

    TADDR SyncBlk::GetHoldingThread() const
    {
        SOS_Assert(mIndex);
        return TO_TADDR(mData.HoldingThread);
    }

    TADDR SyncBlk::GetAppDomain() const
    {
        SOS_Assert(mIndex);
        return TO_TADDR(mData.appDomainPtr);
    }

    void BuildTypeWithExtraInfo(TADDR addr, unsigned int size, __inout_ecount(size) WCHAR *buffer)
    {
        try
        {
            sos::Object obj(addr);
            TADDR mtAddr = obj.GetMT();
            bool isArray = sos::MethodTable::IsArrayMT(mtAddr);
            bool isString = obj.IsString();

            sos::MethodTable mt(isArray ? obj.GetComponentMT() : mtAddr);

            if (isArray)
            {
                swprintf_s(buffer, size, W("%s[]"), mt.GetName());
            }
            else if (isString)
            {
                WCHAR str[32];
                obj.GetStringData(str, ARRAY_SIZE(str));

                _snwprintf_s(buffer, size, _TRUNCATE, W("%s: \"%s\""), mt.GetName(), str);
            }
            else
            {
                _snwprintf_s(buffer, size, _TRUNCATE, W("%s"), mt.GetName());
            }
        }
        catch (const sos::Exception &e)
        {
            int len = MultiByteToWideChar(CP_ACP, 0, e.what(), -1, NULL, 0);

            ArrayHolder<WCHAR> tmp = new WCHAR[len];
            MultiByteToWideChar(CP_ACP, 0, e.what(), -1, (WCHAR*)tmp, len);

            swprintf_s(buffer, size, W("<invalid object: '%s'>"), (WCHAR*)tmp);
        }
    }
}
