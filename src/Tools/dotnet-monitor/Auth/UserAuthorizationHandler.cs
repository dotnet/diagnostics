// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Diagnostics.Monitoring.RestServer;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Tools.Monitor
{
    /// <summary>
    /// Handles authorization for both Negotiate and ApiKey authentication.
    /// </summary>
    internal sealed class UserAuthorizationHandler : AuthorizationHandler<AuthorizedUserRequirement>
    {
        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, AuthorizedUserRequirement requirement)
        {
            // If the schema type is ApiKey, we do not need further authorization.
            if (context.User.Identity.AuthenticationType == AuthConstants.ApiKeySchema)
            {
                context.Succeed(requirement);
            }
            else if ((context.User.Identity.AuthenticationType == AuthConstants.NtlmSchema) ||
                    (context.User.Identity.AuthenticationType == AuthConstants.KerberosSchema) ||
                    (context.User.Identity.AuthenticationType == AuthConstants.NegotiateSchema))
            {
                //Negotiate, Kerberos, or NTLM.
                //CONSIDER In the future, we may want to have configuration around a dotnet-monitor group sid instead.
                //We cannot check the user against BUILTIN\Administrators group membership, since the browser user
                //has a deny claim on Administrator group.
                //Validate that the user that logged in matches the user that is running dotnet-monitor
                //Do not allow at all if running as Administrator.
                WindowsIdentity currentUser = WindowsIdentity.GetCurrent();
                WindowsPrincipal principal = new WindowsPrincipal(currentUser);
                if (principal.IsInRole(WindowsBuiltInRole.Administrator))
                {
                    return Task.CompletedTask;
                }

                Claim currentUserClaim = currentUser.Claims.FirstOrDefault(claim => string.Equals(claim.Type, ClaimTypes.PrimarySid));
                if ((currentUserClaim != null) && context.User.HasClaim(currentUserClaim.Type, currentUserClaim.Value))
                {
                    context.Succeed(requirement);
                }
            }

            return Task.CompletedTask;
        }
    }
}
