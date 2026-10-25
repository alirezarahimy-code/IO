#!/usr/bin/env python3
"""Offline preview server for the T5Web admin console.

Serves src/T5Web so the preview uses the *real* product assets
(T5Web.Web/Content/t5web-admin.css and t5web-admin.js) instead of copies.
Nothing is fetched from the network: no fonts, no CDNs, no analytics.

    python3 preview/server.py           -> http://0.0.0.0:8090/preview/

The preview is static HTML that mirrors the Razor views one-to-one, so a layout
change can be reviewed in a browser before the .NET build is run on Windows.
"""
import os
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PORT = int(os.environ.get("PORT", "8090"))


class Handler(SimpleHTTPRequestHandler):
    def __init__(self, *args, **kwargs):
        super().__init__(*args, directory=ROOT, **kwargs)

    def do_GET(self):
        if self.path in ("/", "/index.html"):
            self.send_response(302)
            self.send_header("Location", "/preview/index.html")
            self.end_headers()
            return
        super().do_GET()

    def end_headers(self):
        # Same headers the product sends, so the preview cannot accidentally rely
        # on something the real CSP forbids.
        self.send_header("Content-Security-Policy",
                         "default-src 'self'; style-src 'self'; img-src 'self' data:; "
                         "script-src 'self'; font-src 'self'; connect-src 'self'; "
                         "frame-ancestors 'none'")
        self.send_header("X-Content-Type-Options", "nosniff")
        self.send_header("Referrer-Policy", "no-referrer")
        super().end_headers()

    def log_message(self, fmt, *args):
        pass


if __name__ == "__main__":
    print("T5Web preview  ->  http://0.0.0.0:%d/preview/" % PORT)
    ThreadingHTTPServer(("0.0.0.0", PORT), Handler).serve_forever()
