#!/usr/bin/env python3
"""Offline preview server for the ARIA rewrite.

Serves src/TWF so the preview can reuse the real product assets
(TWF.Web/Content/twf-industrial-ui.css and TWF.Web/Scripts/twf-core.js)
without copying them, and redirects the site root to the login screen.
"""
import os
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PORT = int(os.environ.get("PORT", "8080"))


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

    def log_message(self, fmt, *args):
        pass


if __name__ == "__main__":
    ThreadingHTTPServer(("0.0.0.0", PORT), Handler).serve_forever()
