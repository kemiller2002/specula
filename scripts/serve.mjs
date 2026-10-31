// Minimal static file server for local runs and browser tests.
import { createServer } from "node:http";
import { readFile, stat } from "node:fs/promises";
import { extname, join, normalize, resolve } from "node:path";

const root = resolve(process.argv[2] ?? "dist");
const port = Number(process.argv[3] ?? 4173);
const types = {
  ".html": "text/html; charset=utf-8",
  ".js": "text/javascript; charset=utf-8",
  ".mjs": "text/javascript; charset=utf-8",
  ".css": "text/css; charset=utf-8",
  ".json": "application/json; charset=utf-8",
  ".wasm": "application/wasm",
  ".dat": "application/octet-stream",
  ".svg": "image/svg+xml",
};

const resolvePath = (url) => {
  const path = normalize(decodeURIComponent(new URL(url, "http://localhost").pathname)).replace(/^([/\\])+/, "");
  const full = join(root, path);
  return full.startsWith(root) ? full : null;
};

createServer(async (request, response) => {
  const path = resolvePath(request.url ?? "/");
  const target = path && (await stat(path).then((s) => (s.isDirectory() ? join(path, "index.html") : path), () => null));
  const file = target ?? join(root, "404.html");
  try {
    const body = await readFile(file);
    response.writeHead(target ? 200 : 404, { "content-type": types[extname(file)] ?? "application/octet-stream" });
    response.end(body);
  } catch {
    response.writeHead(404, { "content-type": "text/plain" });
    response.end("Not found");
  }
}).listen(port, "127.0.0.1", () => console.log(`Serving ${root} on http://127.0.0.1:${port}`));
