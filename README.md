# Samsung SCX-4521F Windows 打印工具 v0.3

基于 Samsung SCX-4521F Mac 项目的协议与 SpliX，实现可运行的 **Windows x64 打印工具**。双击 `SCX4521F.exe` 使用。保留整个文件夹，PDF 转换依赖其中的 `src/PdfRender.ps1`。

当前交付的是独立打印程序。它尚不能作为 Windows 系统驱动让 Word、浏览器等应用直接从打印菜单调用。可先将这些应用中的文档另存为 PDF，再用本工具打印。扫描功能未移植。

## 获取与构建

本仓库保留源码和测试材料。可运行工具和源码快照放在 [downloads](downloads/) 目录：下载 [v0.3 工具包](downloads/SCX4521F-Windows-v0.3.zip) 后解压整个文件夹；校验值见 [SHA256SUMS.txt](downloads/SHA256SUMS.txt)。

从源码构建：在 Windows 10/11 x64 的 Windows PowerShell 5.1 中运行 `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1`，会在项目根目录生成三个 EXE。构建仅使用系统 .NET Framework 编译器，无需 Visual Studio。

离线回归测试：`powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\run.ps1 -PythonPath "Python3.exe 完整路径"`。测试依赖 Python 3.10 或更高版本，解码器只用标准库，不向打印机提交任务。

## 支持范围

- PDF（全部页面或指定页码）、PNG、JPEG、BMP、多页 TIFF。
- 600 DPI 黑白输出，灰阶通过抖动转换；单面或两阶段手动双面打印，1–99 份，逐份整理。
- A4、Letter、Legal、A5、A6；输入内容等比例缩放、居中放入纸张可打印区域。
- USB 直连打印，无需管理员权限或建立打印队列；也可生成 QPDL 文件或通过已有 Windows RAW 队列发送。
- 中文界面和中文测试页；使用 Windows 自带 PDF 渲染器，不需要 Python、Ghostscript 或 CUPS。
- 运行需要 Windows 10/11 x64、.NET Framework 4.8 和 Windows PowerShell 5.1。本机已成功编译并运行转换功能。

## 第一次连接打印机

2026-10-07 已在本机真实 SCX-4521F 上完成简单页面与中文／灰阶测试页打印，用户确认出纸正常。程序启动后自动识别设备，并默认选中“USB 直连 · Samsung SCX-4521F”，可直接打印 PDF 和图片，无需创建打印队列。

保留三个 EXE 和 `src/PdfRender.ps1`。USB 发送由 `SCX4521F-usb.exe` 完成，它先查询真实设备的 IEEE1284 标识，核实 Samsung SCX-4x21／4521 型号，再发送完整转换好的文件。多个匹配设备时会停止。

```powershell
.\SCX4521F-usb.exe probe  # 查询真实设备，不打印
.\SCX4521F-usb.exe test   # 发送一张 A4 测试页
.\SCX4521F-usb.exe test-simple # 只打印边框与三条横线，用于排查兼容性
.\SCX4521F-usb.exe send .\document.qpdl # 发送已经转换的文档
```

设备未返回 IDLE 时，工具会拒绝发送新任务。USB 直连任务在“本工具任务”中监控，可停止继续发送并请求清除设备任务。不应与其他打印程序同时向同一设备发送。`reset` 可请求 USB 软件复位，但不保证清除打印机内部任务；任务挂起时可用面板停止键或重新开机恢复。

1. 用 USB 连接 SCX-4521F，开机，装入 A4 纸。
2. 双击 `SCX4521F.exe`，确认打印目标为“USB 直连 · Samsung SCX-4521F”。未识别时点“刷新”。也可选择已经连接到本机 SCX-4521F 的队列；不要选择 PDF、XPS、Fax 或其他打印机。
3. 点“打印测试页”，观察纸张、中文字、边框与灰阶是否正常。程序显示“任务已提交”只表示 Windows 接受了数据；实际出纸情况需要观察打印机。
4. 测试页成功后，选择 PDF 或图片，设置页码、份数和打印模式，点“加入打印任务”。

## 页码、份数和双面打印

页码范围留空表示全部；可输入 `2`、`1,3,5-8`，重复页码自动去重并按升序排列。奇偶页筛选按原文页码计算。单面可选择逐份整理或逐页多份，以及逆序输出。双面始终按整份安排纸张。

“双面：翻纸后继续”是程序自动安排页序、人工回装纸张的两阶段流程：

1. 首次使用点击“双面走纸校准”，程序先打印两张正面。等纸全部输出，按界面提示将整叠纸的空白面朝上放回纸盘，保持纸叠顺序。
2. 点击“纸已放回，继续打印背面”。正确校准结果为一张纸正面 1、背面 2，另一张正面 3、背面 4，上下箭头一致。
3. 若页码配对错误，切换“背面逆序”；若背面倒置，切换“校准：背面再转 180°”，重新校准。设置目前需要每次打开程序重新选择。
4. 正式打印时选“双面”及长边／短边装订，输入页码和份数。程序会显示正背面页序；选中页数为奇数时自动补空白背面，翻纸时保留整叠纸。

例如选择 `1,3-4` 时，每份第一张是 1／3，第二张是 4／空白。配对依据选中页面的先后顺序。此功能需要手动翻纸，不能实现硬件自行翻面。回装方向和长文档双面仍需本机实测，建议先完成两张纸的校准。

导出双面打印数据会生成 `名称-正面.qpdl`、`名称-背面.qpdl`、`名称-翻纸说明.txt` 三个文件。

## 任务监控与终止

- “本工具任务”显示等待、准备、发送页数、等待翻纸、失败等状态。可连续加入多个任务，按加入顺序处理。
- “暂停等待队列”阻止下一个任务开始，当前任务继续；“停止选中任务”可取消尚未开始的任务，或停止当前任务。停止转换时不会提交不完整文件；停止发送时完成当前页的数据收尾后停止后续页。
- “清除设备任务”暂停等待队列，停止当前任务，然后尝试 USB 软件复位及 PJL RESET，再读取设备状态。打印机内部已经接收的页面可能继续出纸；状态仍为忙碌时需要使用面板停止键或重新开机。软件清除效果尚未实机验证。
- 选择 Windows 队列后，在“Windows 打印队列”页查看系统任务，暂停、恢复或删除选中的任务。操作权限由 Windows 队列决定；只控制选中任务。
- USB 直连任务不会出现在 Windows 队列中。界面的发送页数表示数据提交进度，不能判断纸面是否已经成功打印。设备状态约每 3 秒更新，传输期间减少探测以避免接口争用；任务失败不会自动重印。

纸盘方向依据 [SCX-4x21 官方手册](https://h10032.www1.hp.com/ctg/Manual/c05786751.pdf)，Windows 队列操作依据 [Microsoft SetJob](https://learn.microsoft.com/en-us/windows/win32/printdocs/setjob)。

### 可选：使用 Windows 打印队列

本机已验证的默认路径是 USB 直连，可以跳过本节。需要通过已有 Windows 队列发送时，可使用下列脚本；该安装路径尚未实测。

只有 USB 打印端口已经由 Windows 的 USB 打印支持建立时才能创建队列。**USB001 是示例，不能默认认定它属于 SCX-4521F。** 多台 USB 打印机同时连接时，应先在打印机属性“端口”页确认。

在管理员 **Windows PowerShell** 中进入此文件夹，先查看 USB 端口：

```powershell
Get-PrinterPort | Where-Object Name -match '^USB\d+$' | Format-Table Name,Description
```

确认端口后运行，例如：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\setup-queue.ps1 -PortName USB001
```

脚本使用 Windows 自带的 `Generic / Text Only` 驱动在指定的已存在 USB 端口上建立 `SCX4521F-RAW` 队列。本工具向该队列发送已转换的 QPDL 数据。**直接从其他应用向这个 Generic 队列打印普通文档不会完成转换。** 脚本不会覆盖已有队列，也不会猜测或新建 USB 端口。

如果找不到 USB 端口，或 Windows 无法加载 `Generic / Text Only`，请先完成 Windows 的 USB 打印支持／添加打印机步骤。此交付包不含 USB 内核驱动，也不能修复 USB 设备枚举失败。

移除此工具创建的队列：在管理员 Windows PowerShell 中运行 `remove-queue.ps1`，它会请求删除确认。保留现有厂商驱动时优先使用已有队列。

## 暂时不连接打印机

现在可以打开程序，选择 PDF 或图片，点“导出 QPDL”。导出的文件是专供 SCX-4521F 的二进制打印数据，不能作为 PDF 打开。

## 命令行

```powershell
.\SCX4521F-cli.exe list
.\SCX4521F-cli.exe test .\test.qpdl A4
.\SCX4521F-cli.exe convert "D:\文件\文档.pdf" .\document.qpdl A4 1
.\SCX4521F-cli.exe send "SCX4521F-RAW" .\document.qpdl
```

`send` 的第一个参数必须是打印队列名称，第二个参数是已经转换完成的 QPDL 文件。此版本先完整转换到临时文件，再提交打印任务；转换失败不会发送半个作业。PDF 逐页转换，不同时将所有页面放入内存。

直接 USB 打印文档：先执行上述 `convert`，再运行 `.\SCX4521F-usb.exe send .\document.qpdl`。

## 已验证与待验证

离线通过：C# 编译、USB 自动识别／默认选择／界面布局、实际队列枚举、Windows 原生 PDF 渲染、双页 PDF、多页 TIFF、图片转换、12 种压缩输入的独立解码、纸张头／分页／份数／校验和／结束标记检查、输入覆盖和无效份数拒绝。实机通过：简单测试页、中文和灰阶测试页。另已通过主程序使用的转换和发送流程，将一份单页 PDF 完整传至打印机。详见 `TEST-REPORT.md`。

**长文档、其他纸张尺寸、断线／取消恢复和队列安装脚本仍待实机验证。** 尚未制作系统打印驱动 INF/DLL 包或进行驱动签名，也未实现扫描、传真或硬件自动双面。v0.3 手动回装双面和任务控制已经完成离线验证，实机校准、队列暂停／删除和软件清除仍待验证。原 Mac 项目的扫描功能本身亦标注尚未成功。

如果任务留在队列中，请先检查打印机在线状态和端口是否正确。必要时在 Windows 队列中取消任务，待设备空闲后再试一张测试页。请不要一次提交大量文档进行首次验证。

## 技术与源码

Mac 的 SwiftUI、CUPS、Mach-O 二进制和 SANE 调用无法直接用于 Windows，因此界面、文件渲染和队列发送均使用 C#／Windows API 重写。QPDL v1 的页头、带记录、校验和以及 0x0E 编码格式依据原项目指定的 SpliX 提交 `4854286334346059e7fec6f5f2328a23fa5fa774`。保留原 PPD 的参数：600 DPI、128 行带、页头 `00 02 01`、纸张代码和硬件边距。0x0E 是 SpliX 的 0x0D 备用格式；当前版本优先使用 0x0D，达到复杂度限制时回退 0x0E。编码包选择可能不同于 SpliX，未声称输出字节逐一相同。

原理：PDF／图片 → Windows 渲染 → 黑白光栅 → QPDL v1（0x0D 优先，0x0E 回退）→ Windows USB 打印接口，或 Winspool RAW 队列。

源码在 `src/`，包括 `SCX4521F.cs`、`PrintWorkflow.cs`、`MainWindow.cs`、`QueueMonitor.cs`、`PdfRender.ps1` 和 `UsbDirect.cs`。运行 `build.ps1` 可重新生成三个 EXE，无需 Visual Studio。程序不自行请求管理员权限；仅建立队列需要管理员权限。

参考：

- [SpliX 上游](https://github.com/OpenPrinting/splix)，相关原始文件在 `reference/`。
- [Microsoft StartDocPrinter](https://learn.microsoft.com/en-us/windows/win32/printdocs/startdocprinter)。
- [Microsoft WritePrinter](https://learn.microsoft.com/en-us/windows/win32/printdocs/writeprinter)。

本移植版为 GPL-2.0-only，源码与许可证随包提供。版权与第三方说明见 `THIRD-PARTY.md`。未经 Samsung／HP 签名或认证。
