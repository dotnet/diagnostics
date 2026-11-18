// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.Diagnostics.DebugServices;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    /// <summary>
    /// Registers managed analysis services and commands.
    /// </summary>
    public sealed class AnalysisModule : IDiagnosticsModule
    {
        public void RegisterServices(IServiceRegistry registry)
        {
            if (registry is null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            registry.AddServiceFactory<ClrMDHelper>(ServiceScope.Runtime, ClrMDHelper.TryCreate);
            registry.AddService<DumpHeapService>(ServiceScope.Runtime);
            registry.AddService<GCHistory>(ServiceScope.Runtime);
            registry.AddService<ManagedFileLineService>(ServiceScope.Runtime);
            registry.AddService<LiveObjectService>(ServiceScope.Runtime);
            registry.AddServiceFactory<NativeAddressHelper>(ServiceScope.Target, NativeAddressHelper.TryCreate);
            registry.AddService<StaticVariableService>(ServiceScope.Runtime);
            registry.AddService<RootCacheService>(ServiceScope.Runtime);
        }

        public void RegisterCommands(ICommandService commandService)
        {
            if (commandService is null)
            {
                throw new ArgumentNullException(nameof(commandService));
            }

            commandService.AddCommands<AnalyzeOOMCommand>();
            commandService.AddCommands<AssembliesCommand>();
            commandService.AddCommands<CrashInfoCommand>();
            commandService.AddCommands<DumpAsyncCommand>();
            commandService.AddCommands<DumpConcurrentDictionaryCommand>();
            commandService.AddCommands<DumpConcurrentQueueCommand>();
            commandService.AddCommands<DumpExceptionsCommand>();
            commandService.AddCommands<DumpGenCommand>();
            commandService.AddCommands<DumpHeapCommand>();
            commandService.AddCommands<DumpHttpCommand>();
            commandService.AddCommands<DumpLogCommand>();
            commandService.AddCommands<DumpObjGCRefsCommand>();
            commandService.AddCommands<DumpRequestsCommand>();
            commandService.AddCommands<DumpRuntimeTypeCommand>();
            commandService.AddCommands<DumpStackObjectsCommand>();
            commandService.AddCommands<EEHeapCommand>();
            commandService.AddCommands<FinalizeQueueCommand>();
            commandService.AddCommands<FindEphemeralReferencesToLOHCommand>();
            commandService.AddCommands<FindPointersInCommand>();
            commandService.AddCommands<FindReferencesToEphemeralCommand>();
            commandService.AddCommands<GCHandleLeaksCommand>();
            commandService.AddCommands<GCHeapStatCommand>();
            commandService.AddCommands<GCRootCommand>();
            commandService.AddCommands<GCToNativeCommand>();
            commandService.AddCommands<GCWhereCommand>();
            commandService.AddCommands<HistClearCommand>();
            commandService.AddCommands<HistInitCommand>();
            commandService.AddCommands<HistObjCommand>();
            commandService.AddCommands<HistObjFindCommand>();
            commandService.AddCommands<HistRootCommand>();
            commandService.AddCommands<HistStatsCommand>();
            commandService.AddCommands<ConsoleLoggingCommand>();
            commandService.AddCommands<HelpCommand>();
            commandService.AddCommands<LoggingCommand>();
            commandService.AddCommands<ModulesCommand>();
            commandService.AddCommands<Microsoft.Diagnostics.Tools.Dump.ReadMemoryCommand>();
            commandService.AddCommands<RegistersCommand>();
            commandService.AddCommands<RuntimesCommand>();
            commandService.AddCommands<SetClrPath>();
            commandService.AddCommands<SetSymbolServerCommand>();
            commandService.AddCommands<StatusCommand>();
            commandService.AddCommands<ThreadsCommand>();
            commandService.AddCommands<ListNearObjCommand>();
            commandService.AddCommands<MAddressCommand>();
            commandService.AddCommands<Name2EECommand>();
            commandService.AddCommands<NotReachableInRangeCommand>();
            commandService.AddCommands<ObjSizeCommand>();
            commandService.AddCommands<ParallelStacksCommand>();
            commandService.AddCommands<PathToCommand>();
#if DEBUG
            commandService.AddCommands<SimulateGCHeapCorruption>();
#endif
            commandService.AddCommands<SizeStatsCommand>();
            commandService.AddCommands<TaskStateCommand>();
            commandService.AddCommands<ThreadPoolCommand>();
            commandService.AddCommands<ThreadPoolQueueCommand>();
            commandService.AddCommands<TimersCommand>();
            commandService.AddCommands<Token2EECommand>();
            commandService.AddCommands<TraverseHeapCommand>();
            commandService.AddCommands<VerifyHeapCommand>();
            commandService.AddCommands<VerifyObjectCommand>();
        }
    }
}
