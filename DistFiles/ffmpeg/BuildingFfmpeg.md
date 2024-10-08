                                                June 7, 2018
Building a Reduced ffmpeg for Windows
=====================================

These websites gave the inspiration and basic direction for building a reduced
ffmpeg on Windows, but details differed in a lot of places.  The first website
assumes you're running on Linux, and doesn't include libvpx which is needed for
reading mp4 files produced by mozilla code.  (Also, the python script he talks
about won't run on Windows.)  The second website is a bit dated, as it talks
about building on Windows 7.

  - [Build-your-own-tiny-FFMPEG](https://github.com/alberthdev/alberthdev-misc/wiki/Build-your-own-tiny-FFMPEG)
  - [building-with-libvpx](http://wiki.webmproject.org/ffmpeg/building-with-libvpx)


mingw
-----
1. Download [mingw-get-setup.exe](https://sourceforge.net/projects/mingw/files/).
   (This is a 32-bit version of mingw.  If you want to build 64-bit programs, then
   you need to download mingw64, which I haven't tested but which shouldn't be too
   different in theory.)
2. Run the installer, install for all users if you like, install into C:\mingw,
   creat shortcuts to launch the MinGW shell window, and add MinGW to your PATH.
3. Install at least the following components:
      - mingw-developer-toolkit
      - mingw32-base
      - mingw32-gcc-g++
      - msys-base


pkg-config (not included with mingw)
----------
1. Download [pkg-config-lite-0.28-1.zip](https://sourceforge.net/projects/pkgconfiglite/)
   into a temporary location.
2. Extract the files from the zip file.
3. Copy pkg-config-lite-0.28-1\bin\pkg-config.exe to C:\mingw\bin.
4. Copy pkg-config-lite-0.28-1\share\aclocal\pkg.m4 to C:\mingw\share\aclocal.


git (not included with mingw)
---
1. Download the [Windows git installer](https://git-scm.com/download/win).
2. Install git, choosing the "Use Git from the Windows Command Prompt" option, and
   then choosing "Checkout as-is, commit Unix-style line endings".  (The latter may
   need to change for other projects, but is needed for this set of builds.)  The
   default settings are okay otherwise.

If you already have git installed for other work, then you may need to edit your global
.gitconfig file to set the following values:
<pre>
[core]
        autocrlf = input
</pre>
Once you are through with this build, you will need to restore its original (possibly
absent) setting.


work directory
--------------
Launch the MinGW shell ("C:\MinGW\msys\1.0\msys.bat") and create a directory for holding all this work (unless you
already have such a location).
<pre>
    mkdir ~/src
    cd ~/src
</pre>
or
<pre>
    mkdir /d/src
    cd /d/src
</pre>
or wherever you want to work on this build process.

The rest of these instructions assume you have a MinGW shell window open to the desired
work directory.


yasm
----
<pre>
git clone git://github.com/yasm/yasm.git
cd yasm
./autogen.sh --prefix=/mingw --target=x86-win32-gcc
make install
cd ..
</pre>

libogg
------
<pre>
git clone https://github.com/xiph/ogg
cd ogg
./autogen.sh && ./configure --prefix=/mingw --target=x86-win32-gcc
make install
cd ..
</pre>

libvorbis
---------
<pre>
git clone https://github.com/xiph/vorbis
cd vorbis
./autogen.sh && ./configure --prefix=/mingw
make install
cd ..
</pre>

libvpx
------
Note that the repository is a fork of the [original libvpx repository](https://chromium.googlesource.com/webm/libvpx).
I had to edit three files to get the library to compile with MinGW on Windows.
<pre>
git clone https://github.com/BloomBooks/libvpx
cd libvpx
git checkout Bloom
extralibs=-lmingwex ./configure --enable-static --enable-multithread --disable-vp9 --as=yasm --enable-libyuv --enable-webm-io --prefix=/mingw --target=x86-win32-gcc --disable-unit-tests
make install
cd ..
</pre>

libx264
-------
<pre>
git clone http://git.videolan.org/git/x264.git
cd x264
mkdir build
cd build
../configure --enable-static --disable-cli --disable-gpl --disable-opencl --disable-avs --disable-swscale --disable-lavf --disable-ffms --disable-gpac --disable-lsmash --enable-lto --prefix=/mingw
make install
cd ../..
</pre>
Note, when I (Andrew) was trying to follow these instructions, I was getting errors during this build because it wanted to use nasm rather than yasm.
I ended up installing nasm. Unfortunately, I didn't take notes on what I did.<br>
[JS] Yeah, x264 defaults to (and prefers) nasm over yasm now. You can download it for Windows. https://www.nasm.us/ -> Download -> Pick the latest release -> win32 (probably) -> download the installer. Some reported x264 doesn't find it unless it's installed system-wide, so I recommend installing it system-wide (Run as administrator). I used the default options. Add the NASM location (e.g. "C:\Program Files (x86)\NASM") to your PATH environment variable. Restart MinGW if it's open.

lame
------
Note that this repository originally came from https://sourceforge.net/projects/lame/, version 3.100, as a .tar (it is a SVN repository).
We had to edit one file to get it to compile with MinGW on Windows.
<pre>
git clone https://github.com/BloomBooks/lame
cd lame
git checkout Bloom
./configure --prefix=/mingw --disable-shared --enable-expopt=full
make install
cd ..
</pre>

ffmpeg
------
Note that the repository is a fork of the [original ffmpeg repository](git://source.ffmpeg.org/ffmpeg.git).
I had to edit one file to get the program to compile with MinGW on Windows.
<pre>
git clone https://github.com/BloomBooks/ffmpeg
cd ffmpeg
git config core.autocrlf false; [delete all non-git files]; git reset --hard [fixes an error where .mak files ended in CRLF and "make install" errors when doing "eval" on CRLF lines]
git checkout bloom-ffmpeg5.0
mkdir build
cd build
../configure \
 --disable-postproc --enable-avcodec --enable-avdevice --enable-avformat --enable-avfilter --enable-swresample --enable-swscale --disable-encoders --enable-encoder='rawvideo,libx264,libvpx_vp8,aac,libmp3lame,h263' --disable-hwaccels --disable-parsers --enable-parser=h264,vp8,mpegaudio --disable-protocols --enable-protocol='file,concat' --disable-muxers --enable-muxer='rawvideo,mp4,mp3,tgp' --disable-bsfs --disable-filters --enable-filter='scale,adelay,afade,amix,aresample,volume' --disable-indevs --enable-indev=gdigrab --disable-outdevs \
  \
 --disable-autodetect --enable-libx264 --enable-libvorbis --enable-libvpx --enable-libmp3lame \
  \
 --disable-programs --enable-ffmpeg \
 --disable-doc \
 --enable-gpl \
 --prefix=/mingw --pkg-config-flags=--static \
  --extra-ldflags=-static

make install
cd ../..
</pre>

After building, the desired ffmpeg.exe is found in .../ffmpeg/build and in C:\mingw\bin.

The current full static ffmpeg.exe is about 60MB in size.  The reduced static ffmpeg.exe
built through the process above is about 6.8MB in size.
