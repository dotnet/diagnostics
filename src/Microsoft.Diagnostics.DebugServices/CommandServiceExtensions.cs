// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Diagnostics.DebugServices
{
    public static class CommandServiceExtensions
    {
        /// <summary>
        /// Registers a command implementation using constructor injection.
        /// </summary>
        public static void AddCommands<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors |
            DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.PublicProperties)] TCommand>(
            this ICommandService commandService) where TCommand : class
        {
            if (commandService is null)
            {
                throw new ArgumentNullException(nameof(commandService));
            }
            ObjectFactory factory = ActivatorUtilities.CreateFactory(typeof(TCommand), Type.EmptyTypes);
            commandService.AddCommands<TCommand>(services => (TCommand)factory(services, Array.Empty<object>()));
        }
    }
}
