param([string]$PortName, [string]$QueueName = 'SCX4521F-RAW')
$ErrorActionPreference = 'Stop'
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw '请在管理员 Windows PowerShell 中运行此脚本。只创建打印队列，不安装自制内核驱动。'
}
$ports = @(Get-PrinterPort | Where-Object { $_.Name -match '^USB\d+$' })
if (-not $PortName) {
    $ports | Format-Table Name, Description
    throw '先连接并打开打印机。确认对应 USB 端口后，用 -PortName USB001（替换为实际端口）重试。可在打印机属性的“端口”页确认。'
}
if ($PortName -notmatch '^USB\d+$' -or -not ($ports.Name -contains $PortName)) {
    throw '指定端口不是已存在的 USB 打印端口。不会创建或猜测 USB 端口。'
}
if (Get-Printer -Name $QueueName -ErrorAction SilentlyContinue) { throw '同名打印队列已存在，请直接使用或指定不同的 -QueueName。'
}
$driver = 'Generic / Text Only'
if (-not (Get-PrinterDriver -Name $driver -ErrorAction SilentlyContinue)) {
    Add-PrinterDriver -Name $driver
}
Add-Printer -Name $QueueName -DriverName $driver -PortName $PortName
Get-Printer -Name $QueueName | Format-Table Name, DriverName, PortName
Write-Output '队列已创建。通过 SCX4521F.exe 打印测试页；应用直接向此队列提交普通文档不会进行 QPDL 转换。'
