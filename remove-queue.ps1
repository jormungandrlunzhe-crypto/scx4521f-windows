param([string]$QueueName = 'SCX4521F-RAW')
$ErrorActionPreference = 'Stop'
$printer = Get-Printer -Name $QueueName -ErrorAction SilentlyContinue
if (-not $printer) { Write-Output '指定队列不存在。'; exit 0 }
if ($printer.DriverName -ne 'Generic / Text Only' -or $printer.PortName -notmatch '^USB\d+$') {
    throw '指定队列不符合本工具创建的 Generic / Text Only USB 队列配置，拒绝删除。'
}
Remove-Printer -Name $QueueName -Confirm
