# Attribution and license

Windows adaptation created 2026-10-07. Distributed under GNU GPL version 2 only; see LICENSE. Complete Windows adaptation sources and build script are included. No warranty.

Protocol and encoding derived from SpliX:
- Copyright 2006–2008 Aurélien Croc (AP²C) and contributors.
- Algorithms 0x0D and 0x0E written by Leonardo Hamada.
- https://github.com/OpenPrinting/splix
- Base commit: 4854286334346059e7fec6f5f2328a23fa5fa774.
- Original relevant source files retained in reference/splix; they are reference material and are not linked or built into the executables.
- The Windows encoder uses bounded encoders with the upstream 0x0D and 0x0E packet formats and selects 0x0D first. It never writes padding to the source bitmap and does not carry over the CUPS cache or thread implementation.

User-provided source:
- samsung-scx4521f-mac-driver-main, app version 2.1.1, GPL-2.0.
- Original PPD and patch retained as reference; macOS executables, SwiftUI app, SANE, and libusb are not redistributed or used by this Windows program.

PDF rendering uses the operating system's Windows.Data.Pdf API through Windows PowerShell. The GUI uses .NET Framework Windows Forms and GDI+. OS components are not redistributed.

The Samsung and SCX-4521F names identify the hardware. This adaptation is not affiliated with Samsung or HP.
