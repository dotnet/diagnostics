// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//*****************************************************************************
// File: daccess.h
//

//
// Support for external access of runtime data structures.  These
// macros and templates hide the details of pointer and data handling
// so that data structures and code can be compiled to work both
// in-process and through a special memory access layer.
//
// This code assumes the existence of two different pieces of code,
// the target, the runtime code that is going to be examined, and
// the host, the code that's doing the examining.  Access to the
// target is abstracted so the target may be a live process on the
// same machine, a live process on a different machine, a dump file
// or whatever.  No assumptions should be made about accessibility
// of the target.
//
// This code assumes that the data in the target is static.  Any
// time the target's data changes the interfaces must be reset so
// that potentially stale data is discarded.
//
// This code is intended for read access and there is no
// way to write data back currently.
//
// DAC-ized code:
// - is read-only (non-invasive). So DACized codepaths can not trigger a GC.
// - has no Thread* object.  In reality, DAC-ized codepaths are
//   ReadProcessMemory calls from out-of-process. Conceptually, they
//   are like a pure-native (preemptive) thread.
////
// This means that in particular, you cannot DACize a GCTRIGGERS function.
// Neither can you DACize a function that throws if this will involve
// allocating a new exception object. There may be
// exceptions to these rules if you can guarantee that the DACized
// part of the code path cannot cause a garbage collection (see
// EditAndContinueModule::ResolveField for an example).
// If you need to DACize a function that may trigger
// a GC, it is probably best to refactor the function so that the DACized
// part of the code path is in a separate function. For instance,
// functions with GetOrCreate() semantics are hard to DAC-ize because
// they the Create portion is inherently invasive. Instead, consider refactoring
// into a GetOrFail() function that DAC can call; and then make GetOrCreate()
// a wrapper around that.

//
// This code works by hiding the details of access to target memory.
// Access is divided into two types:
// 1. DPTR - access to a piece of data.
// 2. VPTR - access to a class with a vtable.  The class can only have
//           a single vtable pointer at the beginning of the class instance.
// Things only need to be declared as VPTRs when it is necessary to
// call virtual functions in the host.  In that case the access layer
// must do extra work to provide a host vtable for the object when
// it is retrieved so that virtual functions can be called.
//
// When compiling with DACCESS_COMPILE the macros turn into templates
// which replace pointers with smart pointers that know how to fetch
// data from the target process and provide a host process version of it.
// Normal data structure access will transparently receive a host copy
// of the data and proceed, so code such as
//     typedef DPTR(Class) PTR_Class;
//     PTR_Class cls;
//     int val = cls->m_Int;
// will work without modification.  The appropriate operators are overloaded
// to provide transparent access, such as the -> operator in this case.
// Note that the convention is to create an appropriate typedef for
// each type that will be accessed.  This hides the particular details
// of the type declaration and makes the usage look more like regular code.
//
// The ?PTR classes also have an implicit base type cast operator to
// produce a host-pointer instance of the given type.  For example
//     Class* cls = PTR_Class(addr);
// works by implicit conversion from the PTR_Class created by wrapping
// to a host-side Class instance.  Again, this means that existing code
// can work without modification.
//
// Code Example:
//
// typedef struct _rangesection
// {
//     PTR_IJitManager pjit;
//     PTR_RangeSection pright;
//     PTR_RangeSection pleft;
//     ... Other fields omitted ...
// } RangeSection;
//
//     RangeSection* pRS = m_RangeTree;
//
//     while (pRS != NULL)
//     {
//         if (currentPC < pRS->LowAddress)
//             pRS=pRS->pleft;
//         else if (currentPC > pRS->HighAddress)
//             pRS=pRS->pright;
//         else
//         {
//             return pRS->_pjit;
//         }
//     }
//
// This code does not require any modifications.  The global reference
// provided by m_RangeTree will be a host version of the RangeSection
// instantiated by conversion.  The references to pRS->pleft and
// pRS->pright will refer to DPTRs due to the modified declaration.
// In the assignment statement the compiler will automatically use
// the implicit conversion from PTR_RangeSection to RangeSection*,
// causing a host instance to be created.  Finally, if an appropriate
// section is found the use of pRS->_pjit will cause an implicit
// conversion from PTR_IJitManager to IJitManager.  The VPTR code
// will look at target memory to determine the actual derived class
// for the JitManager and instantiate the right class in the host so
// that host virtual functions can be used just as they would in
// the target.
//
// There are situations where code modifications are required, though.
//
// 1.  Any time the actual value of an address matters, such as using
//     it as a search key in a tree, the target address must be used.
//
// An example of this is the RangeSection tree used to locate JIT
// managers.  A portion of this code is shown above.  Each
// RangeSection node in the tree describes a range of addresses
// managed by the JitMan.  These addresses are just being used as
// values, not to dereference through, so there are not DPTRs.  When
// searching the range tree for an address the address used in the
// search must be a target address as that's what values are kept in
// the RangeSections.  In the code shown above, currentPC must be a
// target address as the RangeSections in the tree are all target
// addresses.  Use dac_cast<TADDR> to retrieve the target address
// of a ?PTR, as well as to convert a host address to the
// target address used to retrieve that particular instance. Do not
// use dac_cast with any raw target pointer types (such as BYTE*).
//
// 2.  Any time an address is modified, such as by address arithmetic,
//     the arithmetic must be performed on the target address.
//
// When a host instance is created it is created for the type in use.
// There is no particular relation to any other instance, so address
// arithmetic cannot be used to get from one instance to any other
// part of memory.  For example
//     char* Func(Class* cls)
//     {
//         // String follows the basic Class data.
//         return (char*)(cls + 1);
//     }
// does not work with external access because the Class* used would
// have retrieved only a Class worth of data.  There is no string
// following the host instance.  Instead, this code should use
// dac_cast<TADDR> to get the target address of the Class
// instance, add sizeof(*cls) and then create a new ?PTR to access
// the desired data.  Note that the newly retrieved data will not
// be contiguous with the Class instance, so address arithmetic
// will still not work.
//
// Previous Code:
//
//     BOOL IsTarget(LPVOID ip)
//     {
//         StubCallInstrs* pStubCallInstrs = GetStubCallInstrs();
//
//         if (ip == (LPVOID) &(pStubCallInstrs->m_op))
//         {
//             return TRUE;
//         }
//
// Modified Code:
//
//     BOOL IsTarget(LPVOID ip)
//     {
//         StubCallInstrs* pStubCallInstrs = GetStubCallInstrs();
//
//         if ((TADDR)ip == dac_cast<TADDR>(pStubCallInstrs) +
//             (TADDR)offsetof(StubCallInstrs, m_op))
//         {
//             return TRUE;
//         }
//
// The parameter ip is a target address, so the host pStubCallInstrs
// cannot be used to derive an address from.  The member & reference
// has to be replaced with a conversion from host to target address
// followed by explicit offsetting for the field.
//
// PTR_HOST_MEMBER_TADDR is a convenience macro that encapsulates
// these two operations, so the above code could also be:
//
//     if ((TADDR)ip ==
//         PTR_HOST_MEMBER_TADDR(StubCallInstrs, pStubCallInstrs, m_op))
//
// 3.  Any time the amount of memory referenced through an address
//     changes, such as by casting to a different type, a new ?PTR
//     must be created.
//
// Host instances are created and stored based on both the target
// address and size of access.  The access code has no way of knowing
// all possible ways that data will be retrieved for a given address
// so if code changes the way it accesses through an address a new
// ?PTR must be used, which may lead to a difference instance and
// different host address.  This means that pointer identity does not hold
// across casts, so code like
//     Class* cls = PTR_Class(addr);
//     Class2* cls2 = PTR_Class2(addr);
//     return cls == cls2;
// will fail because the host-side instances have no relation to each
// other.  That isn't a problem, since by rule #1 you shouldn't be
// relying on specific host address values.
//
// Previous Code:
//
//     return (ArrayClass *) m_pMethTab->GetClass();
//
// Modified Code:
//
//     return PTR_ArrayClass(m_pMethTab->GetClass());
//
// The ?PTR templates have an implicit conversion from a host pointer
// to a target address, so the cast above constructs a new
// PTR_ArrayClass by implicitly converting the host pointer result
// from GetClass() to its target address and using that as the address
// of the new PTR_ArrayClass.  As mentioned, the actual host-side
// pointer values may not be the same.
//
// Host pointer identity can be assumed as long as the type of access
// is the same.  In the example above, if both accesses were of type
// Class then the host pointer will be the same, so it is safe to
// retrieve the target address of an instance and then later get
// a new host pointer for the target address using the same type as
// the host pointer in that case will be the same.  This is enabled
// by caching all of the retrieved host instances.  This cache is searched
// by the addr:size pair and when there's a match the existing instance
// is reused.  This increases performance and also allows simple
// pointer identity to hold.  It does mean that host memory grows
// in proportion to the amount of target memory being referenced,
// so retrieving extraneous data should be avoided.
// The host-side data cache grows until the Flush() method is called,
// at which point all host-side data is discarded.  No host
// instance pointers should be held across a Flush().
//
// Accessing into an object can lead to some unusual behavior.  For
// example, the SList class relies on objects to contain an SLink
// instance that it uses for list maintenance.  This SLink can be
// embedded anywhere in the larger object.  The SList access is always
// purely to an SLink, so when using the access layer it will only
// retrieve an SLink's worth of data.  The SList template will then
// do some address arithmetic to determine the start of the real
// object and cast the resulting pointer to the final object type.
// When using the access layer this results in a new ?PTR being
// created and used, so a new instance will result.  The internal
// SLink instance will have no relation to the new object instance
// even though in target address terms one is embedded in the other.
// The assumption of data stability means that this won't cause
// a problem, but care must be taken with the address arithmetic,
// as laid out in rules #2 and #3.
//
// 4.  Global address references cannot be used.  Any reference to a
//     global piece of code or data, such as a function address, global
//     variable or class static variable, must be changed.
//
// The external access code may load at a different base address than
// the target process code.  Global addresses are therefore not
// meaningful and must be replaced with something else.  There isn't
// a single solution, so replacements must be done on a case-by-case
// basis.
//
// The simplest case is a global or class static variable.  All
// declarations must be replaced with a special declaration that
// compiles into a modified accessor template value when compiled for
// external data access.  Uses of the variable automatically are fixed
// up by the template instance.  Note that assignment to the global
// must be independently ifdef'ed as the external access layer should
// not make any modifications.
//
// Macros allow for simple declaration of a class static and global
// values that compile into an appropriate templated value.
//
// Previous Code:
//
//     static RangeSection* m_RangeTree;
//     RangeSection* ExecutionManager::m_RangeTree;
//
//     extern ThreadStore* g_pThreadStore;
//     ThreadStore* g_pThreadStore = &StaticStore;
//     class SystemDomain : public BaseDomain {
//         ...
//         ArrayListStatic m_appDomainIndexList;
//         ...
//     }
//
//     SystemDomain::m_appDomainIndexList;
//
//     extern DWORD gThreadTLSIndex;
//
//     DWORD gThreadTLSIndex = TLS_OUT_OF_INDEXES;
//
// Modified Code:
//
//     typedef DPTR(RangeSection) PTR_RangeSection;
//     SPTR_DECL(RangeSection, m_RangeTree);
//     SPTR_IMPL(RangeSection, ExecutionManager, m_RangeTree);
//
//     typedef DPTR(ThreadStore) PTR_ThreadStore
//     GPTR_DECL(ThreadStore, g_pThreadStore);
//     GPTR_IMPL_INIT(ThreadStore, g_pThreadStore, &StaticStore);
//
//     class SystemDomain : public BaseDomain {
//         ...
//         SVAL_DECL(ArrayListStatic; m_appDomainIndexList);
//         ...
//     }
//
//     SVAL_IMPL(ArrayListStatic, SystemDomain, m_appDomainIndexList);
//
//     GVAL_DECL(DWORD, gThreadTLSIndex);
//
//     GVAL_IMPL_INIT(DWORD, gThreadTLSIndex, TLS_OUT_OF_INDEXES);
//
// When declaring the variable, the first argument declares the
// variable's type and the second argument declares the variable's
// name.  When defining the variable the arguments are similar, with
// an extra class name parameter for the static class variable case.
// If an initializer is needed the IMPL_INIT macro should be used.
//
// Things get slightly more complicated when declaring an embedded
// array.  In this case the data element is not a single element and
// therefore cannot be represented by a ?PTR. In the case of a global
// array, you should use the GARY_DECL and GARY_IMPL macros.
// We durrently have no support for declaring static array data members
// or initialized arrays. Array data members that are dynamically allocated
// need to be treated as pointer members. To reference individual elements
// you must use pointer arithmetic (see rule 2 above). An array declared
// as a local variable within a function does not need to be DACized.
//
//
// All uses of ?VAL_DECL must have a corresponding entry given in the
// DacGlobals structure in src\inc\dacvars.h.  For SVAL_DECL the entry
// is class__name.  For GVAL_DECL the entry is dac__name. You must add
// these entries in dacvars.h using the DEFINE_DACVAR macro. Note that
// these entries also are used for dumping memory in mini dumps and
// heap dumps. If it's not appropriate to dump a variable, (e.g.,
// it's an array or some other value that is not important to have
// in a minidump) a second macro, DEFINE_DACVAR_NO_DUMP, will allow
// you to make the required entry in the DacGlobals structure without
// dumping its value. If the variable is implemented with one of the VOLATILE_* macros
// then the DEFINE_DACVAR_VOLATILE macro must be used.
//
// For convenience, here is a list of the various variable declaration and
// initialization macros:
// SVAL_DECL(type, name)      static non-pointer data   class MyClass
//                            member declared within    {
//                            the class declaration        // static int i;
//                                                         SVAL_DECL(int, i);
//                                                      }
//
// SVAL_IMPL(type, cls, name) static non-pointer data   // int MyClass::i;
//                            member defined outside    SVAL_IMPL(int, MyClass, i);
//                            the class declaration
//
// SVAL_IMPL_INIT(type, cls,  static non-pointer data   // int MyClass::i = 0;
//                name, val)  member defined and        SVAL_IMPL_INIT(int, MyClass, i, 0);
//                            initialized outside the
//                            class declaration
// ------------------------------------------------------------------------------------------------
// VOLATILE_SVAL_DECL(type, name)    static volatile   class MyClass
//                                   non-pointer data  {
//                                   member declared      // static Volatile<int> i;
//                                   within the class     VOLATILE_SVAL_DECL(int, i);
//                                    declaration      }
//
// VOLATILE_SVAL_IMPL(type, cls,     static volatile
//                    name)          non-pointer data  // Volatile<int> MyClass::i;
//                                   member defined    VOLATILE_SVAL_IMPL(int, MyClass, i);
//                                   outside the
//                                   class declaration
//
// VOLATILE_SVAL_IMPL_INIT(          static volatile
//    type, cls, name)               non-pointer data  // Volatile<int> MyClass::i = 0;
//                                   member defined    VOLATILE_SVAL_IMPL_INIT(int, MyClass, i, 0);
//                                   and initialized
//                                   outside the
//                                   class declaration
// ------------------------------------------------------------------------------------------------
// SPTR_DECL(type, name)      static pointer data       class MyClass
//                            member declared within    {
//                            the class declaration        // static int * pInt;
//                                                         SPTR_DECL(int, pInt);
//                                                      }
//
// SPTR_IMPL(type, cls, name) static pointer data       // int * MyClass::pInt;
//                            member defined outside    SPTR_IMPL(int, MyClass, pInt);
//                            the class declaration
//
// SPTR_IMPL_INIT(type, cls,  static pointer data       // int * MyClass::pInt = NULL;
//                name, val)  member defined and        SPTR_IMPL_INIT(int, MyClass, pInt, NULL);
//                            initialized outside the
//                            class declaration
// ------------------------------------------------------------------------------------------------
// VOLATILE_SPTR_DECL(type, name)    static volatile   class MyClass
//                                   pointer data      {
//                                   member declared      // static Volatile<int*> i;
//                                   within the class     VOLATILE_SPTR_DECL(int, i);
//                                    declaration      }
//
// VOLATILE_SPTR_IMPL(type, cls,     static volatile
//                    name)          pointer data      // Volatile<int*> MyClass::i;
//                                   member defined    VOLATILE_SPTR_IMPL(int, MyClass, i);
//                                   outside the
//                                   class declaration
//
// VOLATILE_SPTR_IMPL_INIT(          static volatile
//    type, cls, name)               pointer data      // Volatile<int*> MyClass::i = 0;
//                                   member defined    VOLATILE_SPTR_IMPL_INIT(int, MyClass, i, 0);
//                                   and initialized
//                                   outside the
//                                   class declaration
// ------------------------------------------------------------------------------------------------
// GVAL_DECL(type, name)      extern declaration of     // extern int g_i
//                            global non-pointer        GVAL_DECL(int, g_i);
//                            variable
//
// GVAL_IMPL(type, name)      declaration of a          // int g_i
//                            global non-pointer        GVAL_IMPL(int, g_i);
//                            variable
//
// GVAL_IMPL_INIT (type,      declaration and           // int g_i = 0;
//                 name,      initialization of a       GVAL_IMPL_INIT(int, g_i, 0);
//                 val)       global non-pointer
//                            variable
// ****Note****
// If you use GVAL_? to declare a global variable of a structured type and you need to
// access a member of the type, you cannot use the dot operator. Instead, you must take the
// address of the variable and use the arrow operator. For example:
// struct
// {
//    int x;
//    char ch;
// } MyStruct;
// GVAL_IMPL(MyStruct, g_myStruct);
// int i = (&g_myStruct)->x;
// ------------------------------------------------------------------------------------------------
// GPTR_DECL(type, name)      extern declaration of     // extern int * g_pInt
//                            global pointer            GPTR_DECL(int, g_pInt);
//                            variable
//
// GPTR_IMPL(type, name)      declaration of a          // int * g_pInt
//                            global pointer            GPTR_IMPL(int, g_pInt);
//                            variable
//
// GPTR_IMPL_INIT (type,      declaration and           // int * g_pInt = 0;
//                 name,      initialization of a       GPTR_IMPL_INIT(int, g_pInt, NULL);
//                 val)       global pointer
//                            variable
// ------------------------------------------------------------------------------------------------
// GARY_DECL(type, name)      extern declaration of     // extern int g_rgIntList[MAX_ELEMENTS];
//                            a global array            GPTR_DECL(int, g_rgIntList, MAX_ELEMENTS);
//                            variable
//
// GARY_IMPL(type, name)      declaration of a          // int g_rgIntList[MAX_ELEMENTS];
//                            global pointer            GPTR_IMPL(int, g_rgIntList, MAX_ELEMENTS);
//                            variable
//
//
// Certain pieces of code, such as the stack walker, rely on identifying
// an object from its vtable address.  As the target vtable addresses
// do not necessarily correspond to the vtables used in the host, these
// references must be translated.  The access layer maintains translation
// tables for all classes used with VPTR and can return the target
// vtable pointer for any host vtable in the known list of VPTR classes.
//
// ----- Errors:
//
// All errors in the access layer are reported via exceptions.  The
// formal access layer methods catch all such exceptions and turn
// them into the appropriate error, so this generally isn't visible
// to users of the access layer.
//
// ----- DPTR Declaration:
//
// Create a typedef for the type with typedef DPTR(type) PTR_type;
// Replace type* with PTR_type.
//
// ----- VPTR Declaration:
//
// VPTR can only be used on classes that have a single vtable
// pointer at the beginning of the object.  This should be true
// for a normal single-inheritance object.
//
// All of the classes that may be instantiated need to be identified
// and marked.  In the base class declaration add either
// VPTR_BASE_VTABLE_CLASS if the class is abstract or
// VPTR_BASE_CONCRETE_VTABLE_CLASS if the class is concrete.  In each
// derived class add VPTR_VTABLE_CLASS.  If you end up with compile or
// link errors for an unresolved method called VPtrSize you missed a
// derived class declaration.
//
//
// All classes to be instantiated must be listed in src\inc\vptr_list.h.
//
// Create a typedef for the type with typedef VPTR(type) PTR_type;
// When using a VPTR, replace Class* with PTR_Class.
//
// ----- Specific Macros:
//
// PTR_TO_TADDR(ptr)
// Retrieves the raw target address for a ?PTR.
// See code:dac_cast for the preferred alternative
//
// PTR_HOST_TO_TADDR(host)
// Given a host address of an instance produced by a ?PTR reference,
// return the original target address.  The host address must
// be an exact match for an instance.
// See code:dac_cast for the preferred alternative
//
// PTR_HOST_INT_TO_TADDR(host)
// Given a host address which resides somewhere within an instance
// produced by a ?PTR reference (a host interior pointer) return the
// corresponding target address. This is useful for evaluating
// relative pointers (e.g. RelativePointer<T>) where calculating the
// target address requires knowledge of the target address of the
// relative pointer field itself. This lookup is slower than that for
// a non-interior host pointer so use it sparingly.
//
// VPTR_HOST_VTABLE_TO_TADDR(host)
// Given the host vtable pointer for a known VPTR class, return
// the target vtable pointer.
//
// PTR_HOST_MEMBER_TADDR(type, host, memb)
// Retrieves the target address of a host instance pointer and
// offsets it by the given member's offset within the type.
//
// PTR_HOST_INT_MEMBER_TADDR(type, host, memb)
// As above but will work for interior host pointers (see the
// description of PTR_HOST_INT_TO_TADDR for an explanation of host
// interior pointers).
//
// PTR_READ(addr, size)
// Reads a block of memory from the target and returns a host
// pointer for it.  Useful for reading blocks of data from the target
// whose size is only known at runtime, such as raw code for a jitted
// method.  If the data being read is actually an object, use SPTR
// instead to get better type semantics.
//
// DAC_EMPTY()
// DAC_EMPTY_ERR()
// DAC_EMPTY_RET(retVal)
// DAC_UNEXPECTED()
// Provides an empty method implementation when compiled
// for DACCESS_COMPILE.  For example, use to stub out methods needed
// for vtable entries but otherwise unused.
//
// These macros are designed to turn into normal code when compiled
// without DACCESS_COMPILE.
//
//*****************************************************************************


#ifndef __daccess_h__
#define __daccess_h__

#ifndef NATIVEAOT
#include <stdint.h>

#if !defined(HOST_WINDOWS)
#include <pal_mstypes.h>
#endif

#include "switches.h"
#include "safemath.h"
#include "corerror.h"

// Keep in sync with the definitions in dbgutil.cpp and createdump.h
#define DACCESS_TABLE_SYMBOL "g_dacTable"

#include <type_traits>
#include "crosscomp.h"

#include <dn-u16.h>

// Information stored in the DAC table of interest to the DAC implementation
// Note that this information is shared between all instantiations of ClrDataAccess, so initialize
// it just once in code:ClrDataAccess.GetDacGlobals (rather than use fields in ClrDataAccess);
struct DacTableInfo
{
    // On Windows, the first DWORD is the 32-bit timestamp read out of the runtime dll's debug directory.
    // The remaining 3 DWORDS must all be 0.
    // On Mac, this is the 16-byte UUID of the runtime dll.
    // It is used to validate that mscorwks is the same version as mscordacwks
    DWORD dwID0;
    DWORD dwID1;
    DWORD dwID2;
    DWORD dwID3;
};

// The header of the DAC table.  This includes the number of globals, the number of vptrs, and
// the DacTableInfo structure.  We need the DacTableInfo and DacTableHeader structs outside
// of a DACCESS_COMPILE since soshost walks the Dac table headers to find the UUID of CoreCLR
// in the target process.
struct DacTableHeader
{
    ULONG numGlobals;
    ULONG numVptrs;
    DacTableInfo info;
};

//
// This version of things wraps pointer access in
// templates which understand how to retrieve data
// through an access layer.  In this case no assumptions
// can be made that the current compilation processor or
// pointer types match the target's processor or pointer types.
//

// Define TADDR as a non-pointer value so use of it as a pointer
// will not work properly.  Define it as unsigned so
// pointer comparisons aren't affected by sign.
typedef uintptr_t TADDR;

// TSIZE_T used for counts or ranges that need to span the size of a
// target pointer.  For cross-plat, this may be different than SIZE_T
// which reflects the host pointer size.
typedef SIZE_T TSIZE_T;
#endif // !NATIVEAOT

// Used for base classes that can be instantiated directly.
// The fake vfn is still used to force a vtable even when
// all the normal vfns are ifdef'ed out.
#define VPTR_BASE_CONCRETE_VTABLE_CLASS(name)

//
// This version of the macros turns into normal pointers
// for unmodified in-proc compilation.

// *******************************************************
// !!!!!!!!!!!!!!!!!!!!!!!!!NOTE!!!!!!!!!!!!!!!!!!!!!!!!!!
//
// Please search this file for the type name to find the
// DAC versions of these definitions
//
// !!!!!!!!!!!!!!!!!!!!!!!!!NOTE!!!!!!!!!!!!!!!!!!!!!!!!!!
// *******************************************************


// Declare TADDR as a non-pointer type so that arithmetic
// can be done on it directly, as with the DACCESS_COMPILE definition.
// This also helps expose pointer usage that may need to be changed.
typedef uintptr_t TADDR;

typedef void* PTR_VOID;
typedef LPVOID* PTR_PTR_VOID;
typedef const void* PTR_CVOID;

#define DPTR(type) type*
#define ArrayDPTR(type) type*
#define SPTR(type) type*
#define VPTR(type) type*
#define S8PTR(type) type*
#define S8PTRMAX(type, maxChars) type*
#define S16PTR(type) type*
#define S16PTRMAX(type, maxChars) type*
#define _SPTR_DECL(acc_type, store_type, var) \
    static store_type var
#define _SPTR_IMPL(acc_type, store_type, cls, var) \
    store_type cls::var
#define GVAL_DECL(type, var) \
    extern type var
#define GVAL_IMPL(type, var) \
    type var
#define GVAL_IMPL_INIT(type, var, init) \
    type var = init

//----------------------------------------------------------------------------
// dac_cast
// Casting utility, to be used for casting one class pointer type to another.
// Use as you would use static_cast
//
// dac_cast is designed to act just as static_cast does when
// dealing with pointers and their DAC abstractions. Specifically,
// it handles these coversions:
//
//      dac_cast<TargetType>(SourceTypeVal)
//
// where TargetType <- SourceTypeVal are
//
//      ?PTR(Tgt) <- TADDR     - Create PTR type (DPtr etc.) from TADDR
//      ?PTR(Tgt) <- ?PTR(Src) - Convert one PTR type to another
//      ?PTR(Tgt) <- Src *     - Create PTR type from dac host object instance
//      TADDR <- ?PTR(Src)     - Get TADDR of PTR object (DPtr etc.)
//      TADDR <- Src *         - Get TADDR of dac host object instance
//
// Note that there is no direct conversion to other host-pointer types (because we don't
// know if you want a DPTR or VPTR etc.).  However, due to the implicit DAC conversions,
// you can just use dac_cast<PTR_Foo> and assign that to a Foo*.
//
// The beauty of this syntax is that it is consistent regardless
// of source and target casting types. You just use dac_cast
// and the partial template specialization will do the right thing.
//
// One important thing to realise is that all "Foo *" types are
// assumed to be pointers to host instances that were marshalled by DAC.  This should
// fail at runtime if it's not the case.
//
// Some examples would be:
//
//   - Host pointer of one type to a related host pointer of another
//     type, i.e., MethodDesc * <-> InstantiatedMethodDesc *
//     Syntax: with MethodDesc *pMD, InstantiatedMethodDesc *pInstMD
//             pInstMd = dac_cast<PTR_InstantiatedMethodDesc>(pMD)
//             pMD = dac_cast<PTR_MethodDesc>(pInstMD)
//
//   - (D|V)PTR of one encapsulated pointer type to a (D|V)PTR of
//     another type, i.e., PTR_AppDomain <-> PTR_BaseDomain
//     Syntax: with PTR_AppDomain pAD, PTR_BaseDomain pBD
//             dac_cast<PTR_AppDomain>(pBD)
//             dac_cast<PTR_BaseDomain>(pAD)
//
// Example comparisons of some old and new syntax, where
//    h is a host pointer, such as "Foo *h;"
//    p is a DPTR, such as "PTR_Foo p;"
//
//      PTR_HOST_TO_TADDR(h)           ==> dac_cast<TADDR>(h)
//      PTR_TO_TADDR(p)                ==> dac_cast<TADDR>(p)
//      PTR_Foo(PTR_HOST_TO_TADDR(h))  ==> dac_cast<PTR_Foo>(h)
//
//----------------------------------------------------------------------------
template <typename Tgt, typename Src>
inline Tgt dac_cast(Src src)
{
    // In non-DAC builds, dac_cast is the same as a C-style cast because we need to support:
    //  - casting away const
    //  - conversions between pointers and TADDR
    // Perhaps we should more precisely restrict it's usage, but we get the precise
    // restrictions in DAC builds, so it wouldn't buy us much.
    return (Tgt)(src);
#define SPTR_DECL(type, var) _SPTR_DECL(type*, PTR_##type, var)
#define SPTR_IMPL(type, cls, var) _SPTR_IMPL(type*, PTR_##type, cls, var)
}

//----------------------------------------------------------------------------
//
// Forward typedefs for system types.  This is a convenient place
// to declare things for system types, plus it gives us a central
// place to look at when deciding what types may cause issues for
// cross-platform compilation.
//
//----------------------------------------------------------------------------

typedef DPTR(size_t)       PTR_size_t;
typedef ArrayDPTR(uint8_t) PTR_uint8_t;
typedef DPTR(PTR_uint8_t)  PTR_PTR_uint8_t;
typedef DPTR(int32_t)      PTR_int32_t;
typedef DPTR(uint32_t)     PTR_uint32_t;
typedef DPTR(uint64_t)     PTR_uint64_t;
typedef DPTR(uintptr_t)    PTR_uintptr_t;
typedef DPTR(TADDR)        PTR_TADDR;

#ifndef NATIVEAOT
typedef ArrayDPTR(BYTE)    PTR_BYTE;
typedef DPTR(PTR_BYTE) PTR_PTR_BYTE;
typedef DPTR(PTR_PTR_BYTE) PTR_PTR_PTR_BYTE;
typedef ArrayDPTR(signed char) PTR_SBYTE;
typedef ArrayDPTR(const BYTE) PTR_CBYTE;
typedef DPTR(INT8)    PTR_INT8;
typedef DPTR(INT16)   PTR_INT16;
typedef DPTR(UINT16)  PTR_UINT16;
typedef DPTR(WORD)    PTR_WORD;
typedef DPTR(USHORT)  PTR_USHORT;
typedef DPTR(DWORD)   PTR_DWORD;
typedef DPTR(LONG)    PTR_LONG;
typedef DPTR(ULONG)   PTR_ULONG;
typedef DPTR(INT32)   PTR_INT32;
typedef DPTR(UINT32)  PTR_UINT32;
typedef DPTR(ULONG64) PTR_ULONG64;
typedef DPTR(INT64)   PTR_INT64;
typedef DPTR(UINT64)  PTR_UINT64;
typedef DPTR(SIZE_T)  PTR_SIZE_T;
typedef DPTR(int)     PTR_int;
typedef DPTR(BOOL)    PTR_BOOL;
typedef DPTR(unsigned) PTR_unsigned;

typedef S8PTR(char)           PTR_STR;
typedef S8PTR(const char)     PTR_CSTR;
typedef S8PTR(char)           PTR_UTF8;
typedef S8PTR(const char)     PTR_CUTF8;
typedef S16PTR(WCHAR)         PTR_WSTR;
typedef S16PTR(const WCHAR)   PTR_CWSTR;

typedef DPTR(T_CONTEXT)                  PTR_CONTEXT;
typedef DPTR(PTR_CONTEXT)                PTR_PTR_CONTEXT;
typedef DPTR(struct _EXCEPTION_POINTERS) PTR_EXCEPTION_POINTERS;
typedef DPTR(struct _EXCEPTION_RECORD)   PTR_EXCEPTION_RECORD;

typedef DPTR(struct _EXCEPTION_REGISTRATION_RECORD) PTR_EXCEPTION_REGISTRATION_RECORD;

typedef DPTR(struct IMAGE_COR_VTABLEFIXUP) PTR_IMAGE_COR_VTABLEFIXUP;
typedef DPTR(IMAGE_DATA_DIRECTORY)  PTR_IMAGE_DATA_DIRECTORY;
typedef DPTR(IMAGE_DEBUG_DIRECTORY)  PTR_IMAGE_DEBUG_DIRECTORY;
typedef DPTR(IMAGE_DOS_HEADER)      PTR_IMAGE_DOS_HEADER;
typedef DPTR(IMAGE_NT_HEADERS)      PTR_IMAGE_NT_HEADERS;
typedef DPTR(IMAGE_NT_HEADERS32)    PTR_IMAGE_NT_HEADERS32;
typedef DPTR(IMAGE_NT_HEADERS64)    PTR_IMAGE_NT_HEADERS64;
typedef DPTR(IMAGE_SECTION_HEADER)  PTR_IMAGE_SECTION_HEADER;
typedef DPTR(IMAGE_EXPORT_DIRECTORY)  PTR_IMAGE_EXPORT_DIRECTORY;
typedef DPTR(IMAGE_TLS_DIRECTORY)   PTR_IMAGE_TLS_DIRECTORY;
#endif

//----------------------------------------------------------------------------
//
// A PCODE is a valid PC/IP value -- a pointer to an instruction, possibly including some processor mode bits.
// (On ARM, for example, a PCODE value should have the low-order THUMB_CODE bit set if the code should
// be executed in that mode.)
//
typedef TADDR PCODE;
typedef DPTR(PCODE) PTR_PCODE;
typedef DPTR(PTR_PCODE) PTR_PTR_PCODE;
// For cross compilation, controlling type layout is important
// We add a simple macro here which defines DAC_ALIGNAS to the C++11 alignas operator
// This helps force the alignment of the next member
// For most cross compilation cases the layout of types simply works
// There are a few cases (where this macro is helpful) which are not consistent across platforms:
// - Base class whose size is padded to its align size.  On Linux the gcc/clang
//   layouts will reuse this padding in the derived class for the first member
// - Class with an vtable pointer and an alignment greater than the pointer size.
//   The Windows compilers will align the first member to the alignment size of the
//   class.  Linux will align the first member to its natural alignment
#define DAC_ALIGNAS(a) alignas(a)

#endif // #ifndef __daccess_h__
