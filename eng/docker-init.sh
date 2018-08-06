# Install sudo on ubuntu 18.04
os_name=$(uname -s)
if [ "$os_name" == "Linux" ]; then
    if [ -e /etc/os-release ]; then
        source /etc/os-release
        if [[ $ID == "ubuntu" ]]; then
            if [[ $VERSION_ID == "18.04" ]]; then
                apt-get update
                apt-get install sudo
            fi
        fi
    fi
fi
