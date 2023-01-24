# See here for image contents: https://github.com/microsoft/vscode-dev-containers/tree/v0.192.0/containers/dotnet/.devcontainer/base.Dockerfile
# For specifics about the dotnet base container see: https://github.com/microsoft/vscode-dev-containers/tree/main/containers/dotnet

# [Choice] .NET version: 6.0, 5.0, 3.1, 2.1
ARG VARIANT="6.0-focal"
FROM mcr.microsoft.com/vscode/devcontainers/dotnet:0-${VARIANT}

# Set up machine requirements to build the repo
RUN apt-get update && export DEBIAN_FRONTEND=noninteractive \
    && apt-get -y install --no-install-recommends cmake clang \
    curl gdb gettext git libicu-dev lldb liblldb-dev libunwind8 \
    llvm make python python-lldb tar wget zip 