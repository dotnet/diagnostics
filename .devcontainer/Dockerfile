# See https://github.com/devcontainers/images/tree/main/src/dotnet/
ARG VARIANT="10.0-noble"
FROM mcr.microsoft.com/devcontainers/dotnet:${VARIANT}

RUN bash -i -c 'nvm install --lts' && npm install -g @github/copilot

RUN apt-get update && export DEBIAN_FRONTEND=noninteractive \
    && apt-get -y install --no-install-recommends \
        clang \
        cmake \
        cpio \
        build-essential \
        python3 \
        curl \
        git \
        lldb \
        llvm \
        liblldb-dev \
        libunwind8 \
        libunwind8-dev \
        liblttng-ust-dev \
        ninja-build \
    && apt-get clean \
    && rm -rf /var/lib/apt/lists/*
