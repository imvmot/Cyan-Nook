#!/usr/bin/env node
// Cyan Nook local server (Node.js)
// Static file serving + CORS proxy for Claude / OpenAI / etc.
// Cross-platform counterpart of server.ps1. Uses only Node.js built-in modules.

const http = require('http');
const https = require('https');
const fs = require('fs');
const path = require('path');
const { spawn } = require('child_process');

const PORT = parseInt(process.env.PORT || '8080', 10);
const ROOT = __dirname;

const MIME = {
    '.html': 'text/html',
    '.js': 'application/javascript',
    '.mjs': 'application/javascript',
    '.css': 'text/css',
    '.json': 'application/json',
    '.wasm': 'application/wasm',
    '.data': 'application/octet-stream',
    '.png': 'image/png',
    '.jpg': 'image/jpeg',
    '.svg': 'image/svg+xml',
    '.ico': 'image/x-icon',
    '.gif': 'image/gif',
    '.webp': 'image/webp',
    '.webmanifest': 'application/manifest+json',
    '.mp3': 'audio/mpeg',
    '.wav': 'audio/wav',
    '.ogg': 'audio/ogg',
    '.woff': 'font/woff',
    '.woff2': 'font/woff2',
    '.ttf': 'font/ttf',
    '.txt': 'text/plain',
    '.xml': 'application/xml',
    '.unityweb': 'application/octet-stream',
    '.br': 'application/octet-stream',
    '.gz': 'application/octet-stream',
};

const PROXY_ALLOW_HEADERS = ['content-type', 'authorization', 'x-api-key', 'anthropic-version', 'x-goog-api-key'];
const CORS_HEADERS = {
    'Access-Control-Allow-Origin': '*',
    'Access-Control-Allow-Methods': 'GET, POST, PUT, DELETE, OPTIONS',
    'Access-Control-Allow-Headers': 'Content-Type, Authorization, x-api-key, anthropic-version, x-goog-api-key',
    'Access-Control-Max-Age': '86400',
};

// ANSI colors (works on modern Windows 10+ terminals, macOS, Linux)
const C = {
    reset: '\x1b[0m',
    gray: '\x1b[90m',
    red: '\x1b[31m',
    green: '\x1b[32m',
    yellow: '\x1b[33m',
    cyan: '\x1b[36m',
    dim: '\x1b[2m',
};
function log(color, msg) { console.log(`${color}${msg}${C.reset}`); }

function openBrowser(url) {
    const platform = process.platform;
    let cmd, args;
    if (platform === 'darwin') { cmd = 'open'; args = [url]; }
    else if (platform === 'win32') { cmd = 'cmd'; args = ['/c', 'start', '""', url]; }
    else { cmd = 'xdg-open'; args = [url]; }
    try {
        const child = spawn(cmd, args, { detached: true, stdio: 'ignore' });
        // Must attach an 'error' handler — without it, ENOENT crashes the process
        // (common in headless Linux / Docker containers without xdg-open).
        child.on('error', () => { });
        child.unref();
    } catch (_) { /* ignore */ }
}

function handleProxy(req, res) {
    // Preflight
    if (req.method === 'OPTIONS') {
        res.writeHead(204, CORS_HEADERS);
        res.end();
        log(C.dim, '[PROXY] OPTIONS preflight');
        return;
    }

    // Extract target URL from raw url (avoid any normalization)
    const idx = req.url.indexOf('/proxy/');
    const encoded = req.url.substring(idx + '/proxy/'.length);
    let target;
    try { target = decodeURIComponent(encoded); }
    catch (e) { target = ''; }

    if (!target || !/^https?:\/\//i.test(target)) {
        res.writeHead(400, { ...CORS_HEADERS, 'Content-Type': 'text/plain; charset=utf-8' });
        res.end('Bad Request: Invalid target URL');
        log(C.red, '[PROXY] 400 Bad target');
        return;
    }

    log(C.yellow, `[PROXY] ${req.method} -> ${target}`);

    let upstreamUrl;
    try { upstreamUrl = new URL(target); }
    catch (e) {
        res.writeHead(400, { ...CORS_HEADERS, 'Content-Type': 'text/plain; charset=utf-8' });
        res.end('Bad Request: URL parse error');
        return;
    }

    // Build forwarded headers (allowlist only)
    const fwdHeaders = {};
    for (const name of PROXY_ALLOW_HEADERS) {
        const v = req.headers[name];
        if (v) fwdHeaders[name] = v;
    }

    const lib = upstreamUrl.protocol === 'https:' ? https : http;
    const options = {
        method: req.method,
        hostname: upstreamUrl.hostname,
        port: upstreamUrl.port || (upstreamUrl.protocol === 'https:' ? 443 : 80),
        path: upstreamUrl.pathname + upstreamUrl.search,
        headers: fwdHeaders,
    };

    const upstreamReq = lib.request(options, (upstreamRes) => {
        const status = upstreamRes.statusCode || 502;
        const outHeaders = { ...CORS_HEADERS };
        if (upstreamRes.headers['content-type']) outHeaders['Content-Type'] = upstreamRes.headers['content-type'];
        // Do not forward content-length: we relay chunked.
        res.writeHead(status, outHeaders);

        let errorBody = '';
        upstreamRes.on('data', (chunk) => {
            if (status >= 400) {
                try { errorBody += chunk.toString('utf8'); } catch (_) { }
            }
            res.write(chunk);
        });
        upstreamRes.on('end', () => {
            res.end();
            if (status >= 400 && errorBody) {
                log(C.red, `[PROXY] Error body: ${errorBody}`);
            }
            const color = status >= 400 ? C.red : C.dim;
            log(color, `[PROXY] ${status} <- ${target}`);
        });
        upstreamRes.on('error', (err) => {
            log(C.red, `[PROXY] upstream stream error: ${err.message}`);
            try { res.end(); } catch (_) { }
        });
    });

    upstreamReq.on('error', (err) => {
        log(C.red, `[PROXY] 502 Error: ${err.message}`);
        if (!res.headersSent) {
            res.writeHead(502, { ...CORS_HEADERS, 'Content-Type': 'text/plain; charset=utf-8' });
            res.end(`Proxy Error: ${err.message}`);
        } else {
            try { res.end(); } catch (_) { }
        }
    });

    upstreamReq.setTimeout(120000, () => {
        log(C.red, '[PROXY] upstream timeout');
        upstreamReq.destroy(new Error('upstream timeout'));
    });

    // Forward body
    req.pipe(upstreamReq);
}

function handleStatic(req, res) {
    let urlPath;
    try { urlPath = decodeURIComponent(req.url.split('?')[0]); }
    catch (_) { urlPath = req.url.split('?')[0]; }

    if (urlPath === '/') urlPath = '/index.html';

    // Resolve and prevent directory traversal
    const relative = urlPath.replace(/^\/+/, '').split('/').join(path.sep);
    const filePath = path.resolve(ROOT, relative);
    if (!filePath.startsWith(path.resolve(ROOT))) {
        res.writeHead(403); res.end('403 Forbidden');
        return;
    }

    fs.stat(filePath, (err, stat) => {
        if (err || !stat.isFile()) {
            res.writeHead(404, { 'Content-Type': 'text/plain; charset=utf-8' });
            res.end('404 Not Found');
            log(C.red, `[404] ${req.url}`);
            return;
        }

        const ext = path.extname(filePath).toLowerCase();
        let contentType = MIME[ext] || 'application/octet-stream';
        const headers = {
            'Cross-Origin-Opener-Policy': 'same-origin',
            'Cross-Origin-Embedder-Policy': 'credentialless',
        };

        if (ext === '.br' || ext === '.gz') {
            const base = filePath.substring(0, filePath.length - ext.length);
            const originalExt = path.extname(base).toLowerCase();
            if (MIME[originalExt]) contentType = MIME[originalExt];
            headers['Content-Encoding'] = ext === '.br' ? 'br' : 'gzip';
        }

        headers['Content-Type'] = contentType;
        headers['Content-Length'] = stat.size;

        res.writeHead(200, headers);
        const stream = fs.createReadStream(filePath);
        stream.pipe(res);
        stream.on('error', () => { try { res.end(); } catch (_) { } });

        let color = C.gray;
        if (ext === '.html') color = C.cyan;
        else if (ext === '.wasm') color = C.yellow;
        log(color, `[200] ${req.url}`);
    });
}

const server = http.createServer((req, res) => {
    const pathname = req.url.split('?')[0];
    if (pathname.startsWith('/proxy/') || (pathname === '/proxy' && req.method === 'OPTIONS')) {
        handleProxy(req, res);
        return;
    }
    handleStatic(req, res);
});

server.on('error', (err) => {
    if (err.code === 'EADDRINUSE') {
        log(C.red, `ERROR: Port ${PORT} is already in use.`);
        console.log('Close the other server or set PORT env var.');
        process.exit(1);
    }
    log(C.red, `Server error: ${err.message}`);
    process.exit(1);
});

server.listen(PORT, 'localhost', () => {
    log(C.green, `Server running at http://localhost:${PORT}`);
    console.log(`Serving files from: ${ROOT}`);
    log(C.yellow, 'CORS proxy available at /proxy/');
    console.log('');
    openBrowser(`http://localhost:${PORT}`);
});
