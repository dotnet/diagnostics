# Mirrored shared runtime source

The `runtime` directory contains the dotnet/runtime source needed by the
diagnostics PAL and minipal builds. Diagnostics-owned compatibility remains
outside the mirror under `palextensions`.

`runtime-source.txt` records the exact runtime commit used for the current
copy. Run `update-source.ps1` with a runtime checkout to refresh the mirror.
