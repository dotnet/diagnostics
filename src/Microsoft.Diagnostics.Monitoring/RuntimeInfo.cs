using System.IO;

namespace Microsoft.Diagnostics.Monitoring
{
    internal static class RuntimeInfo
    {
        private static readonly string KubernetesServiceAccountPath = "/var/run/secrets/kubernetes.io/serviceaccount";

        public static bool IsKubernetes => Directory.Exists(KubernetesServiceAccountPath);
    }
}