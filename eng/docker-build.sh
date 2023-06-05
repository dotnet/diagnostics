#!/usr/bin/env bash
# Copyright (c) .NET Foundation and contributors. All rights reserved.
# Licensed under the MIT license. See LICENSE file in the project root for full license information.

source_directory=
docker_image=
docker_container_name=

while [ $# -ne 0 ]; do
    name=$1
    case $name in
        -s|--source-directory)
            shift
            source_directory=$1
            ;;
        -i|--docker-image)
            shift
            docker_image=$1
            ;;
        -c|--container-name)
            shift
            docker_container_name=$1
            ;;
        *)
            args="$args $1"
            ;;
    esac
    shift
done

echo "Initialize Docker Container"
if command -v docker > /dev/null; then
    docker_bin=$(command -v docker)
else
    echo "Unable to find docker"
    exit 1
fi

$docker_bin --version

# Get user id
user_name=$(whoami)
echo "user name: $user_name"
user_id=$(id -u $user_name)
echo "user id: $user_id"

# Download image
$docker_bin pull $docker_image

# Create local network to avoid port conflicts when multiple agents run on same machine
$docker_bin network create vsts_network_$docker_container_name

# Create and start container
docker_id="$($docker_bin create -it --rm --security-opt seccomp=unconfined --ulimit core=-1 \
  --name vsts_container_$docker_container_name \
  --network=vsts_network_$docker_container_name \
  --volume $source_directory:$source_directory \
  --workdir=$source_directory $docker_image bash --verbose)"
$docker_bin start $docker_id

# Create an user with the same uid in the container
container_user_name=vsts_$(echo $user_name | awk '{print tolower($0)}')
echo "container user name: $container_user_name"

# Add sudo user with same uid that can run any sudo command without password
$docker_bin exec $docker_id useradd -K MAIL_DIR=/dev/null -m -u $user_id $container_user_name
$docker_bin exec $docker_id groupadd sudouser
$docker_bin exec $docker_id usermod -a -G sudouser $container_user_name
$docker_bin exec $docker_id su -c "echo '%sudouser ALL=(ALL:ALL) NOPASSWD:ALL' >> /etc/sudoers"

echo "Execute $args"
$docker_bin exec --workdir=$source_directory --user $container_user_name $docker_id $args
lasterrorcode=$?

echo "Cleanup Docker Container/Network"
$docker_bin container stop $docker_id
$docker_bin network rm vsts_network_$docker_container_name

exit $lasterrorcode
