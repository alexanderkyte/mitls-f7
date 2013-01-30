# -*- Makefile -*-

# --------------------------------------------------------------------
version  ?= 0.1
name      = miTLS
distname  = $(name)-$(version)

subdirs  += 3rdparty CoreCrypto DB lib TLSharp
subdirs  += HttpServer echo rpc
subdirs  += www-data

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
	if [ -x ./anonymize ]; then \
	  find $(distname) \
	    -type f \( -name '*.fs' -o -name '*.fsi' \) \
	    -exec ./anonymize -m release:$(AKWS) -B -c LICENSE '{}' \+; \
	fi
	tar --format=posix -czf $(distname).tgz $(distname)
	rm -rf $(distname)

do-dist-check:
	tar -xof $(distname).tgz
	cd $(distname) && $(MAKE) && $(MAKE) dist
	tar -C $(distname) -xof $(distname).tgz
	tar -C $(distname) -df $(distname).tgz $(distname)
	rm -rf $(distname)
	@echo "$(distname).tgz is ready for distribution"

dist-check: dist do-dist-check

clean:
	rm -rf bin

dist-clean: clean
	rm -f $(distname).tgz
