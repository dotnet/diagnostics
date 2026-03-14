cd $HOME
wget http://ftp.gnu.org/gnu/binutils/binutils-2.29.1.tar.xz
wget http://releases.llvm.org/3.9.1/cfe-3.9.1.src.tar.xz
wget http://releases.llvm.org/3.9.1/llvm-3.9.1.src.tar.xz
wget http://releases.llvm.org/3.9.1/lldb-3.9.1.src.tar.xz
wget http://releases.llvm.org/3.9.1/compiler-rt-3.9.1.src.tar.xz

tar -xf binutils-2.29.1.tar.xz
tar -xf llvm-3.9.1.src.tar.xz
mkdir llvm-3.9.1.src/tools/clang
mkdir llvm-3.9.1.src/tools/lldb
mkdir llvm-3.9.1.src/projects/compiler-rt
tar -xf cfe-3.9.1.src.tar.xz --strip 1 -C llvm-3.9.1.src/tools/clang
tar -xf lldb-3.9.1.src.tar.xz --strip 1 -C llvm-3.9.1.src/tools/lldb
tar -xf compiler-rt-3.9.1.src.tar.xz --strip 1 -C llvm-3.9.1.src/projects/compiler-rt
rm binutils-2.29.1.tar.xz
rm cfe-3.9.1.src.tar.xz
rm lldb-3.9.1.src.tar.xz
rm llvm-3.9.1.src.tar.xz
rm compiler-rt-3.9.1.src.tar.xz

mkdir llvmbuild
cd llvmbuild
cmake3 -DCMAKE_BUILD_TYPE=Release -DLLVM_LIBDIR_SUFFIX=64 -DLLVM_ENABLE_EH=1 -DLLVM_ENABLE_RTTI=1 -DLLVM_BINUTILS_INCDIR=../binutils-2.29.1/include ../llvm-3.9.1.src
make -j $(($(getconf _NPROCESSORS_ONLN)+1))
sudo make install
cd ..
rm -r llvmbuild
rm -r llvm-3.9.1.src
rm -r binutils-2.29.1
