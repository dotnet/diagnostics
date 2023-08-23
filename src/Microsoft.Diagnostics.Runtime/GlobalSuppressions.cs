// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// This file is used by Code Analysis to maintain SuppressMessage
// attributes that are applied to this project.
// Project-level suppressions either have no target or are given
// a specific target and scoped to a namespace, type, member, etc.

using System.Diagnostics.CodeAnalysis;

[assembly: SuppressMessage("Style", "IDE0011:Add braces", Justification = "Strong disagreement from maintainer on changing.", Scope = "module")]
[assembly: SuppressMessage("Reliability", "CA2021:Do not call Enumerable.Cast<T> or Enumerable.OfType<T> with incompatible types", Justification = "Bad analyzer", Scope = "module")]
