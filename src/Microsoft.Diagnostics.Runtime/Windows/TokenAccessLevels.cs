// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Microsoft.Diagnostics.Runtime.Windows
{
    //
    // Summary:
    //     Defines the privileges of the user account associated with the access token.
    [Flags]
    internal enum TokenAccessLevels
    {
        //
        // Summary:
        //     The user can attach a primary token to a process.
        AssignPrimary = 1,
        //
        // Summary:
        //     The user can duplicate the token.
        Duplicate = 2,
        //
        // Summary:
        //     The user can impersonate a client.
        Impersonate = 4,
        //
        // Summary:
        //     The user can query the token.
        Query = 8,
        //
        // Summary:
        //     The user can query the source of the token.
        QuerySource = 16,
        //
        // Summary:
        //     The user can enable or disable privileges in the token.
        AdjustPrivileges = 32,
        //
        // Summary:
        //     The user can change the attributes of the groups in the token.
        AdjustGroups = 64,
        //
        // Summary:
        //     The user can change the default owner, primary group, or discretionary access
        //     control list (DACL) of the token.
        AdjustDefault = 128,
        //
        // Summary:
        //     The user can adjust the session identifier of the token.
        AdjustSessionId = 256,
        //
        // Summary:
        //     The user has standard read rights and the System.Security.Principal.TokenAccessLevels.Query
        //     privilege for the token.
        Read = 131080,
        //
        // Summary:
        //     The user has standard write rights and the System.Security.Principal.TokenAccessLevels.AdjustPrivileges,
        //     System.Security.Principal.TokenAccessLevels.AdjustGroups and System.Security.Principal.TokenAccessLevels.AdjustDefault
        //     privileges for the token.
        Write = 131296,
        //
        // Summary:
        //     The user has all possible access to the token.
        AllAccess = 983551,
        //
        // Summary:
        //     The maximum value that can be assigned for the System.Security.Principal.TokenAccessLevels
        //     enumeration.
        MaximumAllowed = 33554432
    }
}