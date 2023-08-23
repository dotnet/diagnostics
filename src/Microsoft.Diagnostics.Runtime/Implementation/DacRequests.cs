// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Diagnostics.Runtime.Implementation
{
    internal sealed class DacRequests
    {
        internal const uint VERSION = 0xe0000000U;
        internal const uint THREAD_STORE_DATA = 0xf0000000U;
        internal const uint APPDOMAIN_STORE_DATA = 0xf0000001U;
        internal const uint APPDOMAIN_LIST = 0xf0000002U;
        internal const uint APPDOMAIN_DATA = 0xf0000003U;
        internal const uint APPDOMAIN_NAME = 0xf0000004U;
        internal const uint APPDOMAIN_APP_BASE = 0xf0000005U;
        internal const uint APPDOMAIN_PRIVATE_BIN_PATHS = 0xf0000006U;
        internal const uint APPDOMAIN_CONFIG_FILE = 0xf0000007U;
        internal const uint ASSEMBLY_LIST = 0xf0000008U;
        internal const uint FAILED_ASSEMBLY_LIST = 0xf0000009U;
        internal const uint ASSEMBLY_DATA = 0xf000000aU;
        internal const uint ASSEMBLY_NAME = 0xf000000bU;
        internal const uint ASSEMBLY_DISPLAY_NAME = 0xf000000cU;
        internal const uint ASSEMBLY_LOCATION = 0xf000000dU;
        internal const uint FAILED_ASSEMBLY_DATA = 0xf000000eU;
        internal const uint FAILED_ASSEMBLY_DISPLAY_NAME = 0xf000000fU;
        internal const uint FAILED_ASSEMBLY_LOCATION = 0xf0000010U;
        internal const uint THREAD_DATA = 0xf0000011U;
        internal const uint THREAD_THINLOCK_DATA = 0xf0000012U;
        internal const uint CONTEXT_DATA = 0xf0000013U;
        internal const uint METHODDESC_DATA = 0xf0000014U;
        internal const uint METHODDESC_IP_DATA = 0xf0000015U;
        internal const uint METHODDESC_NAME = 0xf0000016U;
        internal const uint METHODDESC_FRAME_DATA = 0xf0000017U;
        internal const uint CODEHEADER_DATA = 0xf0000018U;
        internal const uint THREADPOOL_DATA = 0xf0000019U;
        internal const uint WORKREQUEST_DATA = 0xf000001aU;
        internal const uint OBJECT_DATA = 0xf000001bU;
        internal const uint FRAME_NAME = 0xf000001cU;
        internal const uint OBJECT_STRING_DATA = 0xf000001dU;
        internal const uint OBJECT_CLASS_NAME = 0xf000001eU;
        internal const uint METHODTABLE_NAME = 0xf000001fU;
        internal const uint METHODTABLE_DATA = 0xf0000020U;
        internal const uint EECLASS_DATA = 0xf0000021U;
        internal const uint FIELDDESC_DATA = 0xf0000022U;
        internal const uint MANAGEDSTATICADDR = 0xf0000023U;
        internal const uint MODULE_DATA = 0xf0000024U;
        internal const uint MODULEMAP_TRAVERSE = 0xf0000025U;
        internal const uint MODULETOKEN_DATA = 0xf0000026U;
        internal const uint PEFILE_DATA = 0xf0000027U;
        internal const uint PEFILE_NAME = 0xf0000028U;
        internal const uint ASSEMBLYMODULE_LIST = 0xf0000029U;
        internal const uint GCHEAP_DATA = 0xf000002aU;
        internal const uint GCHEAP_LIST = 0xf000002bU;
        internal const uint GCHEAPDETAILS_DATA = 0xf000002cU;
        internal const uint GCHEAPDETAILS_STATIC_DATA = 0xf000002dU;
        internal const uint HEAPSEGMENT_DATA = 0xf000002eU;
        internal const uint UNITTEST_DATA = 0xf000002fU;
        internal const uint ISSTUB = 0xf0000030U;
        internal const uint DOMAINLOCALMODULE_DATA = 0xf0000031U;
        internal const uint DOMAINLOCALMODULEFROMAPPDOMAIN_DATA = 0xf0000032U;
        internal const uint DOMAINLOCALMODULE_DATA_FROM_MODULE = 0xf0000033U;
        internal const uint SYNCBLOCK_DATA = 0xf0000034U;
        internal const uint SYNCBLOCK_CLEANUP_DATA = 0xf0000035U;
        internal const uint HANDLETABLE_TRAVERSE = 0xf0000036U;
        internal const uint RCWCLEANUP_TRAVERSE = 0xf0000037U;
        internal const uint EHINFO_TRAVERSE = 0xf0000038U;
        internal const uint STRESSLOG_DATA = 0xf0000039U;
        internal const uint JITLIST = 0xf000003aU;
        internal const uint JIT_HELPER_FUNCTION_NAME = 0xf000003bU;
        internal const uint JUMP_THUNK_TARGET = 0xf000003cU;
        internal const uint LOADERHEAP_TRAVERSE = 0xf000003dU;
        internal const uint MANAGER_LIST = 0xf000003eU;
        internal const uint JITHEAPLIST = 0xf000003fU;
        internal const uint CODEHEAP_LIST = 0xf0000040U;
        internal const uint METHODTABLE_SLOT = 0xf0000041U;
        internal const uint VIRTCALLSTUBHEAP_TRAVERSE = 0xf0000042U;
        internal const uint NESTEDEXCEPTION_DATA = 0xf0000043U;
        internal const uint USEFULGLOBALS = 0xf0000044U;
        internal const uint CLRTLSDATA_INDEX = 0xf0000045U;
        internal const uint MODULE_FINDIL = 0xf0000046U;
        internal const uint CLR_WATSON_BUCKETS = 0xf0000047U;
        internal const uint OOM_DATA = 0xf0000048U;
        internal const uint OOM_STATIC_DATA = 0xf0000049U;
        internal const uint GCHEAP_HEAPANALYZE_DATA = 0xf000004aU;
        internal const uint GCHEAP_HEAPANALYZE_STATIC_DATA = 0xf000004bU;
        internal const uint HANDLETABLE_FILTERED_TRAVERSE = 0xf000004cU;
        internal const uint METHODDESC_TRANSPARENCY_DATA = 0xf000004dU;
        internal const uint EECLASS_TRANSPARENCY_DATA = 0xf000004eU;
        internal const uint THREAD_STACK_BOUNDS = 0xf000004fU;
        internal const uint HILL_CLIMBING_LOG_ENTRY = 0xf0000050U;
        internal const uint THREADPOOL_DATA_2 = 0xf0000051U;
        internal const uint THREADLOCALMODULE_DAT = 0xf0000052U;
    }
}