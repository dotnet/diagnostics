#!/usr/bin/env bash
project_root="$1"
plugin="$2"
host_version="$3"
test_program="$4"
results_dir="$5"

if [[ "$project_root" = "" || "$plugin" = "" || "$test_program" = "" || "$results_dir" = "" ]]; then
    echo "Project root, plugin or log directory required"
    exit 1
fi

host="$project_root/.dotnet/dotnet --fx-version $host_version"

# Turn on stress logging so the dumplog and histinit commands pass
export DOTNET_LogFacility=0xffffffbf
export DOTNET_LogLevel=6
export DOTNET_StressLog=1
export DOTNET_StressLogSize=65536

if [[ ! -x "$LLDB_PATH" ]]; then
    echo "LLDB_PATH doesn't exist or not executable"
    exit 1
fi

log_dir=$results_dir/lldbplugin.tests_$(date +%Y_%m_%d_%H_%M_%S)
mkdir -p $log_dir

cd $project_root/src/SOS/lldbplugin.tests/
rm -f StressLog.txt
python $project_root/src/SOS/lldbplugin.tests/test_libsosplugin.py --lldb $LLDB_PATH --host "$host" --plugin $plugin --logfiledir $log_dir --assembly $test_program
