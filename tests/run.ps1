param([Parameter(Mandatory=$true)][string]$PythonPath)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$testDir = Join-Path ([IO.Path]::GetTempPath()) ('SCX4521F-tests-' + [guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $testDir
& "$root\build.ps1"
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
Copy-Item -LiteralPath "$root\SCX4521F-cli.exe" -Destination $testDir
Copy-Item -LiteralPath "$root\SCX4521F-usb.exe" -Destination $testDir
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:exe /platform:x64 /reference:"$root\SCX4521F-cli.exe" /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /out:"$testDir\BandVectors.exe" "$PSScriptRoot\BandVectors.cs"
if ($LASTEXITCODE -ne 0) { throw 'Test harness build failed' }
& "$testDir\BandVectors.exe" "$testDir\vectors"
if ($LASTEXITCODE -ne 0) { throw 'Fixture generation failed' }
$cli = "$root\SCX4521F-cli.exe"
& $cli test "$testDir\test-a4.qpdl" A4
if ($LASTEXITCODE -ne 0) { throw 'A4 test conversion failed' }
& $cli convert "$PSScriptRoot\data\two-pages.pdf" "$testDir\pdf.qpdl"
if ($LASTEXITCODE -ne 0) { throw 'PDF conversion failed' }
& $cli convert "$PSScriptRoot\data\input.png" "$testDir\image.qpdl"
if ($LASTEXITCODE -ne 0) { throw 'Image conversion failed' }
& $cli convert "$PSScriptRoot\data\two-pages.tiff" "$testDir\tiff.qpdl" A5 2
if ($LASTEXITCODE -ne 0) { throw 'TIFF conversion failed' }
& $PythonPath "$PSScriptRoot\verify.py" "$testDir\vectors" "$testDir\test-a4.qpdl" "$testDir\pdf.qpdl" "$testDir\image.qpdl" "$testDir\tiff.qpdl"
if ($LASTEXITCODE -ne 0) { throw 'Independent decoding failed' }
$null = New-Item -ItemType Directory -Path "$testDir\src"
Copy-Item -LiteralPath "$root\src\PdfRender.ps1" -Destination "$testDir\src"
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:exe /platform:x64 /reference:"$root\SCX4521F-cli.exe" /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /out:"$testDir\WorkflowTests.exe" "$PSScriptRoot\WorkflowTests.cs"
if ($LASTEXITCODE -ne 0) { throw 'Workflow harness build failed' }
& "$testDir\WorkflowTests.exe" "$testDir\workflow" "$PSScriptRoot\data\two-pages.pdf"
if ($LASTEXITCODE -ne 0) { throw 'Workflow tests failed' }
& $PythonPath "$PSScriptRoot\verify_workflow.py" "$testDir\workflow"
if ($LASTEXITCODE -ne 0) { throw 'Workflow independent verification failed' }
Write-Output "PASS. Inspect artifacts at $testDir. No print job was sent."
