#!/bin/bash

echo $USER@`hostname` "$PWD"
echo [`date`] $ dotnet run -p dotnet-trace.csproj -c Debug --no-restore --no-build -- "$@"
dotnet run -p dotnet-trace.csproj -c Debug --no-restore --no-build -- "$@"

