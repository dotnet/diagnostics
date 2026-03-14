# Agent Guidelines for .NET Diagnostics Repository

This document provides standard guidance for AI agents and automated tools working on the .NET Diagnostics repository. Following these guidelines will help ensure consistency and quality across contributions.

## Repository Overview

The .NET Diagnostics repository contains diagnostic tools and libraries for .NET Core, including:

- **SOS**: The Son of Strike debugger extension
- **dotnet-dump**: Dump collection and analysis utility
- **dotnet-gcdump**: Heap analysis tool
- **dotnet-trace**: Event collection tool
- **dotnet-counters**: Performance counter monitoring tool
- **Diagnostic libraries**: Client libraries and services for diagnostics

## Build System

### Building the Repository

The repository uses a cross-platform build system:

**Linux/macOS:**
```bash
./build.sh
```

**Windows:**
```cmd
Build.cmd
```

### Build Scripts Location
- Main build scripts: `build.sh` / `Build.cmd` at repository root
- Actual build logic: `eng/build.sh` / `eng/Build.cmd`
- Build configuration: `build.proj`, `Directory.Build.props`, `Directory.Build.targets`

### Common Build Options
- `-configuration <Debug|Release>`: Build configuration (default: Debug)
- `-architecture <x64|x86|arm|arm64>`: Target architecture
- `-restore`: Restore dependencies before building
- `-build`: Build the repository (incremental, only changed projects)
- `-rebuild`: Clean and rebuild (required after changing .props/.targets files)
- `-bl`: Requests a binlog.
- `-test`: Run tests after building

### Build Output Locations

Understanding where build outputs are placed is essential for verification and debugging:

- **Managed Build outputs**: `artifacts/bin/<ProjectName>/<Configuration>/<TargetFramework>/`
- **SOS Build outputs**: `artifacts/bin/<OS>.<Architecture>.<Configuration>`
- **Test results when using global test script**: `artifacts/TestResults/`
- **Build logs**: `artifacts/log/` (including `Build.binlog` for detailed analysis)
- **NuGet packages**: `artifacts/packages/<Configuration>/`
- **Temporary files**: `artifacts/tmp/`
- **Intermediate files**: `artifacts/obj/` (such as obj files, generated files, etc.)

### Quick Build Commands

After a full build of the repo has been done, some commands can be used to iterate faster on changes:

### For changes under src/Tools:

```bash
# Build the relevant tool
dotnet build src/Tools/dotnet-dump/dotnet-dump.csproj

# Build without restoring (faster if dependencies haven't changed)
dotnet build --no-restore
```

### For changes under to native files:

```bash
# Build the native components to verify compilation works
./build.sh -skipmanaged

# Do a full test run:
./build -test
```

## Testing

### Running All Tests

**Linux/macOS:**
```bash
./test.sh
```

**Windows:**
```cmd
Test.cmd
```

The test script runs all tests in the repository. **Important**: `test.sh` calls `eng/build.sh -test -skipmanaged -skipnative`, which means it only runs tests without rebuilding. Always build first if you've made code changes.

### Test Organization

Test projects are usually located in `src/tests/` with the following structure:

- **Tool and libraries tests**: `*.UnitTests.csproj` or `*.Tests.csproj` under the appropriate tool's folder in `src/tests`.
- Changes with native dependencies (SOS, DBGShim, dotnet-sos, dotnet-dump) are better tested with the global test script.

### Running Specific Tests

```bash
# Run tests for a specific project
dotnet test src/tests/Microsoft.Diagnostics.DebugServices.UnitTests/

# Run tests with detailed output
dotnet test --logger "console;verbosity=detailed"

# Run a specific test by name
dotnet test --filter "FullyQualifiedName~TestMethodName"
```

## Project Structure

```
/src
├── Microsoft.Diagnostics.DebugServices        # Debug service interfaces
├── Microsoft.Diagnostics.DebugServices.Implementation  # Debug service implementations
├── Microsoft.Diagnostics.ExtensionCommands    # SOS extension commands
├── Microsoft.Diagnostics.Monitoring           # Monitoring libraries
├── Microsoft.Diagnostics.NETCore.Client       # Diagnostic client library
├── Microsoft.Diagnostics.Repl                 # REPL infrastructure
├── Microsoft.FileFormats                      # File format parsers
├── Microsoft.SymbolStore                      # Symbol store implementation
├── SOS                                        # SOS debugger extension
├── Tools                                      # Command-line tools (dump, trace, counters, gcdump)
├── tests                                      # Test projects
└── shared                                     # Shared native code

/documentation                                  # Documentation files
/eng                                           # Engineering/build infrastructure
```

## Coding Standards

### C# Code Style

The repository follows standard .NET coding conventions defined in the `.editorconfig` file at the root. This is a **must** for C# code. Ensure your changes conform to these settings:

1. **Indentation**: 4 spaces (no tabs)
2. **Line endings**: LF on Linux/macOS, CRLF on Windows (EditorConfig enforces this)
3. **Braces**: Opening braces on new lines for types, methods, properties, control blocks
4. **Naming conventions** (strictly enforced):
   - PascalCase for types, methods, properties, public fields, constants
   - camelCase for local variables and parameters
   - `_camelCase` for private/internal instance fields (underscore prefix)
   - `s_camelCase` for static private/internal fields (s_ prefix)
5. **File headers**: All C# files must include the MIT license header:
   ```csharp
   // Licensed to the .NET Foundation under one or more agreements.
   // The .NET Foundation licenses this file to you under the MIT license.
   ```
6. **Using directives**: Must be placed **outside** the namespace declaration
7. **Var keyword**: Avoid using `var` - use explicit types (configured as error when type is apparent)
8. **Additional rules**:
   - Trim trailing whitespace
   - Insert final newline
   - Prefer braces even for single-line blocks

### Native Code (C/C++)

Native code follows similar conventions:
- 4 spaces for indentation
- Braces on same line for control structures in C++
- Use clear, descriptive names
- Follow existing patterns in the codebase

### Platform-Specific Line Endings

**Critical**: Line endings must match the platform to avoid breaking scripts:
- Shell scripts (`.sh`): **LF only** (will break on Linux/macOS if CRLF)
- Batch files (`.cmd`, `.bat`): **CRLF only**
- C# files: LF on Linux/macOS, CRLF on Windows
- The `.editorconfig` file enforces these rules automatically

## Making Changes

### General Guidelines

1. **Surgical changes**: Make the smallest possible changes to address the issue. Focus on solving a single problem without addressing unrelated concerns. Balance minimal code changes with preserving overall code clarity and simplicity
2. **Preserve existing behavior**: Don't break working functionality unless explicitly required
3. **Follow existing patterns**: Match the style and structure of surrounding code
4. **Test thoroughly**: Run builds and tests to verify changes
5. **Update documentation**: If making significant changes, update relevant documentation

### Before Making Changes

1. Run a clean build to ensure the current state is valid:
   ```bash
   ./build.sh  # or Build.cmd on Windows
   ```

2. Run tests to understand the baseline:
   ```bash
   ./test.sh  # or Test.cmd on Windows
   ```

3. Understand the area you're working on:
   - Read related source files
   - Review existing tests
   - Check documentation in `/documentation`

### After Making Changes

1. **Build**: Ensure your changes compile without errors or new warnings
   ```bash
   ./build.sh  # or Build.cmd on Windows
   ```
2. **Test**: Run relevant tests to verify functionality
   ```bash
   ./test.sh  # or Test.cmd on Windows
   ```
3. **Code style**: Verify changes match the repository's coding standards
   - Check file headers (.NET Foundation header)
   - Verify naming conventions (especially field prefixes)
   - Ensure using directives are outside namespace
   - Confirm line endings are correct for file type
4. **Documentation**: Update if your changes affect public APIs or behavior

### Investigating Build or Test Failures

When builds or tests fail, follow this diagnostic process:

1. **Check build logs**: Look at `artifacts/log/Build.binlog` using the Binary Log Viewer
2. **Review terminal output**: MSBuild errors will show the failing project and error code
3. **Check test results**: Detailed test logs are in `artifacts/TestResults/`
4. **For native builds**: Review CMake output for missing dependencies or configuration issues
5. **Clean and rebuild**: Sometimes required after changing build configuration files:
   ```bash
   ./build.sh -rebuild
   ```

## Development Workflow

### Typical Workflow

1. **Create a branch**: Create a feature branch from `main` with a descriptive name
2. **Make changes**: Implement your changes following the coding standards
3. **Build locally**: Run `./build.sh` (or `Build.cmd` on Windows) to ensure the code compiles
4. **Run tests**: Execute `./test.sh` (or `Test.cmd` on Windows) to verify functionality
5. **Commit changes**: Make small, logical commits with clear commit messages
6. **Push to remote**: Push your branch to the remote repository
7. **Create pull request**: Open a PR with a clear description of your changes
8. **Address feedback**: Respond to review comments and make necessary updates
9. **Merge**: Once approved, the PR will be merged to `main`

### Iterative Development

- Make small, incremental changes rather than large, sweeping modifications
- Test frequently to catch issues early
- Commit logical units of work separately
- Keep the build and tests passing at each commit when possible

### Pull Request Guidelines

- **Title**: Use a clear, descriptive title that summarizes the change
- **Description**: Explain what changed, why it changed, and how to test it
- **Link issues**: Reference related issues using "Fixes #1234" or "Addresses #1234"
- **Keep focused**: Each PR should address a single concern or feature
- **Respond promptly**: Address review feedback in a timely manner

## Common Patterns

### Solution Files

The main solution file is `build.sln` at the root. **Important**: This file is auto-generated from `build.proj`.

- **Do NOT manually edit** `build.sln`
- Regenerate after adding/removing projects:
  - Linux/macOS: `./eng/generate-sln.sh`
  - Windows: `.\eng\generate-sln.ps1`
- The solution is regenerated automatically during builds when needed

### Dependency Management

**NuGet Package Versions**:

The repository uses a centralized version management system:

- **`eng/Versions.props`**: Defines all NuGet package versions for the repo
  - **Always update this file** when changing package versions
  - Use the `nuget` MCP tool to query and resolve version conflicts
- **`eng/Version.Details.props`**: Auto-generated by Arcade/Darc
  - **Never edit directly** - requires modifying Version.Details.xml.
- **`eng/Version.Details.xml`**: Dependency tracking metadata
  - **Never edit directly** - requires metadata not available to agents.

**Package Source Configuration**:

- Never modify `NuGet.config` to add a source feed unless explicitly requested
- **Never** add `nuget.org` as a source to `NuGet.config`
- Use the `nuget` MCP tool for querying packages and resolving conflicts

**Project References**:

- Use relative paths in `.csproj` files when adding dependencies between projects
  Example: `<ProjectReference Include="..\Microsoft.Diagnostics.DebugServices\Microsoft.Diagnostics.DebugServices.csproj" />`

**Native Dependencies**:

- Handled through machine-wide installation or container installation
- See `documentation/building/` for platform-specific prerequisites

### Platform-Specific Code

The repository supports multiple platforms (Windows, Linux, macOS, FreeBSD, NetBSD):
- Use conditional compilation (`#if`, `#ifdef`) for platform-specific code
- Leverage .NET's platform detection APIs
- Keep platform-specific code minimal and isolated

## Debugging and Diagnostics

### Loading in IDEs

**Visual Studio Code:**
- Open the repository root folder
- Load `build.sln` for better IntelliSense
- Use the provided launch configurations in `.vscode`

**Visual Studio:**
```cmd
start-vs.cmd
```

### Common Issues

1. **Build failures**:
   - Ensure all prerequisites are installed (see `documentation/building/`)
   - Check `artifacts/log/Build.binlog` for detailed error information
2. **Test failures**:
   - Some tests require specific runtime versions or configurations
   - Check test output in `artifacts/TestResults/` if using global test script
   - Verify you built before testing (test scripts don't rebuild)
3. **Native component issues**:
   - Check terminal output for cl/clang/cmake error output.
4. **Line ending issues**:
   - Shell scripts fail on Linux/macOS: Check for CRLF line endings
   - Ensure `.editorconfig` settings are being respected by your editor

## Dependencies and Security

### Adding Dependencies

1. **NuGet packages**: Add to `eng/Versions.props` with appropriate version and never modify `NuGet.config` to add a source feed unless asked to do so specifically. Particularly, *never* add `nuget.org` as a source to `NuGet.config`. Use the `nuget` MCP as needed to query and solve for assembly version conflicts.
2. **Security**: Be mindful of security implications when adding dependencies
3. **Licensing**: Ensure new dependencies are compatible with MIT license
4. **Minimize dependencies**: Only add when necessary

### Security Considerations

- Never commit secrets or credentials
- Follow secure coding practices (input validation, proper error handling)
- Be cautious with native interop and memory management
- Review security implications of changes

## Testing Philosophy

1. **Write tests for new functionality**: New features should include tests
2. **Don't remove existing tests**: Tests verify important functionality
3. **Fix test failures related to your changes**: Don't ignore failing tests
4. **Maintain test quality**: Tests should be clear, focused, and maintainable

## Documentation

### When to Update Documentation

- Adding new tools or features
- Changing public APIs
- Modifying build or test procedures
- Adding new dependencies or requirements

### Documentation Locations

- Tool documentation: `/documentation/*-instructions.md`
- Building instructions: `/documentation/building/`
- Design documents: `/documentation/design-docs/`
- README: `/README.md`

## Git and Version Control

### Branch Strategy

- Main development: `main` branch
- Feature branches: Use descriptive names
- Pull requests: Required for all changes

### Commit Messages

- First line: Brief summary (50 characters or less)
- Blank line, then detailed description if needed
- Reference issues: "Fixes #1234" or "Addresses #1234"

## Resources

- [Building on Linux](documentation/building/linux-instructions.md)
- [Building on Windows](documentation/building/windows-instructions.md)
- [Building on macOS](documentation/building/osx-instructions.md)
- [FAQ](documentation/FAQ.md)

## Questions and Support

If you encounter issues or have questions:

1. Check existing documentation in `/documentation`
2. Review closed issues and pull requests for similar problems
3. Consult the [FAQ](documentation/FAQ.md)
4. Ask in the issue or pull request you're working on
