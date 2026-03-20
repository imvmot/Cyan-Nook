param(
    [int]$Port = 8080
)

$root = Split-Path -Parent $MyInvocation.MyCommand.Path

$mime = @{
    '.html'        = 'text/html'
    '.js'          = 'application/javascript'
    '.mjs'         = 'application/javascript'
    '.css'         = 'text/css'
    '.json'        = 'application/json'
    '.wasm'        = 'application/wasm'
    '.data'        = 'application/octet-stream'
    '.png'         = 'image/png'
    '.jpg'         = 'image/jpeg'
    '.svg'         = 'image/svg+xml'
    '.ico'         = 'image/x-icon'
    '.gif'         = 'image/gif'
    '.webp'        = 'image/webp'
    '.webmanifest' = 'application/manifest+json'
    '.mp3'         = 'audio/mpeg'
    '.wav'         = 'audio/wav'
    '.ogg'         = 'audio/ogg'
    '.woff'        = 'font/woff'
    '.woff2'       = 'font/woff2'
    '.ttf'         = 'font/ttf'
    '.txt'         = 'text/plain'
    '.xml'         = 'application/xml'
    '.unityweb'    = 'application/octet-stream'
    '.br'          = 'application/octet-stream'
    '.gz'          = 'application/octet-stream'
}

$listener = New-Object System.Net.HttpListener
$listener.Prefixes.Add("http://localhost:${Port}/")

try {
    $listener.Start()
} catch {
    Write-Host "ERROR: Port $Port is already in use." -ForegroundColor Red
    Write-Host "Close the other server or change PORT in start-server.bat."
    Read-Host "Press Enter to exit"
    exit 1
}

Write-Host "Server running at http://localhost:$Port" -ForegroundColor Green
Write-Host "Serving files from: $root"
Write-Host ""

# Open browser
Start-Process "http://localhost:$Port"

try {
    while ($listener.IsListening) {
        $ctx = $listener.GetContext()
        $req = $ctx.Request
        $res = $ctx.Response

        $urlPath = [System.Uri]::UnescapeDataString($req.Url.AbsolutePath)
        if ($urlPath -eq '/') { $urlPath = '/index.html' }

        $filePath = Join-Path $root ($urlPath.TrimStart('/').Replace('/', '\'))

        if (Test-Path $filePath -PathType Leaf) {
            $ext = [System.IO.Path]::GetExtension($filePath).ToLower()
            $contentType = 'application/octet-stream'
            if ($mime.ContainsKey($ext)) { $contentType = $mime[$ext] }

            # Handle compressed files (.br / .gz)
            if ($ext -eq '.br' -or $ext -eq '.gz') {
                $basePath = $filePath.Substring(0, $filePath.Length - $ext.Length)
                $originalExt = [System.IO.Path]::GetExtension($basePath).ToLower()
                if ($mime.ContainsKey($originalExt)) { $contentType = $mime[$originalExt] }
                if ($ext -eq '.br') { $res.Headers.Set('Content-Encoding', 'br') }
                if ($ext -eq '.gz') { $res.Headers.Set('Content-Encoding', 'gzip') }
            }

            $res.ContentType = $contentType
            $res.Headers.Set('Cross-Origin-Opener-Policy', 'same-origin')
            $res.Headers.Set('Cross-Origin-Embedder-Policy', 'credentialless')

            $bytes = [System.IO.File]::ReadAllBytes($filePath)
            $res.ContentLength64 = $bytes.Length
            try {
                $res.OutputStream.Write($bytes, 0, $bytes.Length)
                $res.OutputStream.Close()
            } catch {
                # クライアント切断時のI/Oエラーを無視
            }

            $color = 'Gray'
            if ($ext -eq '.html') { $color = 'Cyan' }
            elseif ($ext -eq '.wasm') { $color = 'Yellow' }
            Write-Host "[200] $($req.Url.AbsolutePath)" -ForegroundColor $color
        } else {
            $res.StatusCode = 404
            $msg = [System.Text.Encoding]::UTF8.GetBytes('404 Not Found')
            $res.ContentLength64 = $msg.Length
            try {
                $res.OutputStream.Write($msg, 0, $msg.Length)
                $res.OutputStream.Close()
            } catch {
                # クライアント切断時のI/Oエラーを無視
            }
            Write-Host "[404] $($req.Url.AbsolutePath)" -ForegroundColor Red
        }
    }
} finally {
    $listener.Stop()
}
