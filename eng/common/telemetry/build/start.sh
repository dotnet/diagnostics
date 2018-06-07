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

while (($# > 0)); do
  lowerI="$(echo $1 | awk '{print tolower($0)}')"
  case $lowerI in
    --build-uri)
      buildUri=$2
      shift 2
      ;;
    *)
      echo "Unknown Arg '$1'"
      exit 1
      ;;
  esac
done


curlResult=`
/bin/bash $scriptroot/../curl.sh \
  -H 'Content-Type: application/json' \
  -H "X-Helix-Job-Token: $Helix_JobToken" \
  -H 'Content-Length: 0' \
  -X POST -G "https://helix.dot.net/api/2018-03-14/telemetry/job/build" \
  --data-urlencode "buildUri=$buildUri"
`
curlStatus=$?

if [ $curlStatus -ne 0 ]; then
  echo "Failed to Send Build Start information"
  echo $curlResult
  if /bin/bash "$scriptroot/../../is-vsts.sh"; then
    echo "##vso[task.logissue type=error;sourcepath=telemetry/build/start.sh;code=1;]Failed to Send Build Start information: $curlResult"
  fi
  exit 1
fi

export Helix_WorkItemId=`echo $curlResult | xargs echo` # Strip Quotes

if /bin/bash "$scriptroot/../../is-vsts.sh"; then
  echo "##vso[task.setvariable variable=Helix_WorkItemId]$Helix_WorkItemId"
else
  echo "export Helix_WorkItemId=$Helix_WorkItemId"
fi
