#!/usr/bin/perl

#
# ./cleanup-docker.sh
#

printf "Cleaning up containers\n";
printf "----------------------\n";
my $psList = `docker ps -a`;
my @psItems = split /\n/, $psList;
foreach(@psItems) {
  # match 'docker ps' output to capture the container name
  if($_ =~ /.*\s+([^\s]+)$/ig) {
    my $containerName = $1;
    if($containerName !~ /NAME/ig) {
      printf "delete $containerName\n";
      my $deleteOutput = `docker rm -f $1`;
      print "$deleteOutput\n";
    }
  }
}

printf "Cleaning up volumes\n";
printf "-------------------\n";
my $volumeList = `docker volume ls`;
@volumeItems = split /\n/, $volumeList;
foreach(@volumeItems) {
  # match 'docker volume ls' output to capture the volume name
  if($_ =~ /([^\s]+)\s+([^\s]+)$/ig) {
    my $volumeName = $2;
    if($volumeName !~ /NAME/ig) {
      printf "delete $volumeName\n";
      my $deleteVolumeOutput = `docker volume rm -f $volumeName`;
      printf "$deleteVolumeOutput\n";
    }
  }
}

printf "Cleaning up images\n";
printf "------------------\n";
my $imageList = `docker images`;
@imageItems = split /\n/, $imageList;
foreach(@imageItems) {
  # match 'docker images' output to capture the image id
  if($_ =~ /([^\s]+)\s+([^\s]+)\s+([^\s]+)\s+.*/ig) {
    my $imageId = $3;
    if($imageId !~ /IMAGE/ig) {
      my $imageRepo = $1;
      my $imageTag = $2;
      printf "delete $imageId ($imageRepo:$imageTag)\n";
      my $deleteImageOutput = `docker rmi -f $imageId`;
      printf "$deleteImageOutput\n";
    }
  }
}
