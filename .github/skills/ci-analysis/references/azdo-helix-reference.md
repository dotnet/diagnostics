# Azure DevOps and Helix Reference

## Supported Repositories

The script defaults to `dotnet/diagnostics` but works with any dotnet repository that uses Azure DevOps and Helix:

| Repository | Common Pipelines |
|------------|-----------------|
| `dotnet/diagnostics` | dotnet-diagnostics, dotnet-diagnostics-official |
| `dotnet/runtime` | runtime, runtime-dev-innerloop, dotnet-linker-tests |
| `dotnet/sdk` | dotnet-sdk (mix of local and Helix tests) |
| `dotnet/aspnetcore` | aspnetcore-ci |
| `dotnet/roslyn` | roslyn-CI |

Use `-Repository` to specify a different target:
```powershell
./scripts/Get-CIStatus.ps1 -PRNumber 12345 -Repository "dotnet/runtime"
```

**Note:** The script auto-discovers builds for a PR, so you rarely need to know definition IDs.

## Azure DevOps Organizations

**Public builds (default):**
- Organization: `dnceng-public`
- Project: `cbb18261-c48f-4abb-8651-8cdcb5474649`

**Internal/private builds:**
- Organization: `dnceng`
- Project GUID: Varies by pipeline

Override with:
```powershell
./scripts/Get-CIStatus.ps1 -BuildId 1276327 -Organization "dnceng" -Project "internal-project-guid"
```

## Common Pipeline Names (dotnet/diagnostics)

| Pipeline | Description |
|----------|-------------|
| `dotnet-diagnostics` | Main PR validation build |
| `dotnet-diagnostics-official` | Official/internal build |

The script discovers pipelines automatically from the PR.

## Useful Links

- [Helix Portal](https://helix.dot.net/): View Helix jobs and work items (all repos)
- [Helix API Documentation](https://helix.dot.net/swagger/): Swagger docs for Helix REST API
- [Build Analysis](https://github.com/dotnet/arcade/blob/main/Documentation/Projects/Build%20Analysis/LandingPage.md): Known issues tracking (arcade infrastructure)
- [dnceng-public AzDO](https://dev.azure.com/dnceng-public/public/_build): Public builds for all dotnet repos

## Test Execution Types

### Helix Tests
Tests run on Helix distributed test infrastructure. The script extracts console log URLs and can fetch detailed failure info with `-ShowLogs`.

### Local Tests (Non-Helix)
Some tests run directly on the build agent. The script detects these and extracts Azure DevOps Test Run URLs.

## Known Issue Labels

- `Known Build Error` - Used by Build Analysis across all dotnet repositories
- Search syntax: `repo:<owner>/<repo> is:issue is:open label:"Known Build Error" <test-name>`

> **Note:** The `dotnet/diagnostics` repository does not currently have Known Build Error tracking set up. The script will still search for known issues, but may not find matches. This feature is more useful when targeting repositories like `dotnet/runtime` that have active Build Analysis.

Example searches (use `search_issues` when GitHub MCP is available, `gh` CLI otherwise):
```bash
# Search in diagnostics
gh issue list --repo dotnet/diagnostics --label "Known Build Error" --search "SOS"

# Search in runtime
gh issue list --repo dotnet/runtime --label "Known Build Error" --search "FileSystemWatcher"
```
