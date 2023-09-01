// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// ==++==
//

//
// ==--==
#include <assert.h>
#include <sstream>
#include "sos.h"
#include "safemath.h"
#include "releaseholder.h"

// This is the increment for the segment lookup data
const int nSegLookupStgIncrement = 100;

#define CCH_STRING_PREFIX_SUMMARY 64

/**********************************************************************\
* Routine Description:                                                 *
*                                                                      *
*    This function is called to update GC heap statistics.             *
*                                                                      *
\**********************************************************************/
void HeapStat::Add(DWORD_PTR aData, DWORD aSize)
{
    if (head == 0)
    {
        head = new Node();
        if (head == NULL)
        {
            ReportOOM();
            ControlC = TRUE;
            return;
        }

        if (bHasStrings)
        {
            size_t capacity_pNew = _wcslen((WCHAR*)aData) + 1;
            WCHAR *pNew = new WCHAR[capacity_pNew];
            if (pNew == NULL)
            {
               ReportOOM();
               ControlC = TRUE;
               return;
            }
            wcscpy_s(pNew, capacity_pNew, (WCHAR*)aData);
            aData = (DWORD_PTR)pNew;
        }

        head->data = aData;
    }
    Node *walk = head;
    int cmp = 0;

    for (;;)
    {
        if (IsInterrupt())
            return;

        cmp = CompareData(aData, walk->data);

        if (cmp == 0)
            break;

        if (cmp < 0)
        {
            if (walk->left == NULL)
                break;
            walk = walk->left;
        }
        else
        {
            if (walk->right == NULL)
                break;
            walk = walk->right;
        }
    }

    if (cmp == 0)
    {
        walk->count ++;
        walk->totalSize += aSize;
    }
    else
    {
        Node *node = new Node();
        if (node == NULL)
        {
            ReportOOM();
            ControlC = TRUE;
            return;
        }

        if (bHasStrings)
        {
            size_t capacity_pNew = _wcslen((WCHAR*)aData) + 1;
            WCHAR *pNew = new WCHAR[capacity_pNew];
            if (pNew == NULL)
            {
               ReportOOM();
               ControlC = TRUE;
               return;
            }
            wcscpy_s(pNew, capacity_pNew, (WCHAR*)aData);
            aData = (DWORD_PTR)pNew;
        }

        node->data = aData;
        node->totalSize = aSize;
        node->count ++;

        if (cmp < 0)
        {
            walk->left = node;
        }
        else
        {
            walk->right = node;
        }
    }
}
/**********************************************************************\
* Routine Description:                                                 *
*                                                                      *
*    This function compares two nodes in the tree.     *
*                                                                      *
\**********************************************************************/
int HeapStat::CompareData(DWORD_PTR d1, DWORD_PTR d2)
{
    if (bHasStrings)
        return _wcscmp((WCHAR*)d1, (WCHAR*)d2);

    if (d1 > d2)
        return 1;

    if (d1 < d2)
        return -1;

    return 0;
}

/**********************************************************************\
* Routine Description:                                                 *
*                                                                      *
*    This function is called to sort all entries in the heap stat.     *
*                                                                      *
\**********************************************************************/
void HeapStat::Sort ()
{
    Node *root = head;
    head = NULL;
    ReverseLeftMost (root);

    Node *sortRoot = NULL;
    while (head)
    {
        Node *tmp = head;
        head = head->left;
        if (tmp->right)
            ReverseLeftMost (tmp->right);
        // add tmp
        tmp->right = NULL;
        tmp->left = NULL;
        SortAdd (sortRoot, tmp);
    }
    head = sortRoot;

    Linearize();

    //reverse the order
    root = head;
    head = NULL;
    sortRoot = NULL;
    while (root)
    {
        Node *tmp = root->right;
        root->left = NULL;
        root->right = NULL;
        LinearAdd (sortRoot, root);
        root = tmp;
    }
    head = sortRoot;
}

void HeapStat::Linearize()
{
    // Change binary tree to a linear tree
    Node *root = head;
    head = NULL;
    ReverseLeftMost (root);
    Node *sortRoot = NULL;
    while (head)
    {
        Node *tmp = head;
        head = head->left;
        if (tmp->right)
            ReverseLeftMost (tmp->right);
        // add tmp
        tmp->right = NULL;
        tmp->left = NULL;
        LinearAdd (sortRoot, tmp);
    }
    head = sortRoot;
    fLinear = TRUE;
}

void HeapStat::ReverseLeftMost (Node *root)
{
    while (root)
    {
        Node *tmp = root->left;
        root->left = head;
        head = root;
        root = tmp;
    }
}

/**********************************************************************\
* Routine Description:                                                 *
*                                                                      *
*    This function is called to help to sort heap stat.                *
*                                                                      *
\**********************************************************************/
void HeapStat::SortAdd (Node *&root, Node *entry)
{
    if (root == NULL)
    {
        root = entry;
    }
    else
    {
        Node *parent = root;
        Node *ptr = root;
        while (ptr)
        {
            parent = ptr;
            if (ptr->totalSize < entry->totalSize)
                ptr = ptr->right;
            else
                ptr = ptr->left;
        }
        if (parent->totalSize < entry->totalSize)
            parent->right = entry;
        else
            parent->left = entry;
    }
}

void HeapStat::LinearAdd(Node *&root, Node *entry)
{
    if (root == NULL)
    {
        root = entry;
    }
    else
    {
        entry->right = root;
        root = entry;
    }
}

/**********************************************************************\
* Routine Description:                                                 *
*                                                                      *
*    This function is called to print GC heap statistics.              *
*                                                                      *
\**********************************************************************/
void HeapStat::Print(const char* label /* = NULL */)
{
    if (label == NULL)
    {
        label = "Statistics:\n";
    }
    ExtOut(label);
    if (bHasStrings)
        ExtOut("%8s %12s %s\n", "Count", "TotalSize", "String Value");
    else
        ExtOut("%" POINTERSIZE "s %8s %12s %s\n","MT", "Count", "TotalSize", "Class Name");

    Node *root = head;
    int ncount = 0;
    while (root)
    {
        if (IsInterrupt())
            return;

        ncount += root->count;

        if (bHasStrings)
        {
            ExtOut("%8d %12I64u \"%S\"\n", root->count, (unsigned __int64)root->totalSize, root->data);
        }
        else
        {
            DMLOut("%s %8d %12I64u ", DMLDumpHeapMT(root->data), root->count, (unsigned __int64)root->totalSize);
            if (IsMTForFreeObj(root->data))
            {
                ExtOut("%9s\n", "Free");
            }
            else
            {
                wcscpy_s(g_mdName, mdNameLen, W("UNKNOWN"));
                NameForMT_s((DWORD_PTR) root->data, g_mdName, mdNameLen);
                ExtOut("%S\n", g_mdName);
            }
        }
        root = root->right;

    }
    ExtOut ("Total %d objects\n", ncount);
}

void HeapStat::Delete()
{
    if (head == NULL)
        return;

    // Ensure the data structure is already linearized.
    if (!fLinear)
        Linearize();

    while (head)
    {
        // The list is linearized on such that the left node is always null.
        Node *tmp = head;
        head = head->right;

        if (bHasStrings)
            delete[] ((WCHAR*)tmp->data);
        delete tmp;
    }

    // return to default state
    bHasStrings = FALSE;
    fLinear = FALSE;
}

// -----------------------------------------------------------------------
//
// MethodTableCache implementation
//
// Used during heap traversals for quick object size computation
//
MethodTableInfo* MethodTableCache::Lookup (DWORD_PTR aData)
{
    Node** addHere = &head;
    if (head != 0) {
        Node *walk = head;
        int cmp = 0;

        for (;;)
        {
            cmp = CompareData(aData, walk->data);

            if (cmp == 0)
                return &walk->info;

            if (cmp < 0)
            {
                if (walk->left == NULL)
                {
                    addHere = &walk->left;
                    break;
                }
                walk = walk->left;
            }
            else
            {
                if (walk->right == NULL)
                {
                    addHere = &walk->right;
                    break;
                }
                walk = walk->right;
            }
        }
    }
    Node* newNode = new Node(aData);
    if (newNode == NULL)
    {
        ReportOOM();
        return NULL;
    }
    *addHere = newNode;
    return &newNode->info;
}

/**********************************************************************\
* Routine Description:                                                 *
*                                                                      *
*    This function compares two nodes in the tree.     *
*                                                                      *
\**********************************************************************/
int MethodTableCache::CompareData(DWORD_PTR d1, DWORD_PTR d2)
{
    if (d1 > d2)
        return 1;

    if (d1 < d2)
        return -1;

    return 0;
}

void MethodTableCache::ReverseLeftMost (Node *root)
{
    if (root)
    {
        if (root->left) ReverseLeftMost(root->left);
        if (root->right) ReverseLeftMost(root->right);
        delete root;
    }
}

void MethodTableCache::Clear()
{
    Node *root = head;
    head = NULL;
    ReverseLeftMost (root);
}

MethodTableCache g_special_mtCache;

size_t Align (size_t nbytes)
{
    return (nbytes + ALIGNCONST) & ~ALIGNCONST;
}

size_t AlignLarge(size_t nbytes)
{
    return (nbytes + ALIGNCONSTLARGE) & ~ALIGNCONSTLARGE;
}

size_t GetNumComponents(TADDR obj)
{
    // The number of components is always the second pointer in the object.
    DWORD Value = NULL;
    HRESULT hr = MOVE(Value, obj + sizeof(size_t));

    // If we fail to read out the number of components, let's assume 0 so we don't try to
    // read further data from the object.
    if (FAILED(hr))
        return 0;

    // The component size on a String does not contain the trailing NULL character,
    // so we must add that ourselves.
    if(IsStringObject(obj))
        return Value+1;

    return Value;
}

static MethodTableInfo* GetMethodTableInfo(DWORD_PTR dwAddrMethTable)
{
    // Remove lower bits in case we are in mark phase
    dwAddrMethTable = dwAddrMethTable & ~sos::Object::METHODTABLE_PTR_LOW_BITMASK;
    MethodTableInfo* info = g_special_mtCache.Lookup(dwAddrMethTable);
    if (!info->IsInitialized())        // An uninitialized entry
    {
        // this is the first time we see this method table, so we need to get the information
        // from the target
        DacpMethodTableData dmtd;
        // see code:ClrDataAccess::RequestMethodTableData for details
        if (dmtd.Request(g_sos, dwAddrMethTable) != S_OK)
            return NULL;


        info->BaseSize = dmtd.BaseSize;
        info->ComponentSize = dmtd.ComponentSize;
        info->bContainsPointers = dmtd.bContainsPointers;

        // The following request doesn't work on older runtimes. For those, the
        // objects would just look like non-collectible, which is acceptable.
        DacpMethodTableCollectibleData dmtcd;
        if (SUCCEEDED(dmtcd.Request(g_sos, dwAddrMethTable)))
        {
            info->bCollectible = dmtcd.bCollectible;
            info->LoaderAllocatorObjectHandle = TO_TADDR(dmtcd.LoaderAllocatorObjectHandle);
        }
    }

    return info;
}

BOOL GetSizeEfficient(DWORD_PTR dwAddrCurrObj,
    DWORD_PTR dwAddrMethTable, BOOL bLarge, size_t& s, BOOL& bContainsPointers)
{
    MethodTableInfo* info = GetMethodTableInfo(dwAddrMethTable);
    if (info == NULL)
    {
        return FALSE;
    }

    bContainsPointers = info->bContainsPointers;
    s = info->BaseSize;

    if (info->ComponentSize)
    {
        // this is an array, so the size has to include the size of the components. We read the number
        // of components from the target and multiply by the component size to get the size.
        s += info->ComponentSize*GetNumComponents(dwAddrCurrObj);
    }

    // On x64 we do an optimization to save 4 bytes in almost every string we create
    // IMPORTANT: This cannot be done in ObjectSize, which is a wrapper to this function,
    //                    because we must Align only after these changes are made
#ifdef _TARGET_WIN64_
    // Pad to min object size if necessary
    if (s < min_obj_size)
        s = min_obj_size;
#endif // _TARGET_WIN64_

    s = (bLarge ? AlignLarge(s) : Align (s));
    return TRUE;
}

BOOL GetCollectibleDataEfficient(DWORD_PTR dwAddrMethTable, BOOL& bCollectible, TADDR& loaderAllocatorObjectHandle)
{
    MethodTableInfo* info = GetMethodTableInfo(dwAddrMethTable);
    if (info == NULL)
    {
        return FALSE;
    }

    bCollectible = info->bCollectible;
    loaderAllocatorObjectHandle = info->LoaderAllocatorObjectHandle;

    return TRUE;
}
