/* -------------------------------------------------------------------- */
#include <sys/types.h>
#include <stdlib.h>
#include <string.h>

#include <errno.h>

#include "echo-log.h"
#include "echo-memory.h"
#include "echo-options.h"
#include "echo-client.h"
#include "echo-server.h"

/* -------------------------------------------------------------------- */
#define REQOSSL 0x01000101L

#if OPENSSL_VERSION_NUMBER < REQOSSL
# error "invalid OpenSSL version number"
#endif

/* -------------------------------------------------------------------- */
static event_base_t *evb = NULL;

/* -------------------------------------------------------------------- */
int main(int argc, char *argv[]) {
    options_t options;

    initialize_log4c();

    if (SSLeay() < REQOSSL) {
        elog(LOG_FATAL, "OpenSSL version < 0x%.8lx (compiled with 0x%.8lx)",
             SSLeay(), OPENSSL_VERSION_NUMBER);
        return EXIT_FAILURE;
    }

    if (_options(argc, argv, &options) < 0)
        return EXIT_FAILURE;

    if (options.debug)
        log4c_category_set_priority(logcat, LOG_DEBUG);


    if ((evb = event_init()) == NULL) {
        elog(LOG_FATAL, "cannot initialize libevent");
        return EXIT_FAILURE;
    }

    event_set_log_callback(_evlog);
    event_set_mem_functions(&xmalloc, &xrealloc, &free);

    {   int rr;

        if (options.client)
            rr = echo_client_setup(evb, &options);
        else
            rr = echo_server_setup(evb, &options);

        if (rr < 0)
            return EXIT_FAILURE;
    }

    elog(LOG_NOTICE, "started");
    event_dispatch();

    return 0;
}
