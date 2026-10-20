import { createReadStream, existsSync, statSync } from "node:fs";
import { createServer } from "node:http";
import { extname, isAbsolute, join, relative } from "node:path";
import { fileURLToPath } from "node:url";

const dist = fileURLToPath(new URL("./dist/", import.meta.url));
const types = { ".html": "text/html; charset=utf-8", ".css": "text/css", ".png": "image/png", ".jpg": "image/jpeg", ".webp": "image/webp", ".ico": "image/x-icon" };
const port = Number(process.env.PORT) || 4321;

createServer((request, response) => {
  const path = decodeURIComponent(new URL(request.url, "http://localhost").pathname);
  let file = join(dist, path);
  const inside = relative(dist, file);
  if (inside.startsWith("..") || isAbsolute(inside)) {
    response.writeHead(403).end();
    return;
  }
  if (existsSync(file) && statSync(file).isDirectory()) file = join(file, "index.html");
  if (!existsSync(file)) {
    response.writeHead(404).end("Not found");
    return;
  }
  response.writeHead(200, { "Content-Type": types[extname(file)] ?? "application/octet-stream" });
  createReadStream(file).pipe(response);
}).listen(port, "127.0.0.1", () => console.log(`Kumo website on http://localhost:${port}`));
