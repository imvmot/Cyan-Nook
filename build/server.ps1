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

$script:proxyHeaders = "Content-Type, Authorization, x-api-key, anthropic-version, x-goog-api-key"
$script:allowList = @("Content-Type","Authorization","x-api-key","anthropic-version","x-goog-api-key")

$listener = New-Object System.Net.HttpListener
$listener.Prefixes.Add("http://localhost:${Port}/")

try { $listener.Start() }
catch {
    Write-Host "ERROR: Port $Port is already in use." -ForegroundColor Red
    Write-Host "Close the other server or change PORT in start-server.bat."
    Read-Host "Press Enter to exit"
    exit 1
}

Write-Host "Server running at http://localhost:$Port" -ForegroundColor Green
Write-Host "Serving files from: $root"
Write-Host "CORS proxy available at /proxy/" -ForegroundColor Yellow
Write-Host ""

Start-Process "http://localhost:$Port"

function Add-Cors($r) {
    $r.Headers.Set("Access-Control-Allow-Origin", "*")
    $r.Headers.Set("Access-Control-Allow-Methods", "GET, POST, PUT, DELETE, OPTIONS")
    $r.Headers.Set("Access-Control-Allow-Headers", $script:proxyHeaders)
    $r.Headers.Set("Access-Control-Max-Age", "86400")
}

function Handle-Proxy($ctx) {
    $req = $ctx.Request
    $res = $ctx.Response

    # Preflight
    if ($req.HttpMethod -eq "OPTIONS") {
        Add-Cors $res
        $res.StatusCode = 204
        $res.Close()
        Write-Host "[PROXY] OPTIONS preflight" -ForegroundColor DarkYellow
        return
    }

    # Extract target URL from RawUrl (AbsolutePathは//を/に正規化してしまうため)
    $raw = $req.RawUrl
    $idx = $raw.IndexOf("/proxy/")
    $encoded = $raw.Substring($idx + "/proxy/".Length)
    $target = [System.Uri]::UnescapeDataString($encoded)

    if (-not $target -or -not $target.StartsWith("http")) {
        Add-Cors $res
        $res.StatusCode = 400
        $b = [System.Text.Encoding]::UTF8.GetBytes("Bad Request: Invalid target URL")
        $res.ContentLength64 = $b.Length
        $res.OutputStream.Write($b, 0, $b.Length)
        $res.OutputStream.Close()
        Write-Host "[PROXY] 400 Bad target" -ForegroundColor Red
        return
    }

    Write-Host "[PROXY] $($req.HttpMethod) -> $target" -ForegroundColor Yellow

    $webRes = $null
    $stream = $null
    try {
        # Build outgoing request
        $wr = [System.Net.HttpWebRequest]::Create($target)
        $wr.Method = $req.HttpMethod
        $wr.Timeout = 120000
        $wr.ReadWriteTimeout = 120000

        # Forward allowed headers
        foreach ($h in $script:allowList) {
            $v = $req.Headers[$h]
            if ($v) {
                if ($h -eq "Content-Type") { $wr.ContentType = $v }
                else { $wr.Headers[$h] = $v }
            }
        }

        # Forward body
        if ($req.HasEntityBody) {
            $ws = $wr.GetRequestStream()
            $req.InputStream.CopyTo($ws)
            $ws.Close()
        }

        # Get response (including error responses)
        try { $webRes = $wr.GetResponse() }
        catch [System.Net.WebException] {
            $webRes = $_.Exception.Response
            if (-not $webRes) { throw }
        }

        # Send response with CORS
        Add-Cors $res
        $statusCode = [int]$webRes.StatusCode
        $res.StatusCode = $statusCode
        if ($webRes.ContentType) { $res.ContentType = $webRes.ContentType }
        $res.SendChunked = $true

        # Stream relay (エラーレスポンスの場合はボディをログ出力)
        $stream = $webRes.GetResponseStream()
        $buf = New-Object byte[] 4096
        $go = $true
        $errorBody = ""
        while ($go) {
            $n = $stream.Read($buf, 0, $buf.Length)
            if ($n -le 0) { $go = $false }
            else {
                $res.OutputStream.Write($buf, 0, $n)
                $res.OutputStream.Flush()
                if ($statusCode -ge 400) {
                    $errorBody += [System.Text.Encoding]::UTF8.GetString($buf, 0, $n)
                }
            }
        }

        if ($statusCode -ge 400 -and $errorBody) {
            Write-Host "[PROXY] Error body: $errorBody" -ForegroundColor Red
        }
        Write-Host "[PROXY] $statusCode <- $target" -ForegroundColor DarkYellow
    }
    catch {
        Write-Host "[PROXY] 502 Error: $($_.Exception.Message)" -ForegroundColor Red
        try {
            Add-Cors $res
            $res.StatusCode = 502
            $eb = [System.Text.Encoding]::UTF8.GetBytes("Proxy Error: $($_.Exception.Message)")
            $res.ContentLength64 = $eb.Length
            $res.OutputStream.Write($eb, 0, $eb.Length)
        }
        catch { }
    }
    finally {
        if ($stream) { try { $stream.Close() } catch { } }
        if ($webRes) { try { $webRes.Close() } catch { } }
        try { $res.OutputStream.Close() } catch { }
    }
}

# Main loop
try {
    while ($listener.IsListening) {
        $ctx = $listener.GetContext()
        $req = $ctx.Request
        $res = $ctx.Response
        $urlPath = [System.Uri]::UnescapeDataString($req.Url.AbsolutePath)

        # Proxy
        if ($urlPath.StartsWith("/proxy/") -or ($urlPath -eq "/proxy" -and $req.HttpMethod -eq "OPTIONS")) {
            Handle-Proxy $ctx
            continue
        }

        # Static files
        if ($urlPath -eq "/") { $urlPath = "/index.html" }
        $filePath = Join-Path $root ($urlPath.TrimStart("/").Replace("/", "\"))

        if (Test-Path $filePath -PathType Leaf) {
            $ext = [System.IO.Path]::GetExtension($filePath).ToLower()
            $contentType = "application/octet-stream"
            if ($mime.ContainsKey($ext)) { $contentType = $mime[$ext] }

            if ($ext -eq ".br" -or $ext -eq ".gz") {
                $basePath = $filePath.Substring(0, $filePath.Length - $ext.Length)
                $originalExt = [System.IO.Path]::GetExtension($basePath).ToLower()
                if ($mime.ContainsKey($originalExt)) { $contentType = $mime[$originalExt] }
                if ($ext -eq ".br") { $res.Headers.Set("Content-Encoding", "br") }
                if ($ext -eq ".gz") { $res.Headers.Set("Content-Encoding", "gzip") }
            }

            $res.ContentType = $contentType
            $res.Headers.Set("Cross-Origin-Opener-Policy", "same-origin")
            $res.Headers.Set("Cross-Origin-Embedder-Policy", "credentialless")

            $bytes = [System.IO.File]::ReadAllBytes($filePath)
            $res.ContentLength64 = $bytes.Length
            try {
                $res.OutputStream.Write($bytes, 0, $bytes.Length)
                $res.OutputStream.Close()
            }
            catch { }

            $color = "Gray"
            if ($ext -eq ".html") { $color = "Cyan" }
            elseif ($ext -eq ".wasm") { $color = "Yellow" }
            Write-Host "[200] $($req.Url.AbsolutePath)" -ForegroundColor $color
        }
        else {
            $res.StatusCode = 404
            $msg = [System.Text.Encoding]::UTF8.GetBytes("404 Not Found")
            $res.ContentLength64 = $msg.Length
            try {
                $res.OutputStream.Write($msg, 0, $msg.Length)
                $res.OutputStream.Close()
            }
            catch { }
            Write-Host "[404] $($req.Url.AbsolutePath)" -ForegroundColor Red
        }
    }
}
finally {
    $listener.Stop()
}
