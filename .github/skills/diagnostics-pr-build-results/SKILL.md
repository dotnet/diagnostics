---
name: diagnostics-pr-build-results
description: Look up CI build results for a pull request to the diagnostics repository. Use this when asked to check build status, investigate CI failures, or get test results for a PR number. Covers ADO pipeline results and Helix test work items.
---

# Diagnostics PR Build Results Lookup

Use this skill to find and analyze CI build results for pull requests in the dotnet/diagnostics repository.

## Pipeline Details

- **ADO Organization**: `dnceng-public`
- **ADO Project**: `public`
- **Pipeline Definition ID**: `25` (diagnostics-public-ci)
- **Pipeline URL**: https://dev.azure.com/dnceng-public/public/_build?definitionId=25
- **Helix API**: https://helix.dot.net/api

## Step 1: Find the ADO Build for a PR

### Option A: From GitHub (preferred)

Use the GitHub MCP server to get check runs for the PR:

1. Use `github-mcp-server-pull_request_read` with `method: "get_status"` on `owner: "dotnet"`, `repo: "diagnostics"`, `pullNumber: <PR_NUMBER>` to get the commit status and check runs.
2. Look for the check run from Azure Pipelines — it will have a `target_url` pointing to `dev.azure.com/dnceng-public/public/_build/results?buildId=<BUILD_ID>`.
3. Extract the `buildId` from the URL.

### Option B: From ADO directly

Query ADO for builds triggered by the PR branch:

```
GET https://dev.azure.com/dnceng-public/public/_apis/build/builds?definitions=25&branchName=refs/pull/<PR_NUMBER>/merge&$top=5&api-version=7.0
```

Use the ADO PAT for authentication (Basic auth with `:PAT` base64-encoded).

## Step 2: Get Build Results from ADO

Once you have the `buildId`:

1. **Get the build timeline** to see all jobs and their status:
   ```
   GET https://dev.azure.com/dnceng-public/public/_apis/build/builds/<BUILD_ID>/timeline?api-version=7.0
   ```

2. **Find "Send Tests to Helix" tasks** in the timeline records:
   - Filter for records where `name` contains "Send Tests to Helix"
   - Check `result` (succeeded/failed) and `state` (completed)
   - Each has a parent record chain: Stage > Phase > Job > Task

3. **Get Helix Job IDs** from the task logs:
   - Get the log URL from `record.log.url`
   - Fetch the log content and search for `Sent Helix Job.*jobs/([a-f0-9-]+)` to extract Helix job IDs

4. **Map jobs to build configurations** by traversing the parent chain in the timeline:
   - Task → Job → Phase (has config name like `MacOS_arm64_Debug`) → Stage

## Step 3: Query Helix for Test Results

With the Helix job ID and access token:

1. **List work items**:
   ```
   GET https://helix.dot.net/api/jobs/<JOB_ID>/workitems?api-version=2019-06-17&access_token=<TOKEN>
   ```

2. **Get work item details** (for failed items):
   ```
   GET https://helix.dot.net/api/jobs/<JOB_ID>/workitems/<WORK_ITEM_NAME>?api-version=2019-06-17&access_token=<TOKEN>
   ```
   Returns `State` (Failed/Passed), `ExitCode`, `ConsoleOutputUri`, `Files`.

3. **Get console log** for a failed work item:
   ```
   GET <ConsoleOutputUri>&access_token=<TOKEN>
   ```
   Search for `FAIL`, `Error Message:`, `Assert`, `Exception` lines.

## Step 4: Summarize Results

Present results in a table format grouped by platform:

```
| Platform          | Config  | Test Suite        | Result | Details           |
|-------------------|---------|-------------------|--------|-------------------|
| Linux_x64         | Debug   | EventPipe         | PASS   |                   |
| Linux_x64         | Debug   | SOS.UnitTests     | FAIL   | LLDB 14 broken    |
```

## Authentication

### ADO PAT
Use Basic authentication with the PAT. The user should provide the PAT, or it may be in the session context.
```powershell
$b64 = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes(":$pat"))
$hdrs = @{ Authorization = "Basic $b64" }
Invoke-RestMethod -Uri $url -Headers $hdrs
```

### Helix Access Token
Append `&access_token=<TOKEN>` to Helix API URLs. The user should provide the token, or it may be in the session context.

## Common Test Suites

These are the Helix work items sent by the diagnostics pipeline:

| Work Item | Description |
|-----------|-------------|
| EventPipe.UnitTests | EventPipe functionality |
| Microsoft.Diagnostics.Monitoring.UnitTests | Monitoring library |
| Microsoft.FileFormats.UnitTests | File format parsing |
| Microsoft.SymbolStore.UnitTests | Symbol store |
| DotnetCounters.UnitTests | dotnet-counters tool |
| DotnetTrace.UnitTests | dotnet-trace tool |
| Microsoft.Diagnostics.NETCore.Client.UnitTests | Diagnostics client library |
| Microsoft.Diagnostics.Monitoring.EventPipe.UnitTests | EventPipe monitoring |
| SOS.UnitTests | SOS debugger extension (uses LLDB/cdb) |
| DbgShim.UnitTests | Debug shim (native dependencies) |

## Build Configurations

The pipeline runs these configurations (defined in `diagnostics.yml`):

| OS | Architecture | Config | Helix Queue |
|----|-------------|--------|-------------|
| Windows_NT | x64 | Debug/Release | Windows.Amd64.Server2022.Open |
| Windows_NT | x86 | Release | Windows.Amd64.Server2022.Open |
| linux | x64 | Debug/Release | Ubuntu.2204.Amd64.Open |
| linux (musl) | x64 | Debug/Release | Ubuntu.2204.Amd64.Open |
| osx | x64 | Debug/Release | OSX.15.Amd64.Open |
| osx | arm64 | Debug/Release | OSX.15.Arm64.Open |

## Quick Reference Script

Here's a PowerShell script to quickly get all Helix results for a build:

```powershell
$pat = "<ADO_PAT>"
$helixToken = "<HELIX_TOKEN>"
$buildId = "<BUILD_ID>"

$b64 = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes(":$pat"))
$hdrs = @{ Authorization = "Basic $b64" }

# Get timeline
$timeline = Invoke-RestMethod -Uri "https://dev.azure.com/dnceng-public/public/_apis/build/builds/$buildId/timeline?api-version=7.0" -Headers $hdrs

# Find Helix tasks and extract job IDs
$helixTasks = $timeline.records | Where-Object { $_.name -like "*Send Tests to Helix*" -and $_.state -eq 'completed' }
foreach ($ht in $helixTasks) {
    $parent = $timeline.records | Where-Object { $_.id -eq $ht.parentId }
    $grandparent = $timeline.records | Where-Object { $_.id -eq $parent.parentId }
    $logContent = Invoke-RestMethod -Uri $ht.log.url -Headers $hdrs
    if ($logContent -match 'jobs/([a-f0-9-]+)') {
        $jobId = $Matches[1]
        Write-Host "$($grandparent.name): $($ht.result) | jobId=$jobId"
        # Get work items
        $items = Invoke-RestMethod "https://helix.dot.net/api/jobs/$jobId/workitems?api-version=2019-06-17&access_token=$helixToken"
        # ... process items
    }
}
```
