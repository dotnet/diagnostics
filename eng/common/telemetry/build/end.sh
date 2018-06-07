source="${BASH_SOURCE[0]}"
# resolve $source until the file is no longer a symlink
while [[ -h "$source" ]]; do
  scriptroot="$( cd -P "$( dirname "$source" )" && pwd )"
  source="$(readlink "$source")"
  # if $source was a relative symlink, we need to resolve it relative to the path where the
  # symlink file was located
  [[ $source != /* ]] && source="$scriptroot/$source"
done
scriptroot="$( cd -P "$( dirname "$source" )" && pwd )"

if [ "$AGENT_JOBSTATUS" = "Succeeded" ] || [ "$AGENT_JOBSTATUS" = "PartiallySucceeded" ]; then
  errorCount=0
else
  errorCount=1
fi
warningCount=0

curlResult=`
/bin/bash $scriptroot/../curl.sh \
  -H 'Content-Type: application/json' \
  -H "X-Helix-Job-Token: $Helix_JobToken" \
  -H 'Content-Length: 0' \
  -X POST -G "https://helix.dot.net/api/2018-03-14/telemetry/job/build/$Helix_WorkItemId/finish" \
  --data-urlencode "errorCount=$errorCount" \
  --data-urlencode "warningCount=$warningCount"
`
curlStatus=$?

if [ $curlStatus -ne 0 ]; then
  echo "Failed to Send Build Finish information"
  echo $curlResult
  if /bin/bash "$scriptroot/../../is-vsts.sh"; then
    echo "##vso[task.logissue type=error;sourcepath=telemetry/build/end.sh;code=1;]Failed to Send Build Finish information: $curlResult"
  fi
  exit 1
fi

exit 0
