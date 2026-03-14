#!/bin/bash

dotnet restore dotnet-trace.csproj
dotnet build dotnet-trace.csproj -c Debug --no-restore
dotnet build dotnet-trace.csproj -c Release --no-restore
