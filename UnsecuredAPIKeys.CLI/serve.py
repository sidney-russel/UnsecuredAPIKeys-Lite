import http.server
import os

class Handler(http.server.SimpleHTTPRequestHandler):
    def do_GET(self):
        self.send_response(200)
        self.send_header("Content-Type", "text/csv")
        self.send_header("Content-Disposition", 'attachment; filename="keys.csv"')
        self.end_headers()
        with open("keys.csv", "rb") as f:
            self.wfile.write(f.read())

if __name__ == "__main__":
    port = 3000
    server = http.server.HTTPServer(("0.0.0.0", port), Handler)
    print(f"Serving keys.csv for download at http://localhost:{port}")
    server.serve_forever()
