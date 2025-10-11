#!/bin/bash

# Diagnostics Solution Generator for Unix/macOS
#
# SYNOPSIS:
#     Generates and migrates Visual Studio solution files for the diagnostics repository.
#
# DESCRIPTION:
#     This script generates solution files using SlnGen tool and migrates the legacy
#     build.sln file to the new .slnx format, then cleans up the old file.
#
# USAGE:
#     ./generate-slnx.sh

set -euo pipefail

# Get script directory and repository root
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
DOTNET="$REPO_ROOT/dotnet.sh"

# Color codes for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
MAGENTA='\033[0;35m'
GRAY='\033[0;37m'
NC='\033[0m' # No Color

# Logging functions
write_step() {
    echo -e "${CYAN}==> $1${NC}"
}

write_success() {
    echo -e "${GREEN}[SUCCESS] $1${NC}"
}

write_warning() {
    echo -e "${YELLOW}[WARNING] $1${NC}"
}

write_error() {
    echo -e "${RED}[ERROR] $1${NC}"
}

# Cleanup function for error handling
cleanup() {
    local exit_code=$?
    if [ $exit_code -ne 0 ]; then
        write_error "Script execution failed with exit code $exit_code"
    fi
    exit $exit_code
}

# Set up error handling
trap cleanup EXIT ERR

main() {
    echo -e "${MAGENTA}Diagnostics Solution Generator${NC}"
    echo -e "${MAGENTA}==============================${NC}"

    # Change to repository root
    if ! cd "$REPO_ROOT"; then
        write_error "Failed to change to repository root directory: $REPO_ROOT"
        exit 1
    fi

    # Check if dotnet script exists
    if [ ! -f "$DOTNET" ]; then
        write_error "Local dotnet script not found at: $DOTNET"
        echo "Make sure the dotnet.sh script exists in the repository root."
        exit 1
    fi

    # Make sure dotnet script is executable
    chmod +x "$DOTNET"

    # Step 1: Generate solution files with SlnGen
    write_step "Generating solution files with SlnGen..."
    if "$DOTNET" tool exec Microsoft.VisualStudio.SlnGen.Tool --collapsefolders true --folders true --launch false; then
        write_success "Solution files generated successfully"
    else
        write_error "Failed to generate solution files with SlnGen"
        exit 1
    fi

    # Step 2: Check if build.sln exists before migration
    if [ -f "build.sln" ]; then
        write_step "Migrating build.sln to new format..."
        if "$DOTNET" sln build.sln migrate; then
            write_success "Migration completed successfully"
        else
            write_error "Failed to migrate build.sln"
            write_warning "Continuing with cleanup..."
        fi

        # Step 3: Clean up old solution file
        write_step "Cleaning up old build.sln file..."
        if rm -f "build.sln"; then
            write_success "Old solution file removed"
        else
            write_warning "Could not remove build.sln"
            echo -e "${YELLOW}You may need to remove it manually${NC}"
        fi
    else
        write_warning "build.sln not found, skipping migration step"
    fi

    echo ""
    write_success "Solution generation and migration completed successfully!"
    echo -e "${GRAY}You can now open the generated build.slnx files in Visual Studio.${NC}"
}

# Run main function
main "$@"
