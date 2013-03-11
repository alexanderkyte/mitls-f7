# -*- Makefile -*-

# --------------------------------------------------------------------
version    ?= 0.1.2
name        = miTLS
distname    = $(name)-$(version)
f7distname  = $(name)-f7-$(version)

subdirs  += 3rdparty OpenSSL CoreCrypto DB lib TLSharp
subdirs  += HttpServer echo rpc
subdirs  += www-data

TAR = tar --format=posix --owner=0 --group=0

.PHONY: all build make.in prepare-dist
.PHONY: do-dist-check dist dist-check

all: build

build:
	[ -d bin ] || mkdir bin
	set -e; for d in $(subdirs); do $(MAKE) -f Makefile.build -C $$d; done

make.in:
	set -e; for d in $(subdirs); do $(MAKE) -f Makefile.build -C $$d make.in; done

prepare-dist:
	rm -rf $(distname) && mkdir $(distname)
	rm -rf $(distname).tgz
	set -e; for d in $(subdirs); do \
	   mkdir $(distname)/$$d; \
	   cp $$d/Makefile.build $(distname)/$$d; \
	   $(MAKE) -f Makefile.build -C $$d distdir=../$(distname)/$$d dist; \
	done
	cp Makefile               $(distname)
	cp Makefile.config        $(distname)
	cp Makefile.config.cygwin $(distname)
	cp Makefile.config.unix   $(distname)
	cp README                 $(distname)
	mkdir $(distname)/licenses && \
	  cp licenses/*.txt $(distname)/licenses
	find $(distname) -type f -exec chmod a-x '{}' \+

dist: prepare-dist
	cp LICENSE AUTHORS $(distname)
	if [ -x scripts/anonymize ]; then \
	  find $(distname) \
	    -type f \( -name '*.fs' -o -name '*.fsi' -o -name '*.fs7' \) \
	    -exec scripts/anonymize \
	      -m release -B -P -I ideal -I verify -c LICENSE.header '{}' \+; \
	fi
	$(TAR) -czf $(distname).tgz $(distname)
	rm -rf $(distname)

do-dist-check:
	tar -xof $(distname).tgz
	set -x; \
	     $(MAKE) -C $(distname) \
	  && $(MAKE) -C $(distname) dist \
	  && mkdir $(distname)/dist1 $(distname)/dist2 \
	  && ( cd $(distname)/dist1 && tar -xof ../$(distname).tgz ) \
	  && ( cd $(distname)/dist2 && tar -xof ../../$(distname).tgz ) \
	  && diff -rq $(distname)/dist1 $(distname)/dist2 \
	  || exit 1
	rm -rf $(distname)
	@echo "$(distname).tgz is ready for distribution" | \
	  sed -e 1h -e 1s/./=/g -e 1p -e 1x -e '$$p' -e '$$x'

dist-check: dist do-dist-check

clean:
	rm -rf bin

dist-clean: clean
	rm -f $(distname).tgz
	rm -f $(f7distname).tgz
