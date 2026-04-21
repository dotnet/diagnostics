---
name: release-notes
description: Generate release notes for the dotnet/diagnostics repository. Use when asked to generate release notes, a release summary, or a changelog.
---

# Release Notes Generation

## Process

### 1. Determine the baseline

Find the **last published GitHub release** — not the latest git tag. This repo may have many version tags (e.g., `v10.0.*`, `v11.0.*`) that don't correspond to published releases.

```bash
# Query the GitHub releases API for the last published release
# Use the github-mcp-server or the GitHub API directly
```

The last published release may be on a different branch (e.g., `release/9.0`). Even so, use the tag directly in `git log` — **do NOT use `git merge-base`**. The release branch often contains snap/backport merges that bring `main` commits into the release, so `merge-base` returns an ancestor that is already included in the release tag. Instead:

```bash
# Correct: use the tag directly — git handles cross-branch reachability
git log <release-tag>..HEAD --oneline --first-parent
```

This ensures commits already reachable from the release tag (via snap merges or backports) are excluded.

### 2. Get the commit list

List all first-parent commits from the release tag to HEAD, excluding dependency flow PRs:

```bash
git log <release-tag>..HEAD --oneline --first-parent
```

Filter out:
- `[main] Update dependencies from *`
- `Update dependencies from *`
- Pure test infrastructure changes (test reorganization, CI image updates, SDK bumps)
- Build/CI plumbing (SDK updates, pipeline config, dependabot changes)

### 3. Investigate each PR

For each user-facing PR, launch **parallel explore agents** (one per group of 3-5 related PRs) to read:
- PR body (`get` method)
- Review comments (`get_review_comments` method)
- General comments (`get_comments` method)

Focus on:
- **What** user-visible behavior changed
- **Why** — what issue it solves, what feature it adds
- **Breaking changes** or UX surprises discussed in review threads
- Concrete examples (before/after output, error messages)

### 4. Write the release notes

Follow the format used in previous releases (see `v9.0.661903` for reference). Structure:

```markdown
### General announcements

Brief summary.

### dotnet-dump / SOS

- **Feature/fix name**: Description focusing on WHY and user impact. (#PR)

#### SOS Bug Fixes

- **Command affected**: What was wrong, what's fixed. (#PR)

### dotnet-trace

- **collect-linux improvements**: Group related sub-items.

### dotnet-counters

- **Feature**: Description. (#PR)

### Microsoft.Diagnostics.NETCore.Client

- **Feature**: Description. (#PR)

### Other notable changes

- Items that don't fit above categories.
```

### 5. Style guidelines

- Focus on **why** a change matters to users, not implementation details
- Include issue references where available: `([#1234](https://github.com/dotnet/diagnostics/issues/1234), #5678)`
- Group related fixes (e.g., all `collect-linux` terminal fixes together)
- Call out breaking changes or UX changes explicitly
- Omit purely internal changes (refactors, test moves, CI config) unless they have user impact
- Use bold for command names and flags: **`!dumpheap -stat -bycount`**

## Example output

See the release at https://github.com/dotnet/diagnostics/releases/tag/v9.0.661903 for format reference.
