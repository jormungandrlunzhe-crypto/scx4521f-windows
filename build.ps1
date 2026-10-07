$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework C# compiler not found.' }
$root = $PSScriptRoot
$sources = @("$root\src\SCX4521F.cs", "$root\src\PrintWorkflow.cs", "$root\src\QueueMonitor.cs", "$root\src\MainWindow.cs")
& $compiler /nologo /optimize+ /target:winexe /platform:x64 /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /out:"$root\SCX4521F.exe" $sources
if ($LASTEXITCODE -ne 0) { throw 'GUI build failed.' }
& $compiler /nologo /optimize+ /target:exe /platform:x64 /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /out:"$root\SCX4521F-cli.exe" $sources
if ($LASTEXITCODE -ne 0) { throw 'CLI build failed.' }
& $compiler /nologo /optimize+ /target:exe /platform:x64 /reference:"$root\SCX4521F-cli.exe" /reference:System.Drawing.dll /out:"$root\SCX4521F-usb.exe" "$root\src\UsbDirect.cs"
if ($LASTEXITCODE -ne 0) { throw 'USB test build failed.' }
Write-Output 'Build complete.'
