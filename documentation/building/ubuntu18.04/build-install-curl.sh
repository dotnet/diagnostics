# Install cmake version 3.10.2 without installing libcurl.so.4
wget https://cmake.org/files/v3.10/cmake-3.10.2-Linux-x86_64.tar.gz
sudo tar -xf cmake-3.10.2-Linux-x86_64.tar.gz --strip 1 -C /usr/local
rm cmake-3.10.2-Linux-x86_64.tar.gz

# Build and install curl 7.45.0 to get the right version of libcurl.so.4
wget https://curl.haxx.se/download/curl-7.45.0.tar.lzma
tar -xf curl-7.45.0.tar.lzma
rm curl-7.45.0.tar.lzma
cd curl-7.45.0
./configure --disable-dict --disable-ftp --disable-gopher --disable-imap --disable-ldap --disable-ldaps --disable-libcurl-option --disable-manual --disable-pop3 --disable-rtsp --disable-smb --disable-smtp --disable-telnet --disable-tftp --enable-ipv6 --enable-optimize --enable-symbol-hiding --with-ca-path=/etc/ssl/certs/ --with-nghttp2 --with-gssapi --with-ssl --without-librtmp
make
sudo make install
cd ..
rm -r curl-7.45.0

# Install curl
sudo apt-get install curl
