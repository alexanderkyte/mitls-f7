miTLS
=====

This is a temporary public repository of miTLS,
a verified reference implementation of the TLS security protocol.

1. Compilation
--------------

To compile, usually running "make" from the top level directory is
enough. (See below for prerequisites.)

The produced executables are placed in the `bin' directory.

Each command line tool accepts a "--help" option that shows all the
available command line options and their default values.

The following make targets are available:

- build (default)
    compiles source code and puts executables in then bin directory

- build-debug
	compiles source code in debug mode

- dist
    prepares a compressed archive that can be distributed

- dist-check
    as dist, but also performs some sanity checks

- clean
    remove object files

- dist-clean
    remove object files and distribution archives

The test suite is currently not released, and thus not
available as a make target.

2. Verification
---------------

Refinement type checking of the code base is driven by the Makefile in
./lib; this file has a "tc7" target for each file to be type checked.
Type checking requires F7 and Z3. Note that the latest version of F7
we use is currently not released.

Each F# implementation file (with .fs extension) may use compilation
flags to control what is passed to F7 vs F#

- ideal: enables ideal cryptographic functionalities in the code.
  (e.g. the ones performing table lookups)

- verify: enables assumptions of events in the code.

Both compilation flags are disabled when compiling the concrete code,
and enabled during type checking.

3. Prerequisites
----------------

In the following, the prerequisites for each supported platform are
given. In general, you need a running F# installation, see
http://fsharp.org/ on how to get the most recent version of F#
for your platform.

### 3.a. Microsoft Windows

Either
- Visual Studio 2013

or
- Cygwin, with the make utility installed
- .NET version 4.5 or above
- Visual F# 3.1 or above

### 3.b. Linux, Mac OS X and other Un*ces

- Mono framework, version 3.4.0 or above; this includes F# 3.1.

* 4. FlexTLS & FlexApps

The FlexTLS library and the FlexApps console application should 
both build out of the box if all requirements of miTLS are satisfied.
The FlexApps project stores the scenarios; Application.fs
is the file where the calls to those can be commented/uncommented.

To use certificates, miTLS and flexTLS rely on certificate stores.
On Windows, the Windows store handles management of those. For
other Unix platforms the Mono store is used.

The following are guidelines on how to import certificates into
the store, according to your platform.

A DH scenario requires default DH parameters to be loaded from file.
A sample default-dh.pem file with default parameters is provided
in the data/dh directory.

** 4.a. Microsoft Windows

Add a CA certificate to the store by using the certutil command
certutil -f -user -addstore Root ca.crt

Add a personal certificate to the store by using the certutil command
certutil -f -user -p '' -v -importpfx certificate.p12

Delete a CA certificate from the store by using the certutil command
certutil -user -delstore Root <NAME>

Delete a certificate from the store by using the certutil command
certutil -user -delstore My <NAME>

** 4.b. Linux, MacOS X and other Un*ces

Add a CA certificate to the store by using the certmgr command
umask 077; certmgr -add -c Trust ca.crt

Add a personal certificate to the store by using the certmgr command
umask 077; ( set -e; \
  certmgr -add       -c My <NAME>.crt;      \
  certmgr -importKey -c -p '' My <NAME>.p12 \
)

Delete a CA certificate from the store by using the certmgr command
certmgr -del -c Trust <NAME>

Delete a certificate from the store by using the certmgr command
certmgr -del -c My <NAME>
