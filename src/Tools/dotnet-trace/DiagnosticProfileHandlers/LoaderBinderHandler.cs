using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;
using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.Diagnostics.Tools.Trace.DiagnosticProfileHandlers
{
    /// <summary>
    /// This class handles parsing for loader and binder CLR events.
    /// </summary>
    internal class LoaderBinderHandler : IDiagnosticProfileHandler
    {
        public void AddHandler(EventPipeEventSource source)
        {
            source.Clr.AssemblyLoaderStart += Clr_AssemblyLoaderStart;
            source.Clr.AssemblyLoaderStop += Clr_AssemblyLoaderStop;
            source.Clr.AssemblyLoaderResolutionAttempted += Clr_AssemblyLoaderResolutionAttempted;
            source.Clr.AssemblyLoaderAppDomainAssemblyResolveHandlerInvoked += Clr_AssemblyLoaderAppDomainAssemblyResolveHandlerInvoked;
            source.Clr.AssemblyLoaderAssemblyLoadContextResolvingHandlerInvoked += Clr_AssemblyLoaderAssemblyLoadContextResolvingHandlerInvoked;
            source.Clr.AssemblyLoaderAssemblyLoadFromResolveHandlerInvoked += Clr_AssemblyLoaderAssemblyLoadFromResolveHandlerInvoked;
            source.Clr.AssemblyLoaderKnownPathProbed += Clr_AssemblyLoaderKnownPathProbed;
            source.Clr.LoaderAssemblyLoad += Clr_LoaderAssemblyLoad;
            source.Clr.LoaderAssemblyUnload += Clr_LoaderAssemblyUnload;
            source.Clr.LoaderModuleLoad += Clr_LoaderModuleLoad;
            source.Clr.LoaderModuleUnload += Clr_LoaderModuleUnload;
        }

        private void WriteLoaderBinderLog(string evtName, string msg)
        {
            Console.WriteLine($"[CLR-LoaderBinder|{evtName}] {msg}");
        }

        private void Clr_AssemblyLoaderStart(AssemblyLoadStartTraceData obj)
        {
            WriteLoaderBinderLog("AssemblyLoaderStart", $"Name={obj.AssemblyName};Path={obj.AssemblyPath};LoadContext={obj.AssemblyLoadContext};RequestingAssembly={obj.RequestingAssembly};RequestingAssemblyLoadContext={obj.RequestingAssemblyLoadContext}");
        }

        private void Clr_AssemblyLoaderStop(AssemblyLoadStopTraceData obj)
        {
            string loadSuccess = obj.Success ? "Load Successful" : "Load Failed";
            WriteLoaderBinderLog("AssemblyLoaderStop", $"{loadSuccess} Name={obj.ResultAssemblyName};Path={obj.ResultAssemblyPath};LoadContext={obj.AssemblyLoadContext};Cached={obj.Cached};RequestingAssembly={obj.RequestingAssembly};RequestingAssemblyLoadContext={obj.RequestingAssemblyLoadContext}");
        }

        private void Clr_AssemblyLoaderResolutionAttempted(ResolutionAttemptedTraceData obj)
        {
            if (obj.Result == ResolutionAttemptedResult.Success)
            {
                WriteLoaderBinderLog("ResolutionAttempted", $"Name={obj.AssemblyName};Stage={GetAssemblyResolutionStage(obj.Stage)};Result={GetAssemblyResolutionResult(obj.Result)};ResultAssemblyName={obj.ResultAssemblyName};ResultAssemblyPath={obj.ResultAssemblyPath}");
            }
            else
            {
                WriteLoaderBinderLog("ResolutionAttempted", $"Name={obj.AssemblyName};Stage={GetAssemblyResolutionStage(obj.Stage)};Result={GetAssemblyResolutionResult(obj.Result)};ErrorMessage={obj.ErrorMessage}");
            }
        }
 
        private void Clr_AssemblyLoaderAppDomainAssemblyResolveHandlerInvoked(AppDomainAssemblyResolveHandlerInvokedTraceData obj)
        {
            WriteLoaderBinderLog("AppDomainAssemblyResolveHandlerInvoked", $"Name={obj.AssemblyName};HandlerName={obj.HandlerName};ResultAssemblyName={obj.ResultAssemblyName};ResultAssemblyPath={obj.ResultAssemblyPath}");
        }

        private void Clr_AssemblyLoaderAssemblyLoadContextResolvingHandlerInvoked(AssemblyLoadContextResolvingHandlerInvokedTraceData obj)
        {
            WriteLoaderBinderLog("AssemblyLoadContextResolvingHandlerInvoked", $"Name={obj.AssemblyName};HandlerName={obj.HandlerName};AssemblyLoadContext={obj.AssemblyLoadContext};ResultAssemblyName={obj.ResultAssemblyName};ResultAssemblyPath={obj.ResultAssemblyPath}");
        }

        private void Clr_AssemblyLoaderAssemblyLoadFromResolveHandlerInvoked(AssemblyLoadFromResolveHandlerInvokedTraceData obj)
        {
            WriteLoaderBinderLog("AssemblyLoadFromResolveHandlerInvoked", $"Name={obj.AssemblyName};IsTracked={obj.IsTrackedLoad};RequestingAssemblyPath={obj.RequestingAssemblyPath};ComputedRequestedAssemblyPath={obj.ComputedRequestedAssemblyPath}");
        }

        private void Clr_AssemblyLoaderKnownPathProbed(KnownPathProbedTraceData obj)
        {
            WriteLoaderBinderLog("KnownPathProbed", $"FilePath={obj.FilePath};Source={GetKnownPathSource(obj.PathSource)};Result={obj.Result}");
        }

        private void Clr_LoaderModuleUnload(ModuleLoadUnloadTraceData obj)
        {
            WriteLoaderBinderLog("ModuleUnload", $"ModuleID={obj.ModuleID};AssemblyID={obj.AssemblyID};ModuleFlags={obj.ModuleFlags};ModuleILPath={obj.ModuleILPath};ModuleNativePath={obj.ModuleNativePath};ManagedPdbSignature={obj.ManagedPdbSignature};ManagedPdbAge={obj.ManagedPdbAge};ManagedPdbBuildPath={obj.ManagedPdbBuildPath};NativePdbSiganture={obj.NativePdbSignature};NatviePdbAge={obj.NativePdbAge};NativePdbBuildPath={obj.NativePdbBuildPath}");
        }

        private void Clr_LoaderModuleLoad(ModuleLoadUnloadTraceData obj)
        {
            WriteLoaderBinderLog("ModuleLoad", $"ModuleID={obj.ModuleID};AssemblyID={obj.AssemblyID};ModuleFlags={obj.ModuleFlags};ModuleILPath={obj.ModuleILPath};ModuleNativePath={obj.ModuleNativePath};ManagedPdbSignature={obj.ManagedPdbSignature};ManagedPdbAge={obj.ManagedPdbAge};ManagedPdbBuildPath={obj.ManagedPdbBuildPath};NativePdbSiganture={obj.NativePdbSignature};NatviePdbAge={obj.NativePdbAge};NativePdbBuildPath={obj.NativePdbBuildPath}");
        }

        private void Clr_LoaderAssemblyUnload(AssemblyLoadUnloadTraceData obj)
        {
            WriteLoaderBinderLog("AssemblyUnload", $"AssemblyID={obj.AssemblyID};AppDomainID={obj.AppDomainID};BindingID={obj.BindingID};AssemblyName={obj.FullyQualifiedAssemblyName};Flags={GetAssemblyFlagName(obj.AssemblyFlags)}");
        }

        private void Clr_LoaderAssemblyLoad(AssemblyLoadUnloadTraceData obj)
        {
            WriteLoaderBinderLog("AssemblyLoad", $"AssemblyID={obj.AssemblyID};AppDomainID={obj.AppDomainID};BindingID={obj.BindingID};AssemblyName={obj.FullyQualifiedAssemblyName};Flags={GetAssemblyFlagName(obj.AssemblyFlags)}");
        }

        private string GetAssemblyFlagName(AssemblyFlags flag)
        {
            return flag switch
            {
                AssemblyFlags.None => "None",
                AssemblyFlags.DomainNeutral => "Domain Neutral",
                AssemblyFlags.Dynamic => "Dynamic",
                AssemblyFlags.Native => "Native",
                AssemblyFlags.Collectible => "Collectible",
                AssemblyFlags.ReadyToRun => "ReadyToRun",
                _ => "UNKNOWN"
            };
        }

        private string GetAssemblyResolutionStage(ResolutionAttemptedStage stage)
        {
            return stage switch
            {
                ResolutionAttemptedStage.FindInLoadContext => "FindInLoadContext",
                ResolutionAttemptedStage.AssemblyLoadContextLoad => "AssemblyLoadContextLoad",
                ResolutionAttemptedStage.ApplicationAssemblies => "ApplicationAssemblies",
                ResolutionAttemptedStage.DefaultAssemblyLoadContextFallback => "DefaultAssemblyLoadContextFallback",
                ResolutionAttemptedStage.ResolveSatelliteAssembly => "ResolveSatelliteAssembly",
                ResolutionAttemptedStage.AssemblyLoadContextResolvingEvent => "AssemblyLoadContextResolving",
                ResolutionAttemptedStage.AppDomainAssemblyResolveEvent => "AppDomainAssemblyResolve",
                _ => "UNKNOWN"
            };
        }

        private string GetAssemblyResolutionResult(ResolutionAttemptedResult result)
        {
            return result switch
            {
                ResolutionAttemptedResult.Success => "Success",
                ResolutionAttemptedResult.AssemblyNotFound => "Assembly Not Found",
                ResolutionAttemptedResult.MismatchedAssemblyName => "Mismatched Assembly Name",
                ResolutionAttemptedResult.IncompatibleVersion => "Incompatible Version",
                ResolutionAttemptedResult.Failure => "Failure",
                ResolutionAttemptedResult.Exception => "Exception",
                _ => "UNKNOWN"
            };
        }

        private string GetKnownPathSource(KnownPathSource source)
        {
            return source switch
            {
                KnownPathSource.ApplicationAssemblies => "Application Assembly",
                KnownPathSource.AppNativeImagePaths => "App Native Image Path",
                KnownPathSource.AppPaths => "App Paths",
                KnownPathSource.PlatformResourceRoots => "Platform Resource Roots",
                KnownPathSource.SatelliteSubdirectory => "Satellite Subdirectory",
                _ => "UNKNOWN"
            };
        }


    }
}
