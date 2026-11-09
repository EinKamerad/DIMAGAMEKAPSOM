import http.server, socketserver, webbrowser, os
os.chdir(os.path.dirname(os.path.abspath(__file__)))
PORT = 8080
class NoCacheHandler(http.server.SimpleHTTPRequestHandler):
    def end_headers(self):
        self.send_header('Cache-Control', 'no-store, no-cache, must-revalidate, max-age=0')
        super().end_headers()
socketserver.TCPServer.allow_reuse_address = True
print(f'Starting BioFarme local WebGL server at http://localhost:{PORT}')
webbrowser.open(f'http://localhost:{PORT}/index.html')
with socketserver.TCPServer(('', PORT), NoCacheHandler) as httpd:
    httpd.serve_forever()
