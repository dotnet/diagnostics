// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace SOS.Hosting.DbgEng.Interop
{
    public enum INTERFACE_TYPE
    {
        InterfaceTypeUndefined = -1,
        Internal,
        Isa,
        Eisa,
        MicroChannel,
        TurboChannel,
        PCIBus,
        VMEBus,
        NuBus,
        PCMCIABus,
        CBus,
        MPIBus,
        MPSABus,
        ProcessorInternal,
        InternalPowerBus,
        PNPISABus,
        PNPBus,
        Vmcs,
        MaximumInterfaceType
    }
}
