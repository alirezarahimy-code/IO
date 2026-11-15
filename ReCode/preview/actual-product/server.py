#!/usr/bin/env python3
"""Read-only source viewer, NOT the product backend. No fixtures, login, SQL or SDK."""
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import urlsplit, parse_qs, unquote
from pathlib import Path
import argparse
import hashlib
import json
import re
import subprocess

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
UI = ROOT / 'ReCode/src/IO.Web/Ui'
CATALOG = ROOT / 'ReCode/src/IO.Infrastructure/Security/WorkspaceCatalog.cs'


def inventory():
    source = CATALOG.read_text(encoding='utf-8')
    rows = [dict(zip(('route', 'title', 'group', 'resource'), match)) for match in re.findall(
        r'new\("([^"]+)",\s*"([^"]+)",\s*"([^"]+)",\s*"([^"]+)"', source)]
    if not rows or len({row['route'] for row in rows}) != len(rows):
        raise RuntimeError('Invalid real product page catalogue')
    tracked = set(subprocess.check_output(['git', 'ls-files', 'ReCode/src/IO.Web/Ui'], cwd=ROOT, text=True).splitlines())
    articles = json.loads((UI / 'HelpContent.json').read_text(encoding='utf-8'))
    for row in rows:
        file = UI / row['resource']
        if file.parent != UI or not file.is_file() or file.relative_to(ROOT).as_posix() not in tracked:
            raise RuntimeError('Catalogue page must have a real tracked HTML resource')
        data = file.read_bytes()
        row.update(source=file.relative_to(ROOT).as_posix(), sha256=hashlib.sha256(data).hexdigest(), bytes=len(data))
        article = next((a for a in articles if a['Route'] == row['route']), None)
        row['article'] = {key: article.get(key) for key in ('Overview', 'Steps', 'Limitations')} if article else None
    revision = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip()
    return {'revision': revision, 'pages': rows, 'routes': len(rows),
            'htmlFiles': len({p['resource'] for p in rows}), 'backendConnected': False,
            'dataFixtures': False, 'source': CATALOG.relative_to(ROOT).as_posix()}


class Handler(BaseHTTPRequestHandler):
    def send_bytes(self, status, data, mime='text/plain; charset=utf-8', headers=None):
        self.send_response(status)
        self.send_header('Content-Type', mime)
        self.send_header('Content-Length', str(len(data)))
        self.send_header('Cache-Control', 'no-store')
        self.send_header('X-Content-Type-Options', 'nosniff')
        self.send_header('Referrer-Policy', 'no-referrer')
        for key, value in (headers or {}).items():
            self.send_header(key, value)
        self.end_headers()
        if self.command != 'HEAD':
            self.wfile.write(data)

    def json(self, status, value):
        self.send_bytes(status, json.dumps(value, ensure_ascii=False).encode(), 'application/json; charset=utf-8')

    def unavailable(self):
        self.json(503, {'error': 'preview-backend-unavailable', 'backendConnected': False,
                        'message': 'این نمایشگر فقط فایل‌های واقعی محصول را باز می‌کند؛ backend، ورود، مجوز، SQL و SDK به آن متصل نیستند. هیچ داده یا نتیجهٔ ساختگی برگردانده نمی‌شود.'})

    def do_HEAD(self):
        self.do_GET()

    def do_GET(self):
        url = urlsplit(self.path)
        path = unquote(url.path)
        if path in ('/', '/__preview/'):
            self.send_bytes(200, (HERE / 'index.html').read_bytes(), 'text/html; charset=utf-8', {
                'Content-Security-Policy': "default-src 'self'; script-src 'self'; style-src 'self'; frame-src 'self'; connect-src 'self'; object-src 'none'; base-uri 'none'; form-action 'none'"})
            return
        if path in ('/__preview/viewer.js', '/__preview/viewer.css'):
            file = HERE / path.rsplit('/', 1)[1]
            self.send_bytes(200, file.read_bytes(), 'text/javascript; charset=utf-8' if file.suffix == '.js' else 'text/css; charset=utf-8')
            return
        if path == '/__preview/inventory':
            self.json(200, inventory())
            return
        if path == '/__preview/source':
            requested = parse_qs(url.query).get('route', [''])[0]
            row = next((r for r in inventory()['pages'] if r['route'] == requested), None)
            if row is None:
                self.send_bytes(404, b'Not a real product page')
                return
            self.send_bytes(200, (UI / row['resource']).read_bytes(), headers={'X-Product-Source-SHA256': row['sha256']})
            return
        if path.startswith('/assets/'):
            name = path[len('/assets/'):]
            file = UI / name
            if '/' in name or '\\' in name or file.parent != UI or not file.is_file() or file.suffix not in ('.js', '.css'):
                self.send_bytes(404, b'No such product asset')
                return
            self.send_bytes(200, file.read_bytes(), 'text/javascript; charset=utf-8' if file.suffix == '.js' else 'text/css; charset=utf-8')
            return
        row = next((r for r in inventory()['pages'] if r['route'] == path), None)
        # Help articles are an existing dynamic route backed by Help.html, not invented screens.
        if row is None and path.startswith('/workspace/help/'):
            row = next((r for r in inventory()['pages'] if r['route'] == '/workspace/help'), None)
        if row:
            mode = parse_qs(url.query).get('__preview_mode', ['layout'])[0]
            # HTML bytes are unchanged in BOTH modes. Policy, not DOM rewriting, disables scripts.
            scripts = "'self'" if mode == 'runtime' else "'none'"
            connect = "'self'" if mode == 'runtime' else "'none'"
            self.send_bytes(200, (UI / row['resource']).read_bytes(), 'text/html; charset=utf-8', {
                'X-Product-Source-SHA256': row['sha256'],
                'Content-Security-Policy': f"default-src 'none'; script-src {scripts}; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src {connect}; form-action 'none'; base-uri 'none'; object-src 'none'"})
            return
        if path == '/favicon.ico':
            self.send_bytes(204, b'')
            return
        # Never return []/0, capabilities, a CSRF token, fabricated identity or a success receipt.
        self.unavailable()

    def do_POST(self):
        self.close_connection = True
        self.unavailable()

    do_PUT = do_PATCH = do_DELETE = do_POST

    def log_message(self, fmt, *args):
        # No query values, bodies, credentials or form values in this viewer's request log.
        print(f'{self.command} {urlsplit(self.path).path}', flush=True)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--port', type=int, default=4180)
    args = parser.parse_args()
    data = inventory()
    print(f"Actual product source viewer: {data['routes']} real routes / {data['htmlFiles']} HTML resources. NO database/auth fixtures. 0.0.0.0:{args.port}", flush=True)
    ThreadingHTTPServer(('0.0.0.0', args.port), Handler).serve_forever()
