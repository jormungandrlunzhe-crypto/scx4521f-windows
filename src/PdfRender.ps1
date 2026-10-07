param()
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
Add-Type -AssemblyName System.Runtime.WindowsRuntime
$null = [Windows.Storage.StorageFile,Windows.Storage,ContentType=WindowsRuntime]
$null = [Windows.Data.Pdf.PdfDocument,Windows.Data.Pdf,ContentType=WindowsRuntime]
$null = [Windows.Storage.Streams.InMemoryRandomAccessStream,Windows.Storage.Streams,ContentType=WindowsRuntime]
$null = [Windows.Data.Pdf.PdfPageRenderOptions,Windows.Data.Pdf,ContentType=WindowsRuntime]
$asTask = [System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object { $_.Name -eq 'AsTask' -and $_.IsGenericMethod -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' } | Select-Object -First 1
$asActionTask = [System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object { $_.Name -eq 'AsTask' -and -not $_.IsGenericMethod -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncAction' } | Select-Object -First 1
function Await-Operation($operation, [Type]$resultType) {
    $task = $asTask.MakeGenericMethod($resultType).Invoke($null, @($operation))
    $task.GetAwaiter().GetResult()
}
try {
    $file = Await-Operation ([Windows.Storage.StorageFile]::GetFileFromPathAsync($env:SCX_PDF_INPUT)) ([Windows.Storage.StorageFile])
    $document = Await-Operation ([Windows.Data.Pdf.PdfDocument]::LoadFromFileAsync($file)) ([Windows.Data.Pdf.PdfDocument])
    $index = [int]$env:SCX_PDF_PAGE
    if ($index -lt 0) { [Console]::WriteLine($document.PageCount); exit 0 }
    if ($index -ge $document.PageCount) { throw 'PDF page index is outside the document.' }
    $page = $document.GetPage([uint32]$index)
    $memory = New-Object Windows.Storage.Streams.InMemoryRandomAccessStream
    try {
        $options = New-Object Windows.Data.Pdf.PdfPageRenderOptions
        $scale = [Math]::Min(([double]$env:SCX_PDF_WIDTH / $page.Size.Width), ([double]$env:SCX_PDF_HEIGHT / $page.Size.Height))
        $options.DestinationWidth = [uint32][Math]::Max(1,[Math]::Round($page.Size.Width * $scale))
        $options.DestinationHeight = [uint32][Math]::Max(1,[Math]::Round($page.Size.Height * $scale))
        $action = $page.RenderToStreamAsync($memory,$options)
        $asActionTask.Invoke($null,@($action)).GetAwaiter().GetResult()
        $memory.Seek(0)
        $stream = [System.IO.WindowsRuntimeStreamExtensions]::AsStreamForRead($memory)
        $output = [System.IO.File]::Create($env:SCX_PDF_OUTPUT)
        try { $stream.CopyTo($output) } finally { $output.Dispose(); $stream.Dispose() }
    } finally { $page.Dispose(); $memory.Dispose() }
    [Console]::WriteLine('OK')
} catch { [Console]::Error.WriteLine($_.Exception.ToString()); exit 1 }
