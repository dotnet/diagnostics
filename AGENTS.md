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
- `-build`: Build the repository
- `-rebuild`: Clean and rebuild
- `-test`: Run tests after building

## Testing

### Running Tests

**Linux/macOS:**
```bash
./test.sh
```

**Windows:**
```cmd
Test.cmd
```

The test script runs all tests in the repository. Test projects are located in the `src/tests` directory.

### Test Organization
- Unit tests are typically in `*.Tests` projects
- Integration tests may be in separate test projects
- Test helpers are in `Microsoft.Diagnostics.TestHelpers`

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
2. **Line endings**: LF on Linux/macOS, CRLF on Windows
3. **Braces**: Opening braces on new lines for types, methods, properties, control blocks
4. **Naming**:
   - PascalCase for types, methods, properties, public fields
   - camelCase for local variables, parameters, private fields
   - `_camelCase` for private instance fields (with underscore prefix)
5. **File organization**: One type per file, filename matches type name
6. **Additional EditorConfig rules**:
   - Trim trailing whitespace
   - Insert final newline
   - Follow C# new line and indentation preferences

### Native Code (C/C++)

Native code follows similar conventions:
- 4 spaces for indentation
- Braces on same line for control structures in C++
- Use clear, descriptive names
- Follow existing patterns in the codebase

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
2. **Test**: Run relevant tests to verify functionality
3. **Code style**: Verify changes match the repository's coding standards
4. **Documentation**: Update if your changes affect public APIs or behavior

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

The main solution file is `build.sln` at the root. This file is generated from `build.proj` and can be regenerated using:
```bash
./eng/generate-sln.sh
```

### Dependency Management

- NuGet packages: `eng/Versions.props` defines package versions
- Project references: Use relative paths in `.csproj` files
- Native dependencies: Handled through CMake

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

1. **Build failures**: Ensure all prerequisites are installed (see documentation/building/)
2. **Test failures**: Some tests may require specific runtime versions or configurations
3. **Native component issues**: Check CMake output for missing dependencies

## Dependencies and Security

### Adding Dependencies

1. **NuGet packages**: Add to `eng/Versions.props` with appropriate version
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
