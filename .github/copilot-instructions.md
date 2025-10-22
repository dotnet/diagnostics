**Any code you commit SHOULD compile, and new and existing tests related to the change SHOULD pass.**

You MUST make your best effort to ensure your changes satisfy those criteria before committing. If for any reason you were unable to build or test the changes, you MUST report that. You MUST NOT claim success unless all builds and tests pass as described above.

You MUST follow all code-formatting and naming conventions defined in [`.editorconfig`](/.editorconfig).

In addition to the rules enforced by `.editorconfig`, you SHOULD:

- Prefer file-scoped namespace declarations and single-line using directives.
- Ensure that the final return statement of a method is on its own line.
- Use pattern matching and switch expressions wherever possible.
- Use `nameof` instead of string literals when referring to member names.
- Always use `is null` or `is not null` instead of `== null` or `!= null`.
- Trust the C# null annotations and don't add null checks when the type system says a value cannot be null.
- Prefer `?.` if applicable (e.g. `scope?.Dispose()`).
- When adding new unit tests, strongly prefer to add them to existing test code files rather than creating new code files.
- Do not finish work with any tests commented out or disabled that were not previously commented out or disabled.
- When writing tests, do not emit "Act", "Arrange" or "Assert" comments.

---

# Building & Testing in dotnet/diagnostics

Table of contents
- [Building & Testing in dotnet/diagnostics](#building--testing-in-dotnetdiagnostics)
  - [1. Prerequisites](#1-prerequisites)
    - [1.1. Determine Affected Components](#11-determine-affected-components)
    - [1.2. Baseline Setup](#12-baseline-setup)
  - [2. Iterative Build and Test Strategy](#2-iterative-build-and-test-strategy)
    - [2.1. Success Criteria](#21-success-criteria)
  - [3. Tools Workflow](#3-tools-workflow)
  - [4. Documentation Workflow](#4-documentation-workflow)
  - [5. Other changes workflow](#5-other-changes-workflow)
  - [6. Additional Notes](#6-additional-notes)
    - [6.1. Windows Command Equivalents](#61-windows-command-equivalents)
  - [7. References](#7-references)

## 1. Prerequisites

These steps need to be done **before** applying any changes.

### 1.1. Determine Affected Components

Identify which components will be impacted by the changes. If in doubt, analyze the paths of the files to be updated:

- **Tools:** Changes in `src/Tools/`.
- **Documentation:** Changes in `documentation/` and markdown files.
- **Other** changes in `src/` that don't fall in prior categories.

### 1.2. Baseline Setup

Before applying any changes, ensure you have a full successful build as a baseline.

1. Checkout `main` branch

2. From the repository root, run the build using `./build.sh -test`

3. Verify the build completed without error.
    - _If the baseline build failed, report the failure and don't proceed with the changes._

4. From the repository root:
    - Configure PATH: `export PATH="$(pwd)/.dotnet:$PATH"`
    - Verify SDK Version: `dotnet --version` should match `sdk.version` in `global.json`.

5. Switch back to the working branch.

---

## 2. Iterative Build and Test Strategy

1. Apply the intended changes

2. **Attempt Build.** If the build fails, attempt to fix and retry the step (up to 5 attempts).

3. **Attempt Test.**
    - If a test _build_ fails, attempt to fix and retry the step (up to 5 attempts).
    - If a test _run_ fails,
        - Determine if the problem is in the test or in the source
        - If the problem is in the test, attempt to fix and retry the step (up to 5 attempts).
        - If the problem is in the source, reconsider the full changeset, attempt to fix and repeat the workflow.

4. **Workflow Iteration:**
    - Repeat build and test up to 5 cycles.
    - If issues persist after 5 workflow cycles, report failure.
    - If the same error persists after each fix attempt, do not repeat the same fix. Instead, escalate or report with full logs.

When retrying, attempt different fixes and adjust based on the build/test results.

### 2.1. Success Criteria

- **Build:**
    - Completes without errors.
    - Any non-zero exit code from build commands is considered a failure.

- **Tests:**
    - All tests must pass (zero failures).
    - Any non-zero exit code from test commands is considered a failure.

- **Workflow:**
    - On success: Report completion
    - Otherwise: Report error(s) with logs for diagnostics.
        - Collect logs from `artifacts/log/` and the console output for both build and test steps.
        - Attach relevant log files or error snippets when reporting failures.

---

## 3. Tools Workflow

From the repository root:

- Build:
  `./build.sh`

- Run tests:
  `cd src/tests && dotnet test dirs.proj`

---

## 4. Documentation Workflow

Skip build and test.

---

## 5. Other changes workflow

From the repository root do a full build and test:

- Build and test:
  `./build.sh -test`

___

## 6. Additional Notes


### 6.1. Windows Command Equivalents

- Use `build.cmd` instead of `build.sh` on Windows.
- Set PATH: `set PATH=%CD%\.dotnet;%PATH%`
- All other commands are similar unless otherwise noted.

---

### 7. References

- [`.editorconfig`](/.editorconfig)
