#!/bin/bash

# Diagnostics Solution Generator for Unix/macOS
#
# SYNOPSIS:
#     Generates Visual Studio solution files for the diagnostics repository.
#
# DESCRIPTION:
#     This script generates solution files using SlnGen tool
#
# USAGE:
#     ./generate-sln.sh

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

    # Generate solution files with SlnGen in .sln format
    write_step "Generating solution files with SlnGen..."
    if "$DOTNET" tool exec Microsoft.VisualStudio.SlnGen.Tool --collapsefolders true --folders true --launch false; then
        echo ""
        write_success "Solution generation completed successfully!"
        echo -e "${GRAY}You can now open the generated .sln files in Visual Studio, VS Code, or any compatible IDE.${NC}"
    else
        write_error "Failed to generate solution files with SlnGen"
        echo "Make sure the SlnGen tool is installed:"
        echo "  $DOTNET tool install --global Microsoft.VisualStudio.SlnGen.Tool"
        exit 1
    fi
}

# Run main function
main "$@"
