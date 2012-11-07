# -*- Makefile -*-

# --------------------------------------------------------------------
name     = uTLS
version  = 0.0.internal
distname = $(name)-$(version)

subdirs  += BouncyCastle CoreCrypto lib TLSharp
subdirs  += HttpServer echo rpc
subdirs  += www-data

.PHONY = all build make.in dist

all: build

build:
	[ -d bin ] || mkdir bin
	set -e; for d in $(subdirs); do $(MAKE) -f Makefile.build -C $$d; done

make.in:
	set -e; for d in $(subdirs); do $(MAKE) -f Makefile.build -C $$d make.in; done

dist:
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
	find $(distname) -type f -exec chmod a-x '{}' \+
	tar --format=posix -czf $(distname).tgz $(distname)
	rm -rf $(distname)

dist-check: dist
	tar -xof $(distname).tgz
	cd $(distname) && $(MAKE)
	rm -rf $(distname)
	@echo "$(distname).tgz is ready for distribution"

clean:
	rm -rf bin
