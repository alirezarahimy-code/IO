#!/usr/bin/env python3
"""Offline preview server for the T5Web admin console.

Serves src/T5Web so the preview uses the *real* product assets
(T5Web.Web/Content/t5web-admin.css and t5web-admin.js) instead of copies.
Nothing is fetched from the network: no fonts, no CDNs, no analytics.

    python3 preview/server.py           -> http://0.0.0.0:8090/preview/

The preview is static HTML that mirrors the Razor views one-to-one, so a layout
change can be reviewed in a browser before the .NET build is run on Windows.

Headers are the product's own (T5Web.Web/Web.config), with one deliberate
exception: frame-ancestors. The console itself sends frame-ancestors 'none' -
nothing may embed the admin console. The preview has to be embeddable, because it
is shown inside a review pane, so it sends frame-ancestors * unless
PREVIEW_FRAME_ANCESTORS says otherwise. Everything else (self-only scripts and
styles, no inline anything) is identical, which is the point: a page that only
works with inline CSS will visibly break here first.
"""
import os
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PORT = int(os.environ.get("PORT", "8090"))

# The product sends 'none'; here the page must be embeddable to be reviewed.
FRAME_ANCESTORS = os.environ.get("PREVIEW_FRAME_ANCESTORS", "*")

# Which screen the root URL opens. The index is the guide to the whole preview; a
# reviewer looking at one screen can point the root straight at it instead:
#     PREVIEW_START=personnel.html python3 preview/server.py
START_PAGE = os.environ.get("PREVIEW_START", "index.html")


class Handler(SimpleHTTPRequestHandler):
    def __init__(self, *args, **kwargs):
        super().__init__(*args, directory=ROOT, **kwargs)

    def do_GET(self):
        if self.path in ("/", "/index.html"):
            self.send_response(302)
            self.send_header("Location", "/preview/" + START_PAGE)
            self.end_headers()
            return
        super().do_GET()

    def do_POST(self):
        """A preview form must not land on a 501 error page.

        The static pages carry the product's real <form method="post"> markup, so a
        click would otherwise hit SimpleHTTPRequestHandler's "unsupported method".
        The body is read and thrown away and the same page is returned: the preview
        never writes anything, it just has to look like the console does.
        """
        try:
            length = int(self.headers.get("Content-Length") or 0)
        except ValueError:
            length = 0
        if length > 0:
            self.rfile.read(length)
        self.do_GET()

    def end_headers(self):
        # Same headers the product sends, so the preview cannot accidentally rely
        # on something the real CSP forbids.
        self.send_header("Content-Security-Policy",
                         "default-src 'self'; style-src 'self'; img-src 'self' data:; "
                         "script-src 'self'; font-src 'self'; connect-src 'self'; "
                         "form-action 'self'; frame-ancestors " + FRAME_ANCESTORS)
        self.send_header("X-Content-Type-Options", "nosniff")
        self.send_header("Referrer-Policy", "no-referrer")
        super().end_headers()

    def log_message(self, fmt, *args):
        pass


if __name__ == "__main__":
    print("T5Web preview  ->  http://0.0.0.0:%d/preview/%s" % (PORT, START_PAGE))
    ThreadingHTTPServer(("0.0.0.0", PORT), Handler).serve_forever()
