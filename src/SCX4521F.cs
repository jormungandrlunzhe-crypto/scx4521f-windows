// GPL-2.0-only. Windows adaptation, 2026.
// QPDL records and 0x0E format derived from SpliX (commit 4854286):
// Copyright 2006-2008 Aurelien Croc; codec by Leonardo Hamada.
// See LICENSE and THIRD-PARTY.md. No warranty.
using System;
using System.IO;
using System.Text;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Drawing2D;
using System.Drawing.Printing;
using System.Runtime.InteropServices;
using System.ComponentModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SCX4521F {
    public class Paper {
        public string Name; public int Code, Width, Height;
        public Paper(string name, int code, double pointsW, double pointsH) {
            Name=name; Code=code;
            Width=((int)Math.Ceiling(pointsW*600/72)+7)&~7;
            Height=(int)Math.Ceiling(pointsH*600/72);
        }
        public static Paper Get(string name) {
            switch(name.ToUpperInvariant()) {
                case "A4": return new Paper("A4",2,595,842);
                case "LETTER": return new Paper("Letter",0,612,792);
                case "LEGAL": return new Paper("Legal",1,612,1008);
                case "A5": return new Paper("A5",16,420,595);
                case "A6": return new Paper("A6",17,297,420);
                default: throw new ArgumentException("纸张只支持 A4、Letter、Legal、A5、A6。");
            }
        }
    }
    public static class Qpdl {
        // Hardware origin: ceil(10.75pt at 600dpi) rounded to whole bytes;
        // y origin ceil(15pt at 600dpi), matching source PPD and compress.cpp.
        public const int MarginX=96, MarginY=125, HardwareRowBytes=620, BandHeight=128;
        public static void U16(Stream s,int n) { s.WriteByte((byte)(n>>8)); s.WriteByte((byte)n); }
        public static void U32(Stream s,uint n) { s.WriteByte((byte)(n>>24)); s.WriteByte((byte)(n>>16)); s.WriteByte((byte)(n>>8)); s.WriteByte((byte)n); }
        static void Ascii(Stream s,string t) { byte[] b=Encoding.ASCII.GetBytes(t); s.Write(b,0,b.Length); }
        public static string Clean(string t) {
            StringBuilder b=new StringBuilder();
            foreach(char c in t) { if(c>=32 && c<=126) b.Append(c=='"'?'\'':c); if(b.Length==80) break; }
            return b.Length==0 ? "SCX4521F Windows" : b.ToString();
        }
        public static void Begin(Stream s,string title) {
            Ascii(s,"\x1b%-12345X@PJL SET USERNAME=\"Windows\"\n@PJL SET JOBNAME=\""+Clean(title)+"\"\n"+
                "@PJL SET JAMRECOVERY=OFF\n@PJL SET DUPLEX=OFF\n@PJL SET PAPERTYPE=OFF\n"+
                "@PJL SET ALTITUDE=LOW\n@PJL SET DENSITY=3\n@PJL SET RET=NORMAL\n@PJL ENTER LANGUAGE = QPDL\n");
        }
        public static void End(Stream s) { Ascii(s,"\t\x1b%-12345X"); }
        // SpliX's preferred 0x0D codec. Packets describe displacement from
        // the start of the previous black run, not from the run's end.
        // Input is white=FF; scan black pixels directly without mutating it.
        public static byte[] CompressD(byte[] data) {
            if(data.Length!=HardwareRowBytes*BandHeight) throw new ArgumentException("Invalid band size.");
            using(MemoryStream output=new MemoryStream()) {
                int previousX=0, previousY=0;
                for(int y=0;y<BandHeight;y++) {
                    long lineStart=output.Length; int x=0;
                    while(x<4960) {
                        while(x<4960 && (data[y*620+x/8]&(128>>(x&7)))!=0) x++;
                        if(x==4960) break;
                        int start=x;
                        while(x<4960 && (data[y*620+x/8]&(128>>(x&7)))==0) x++;
                        int run=x-start, dx=start-previousX, dy=y-previousY;
                        if(dx>=-128 && dx<=127 && run<=63 && dy<=1) {
                            output.WriteByte((byte)((dy<<6)|run)); output.WriteByte((byte)dx);
                        } else if(dx>=-8192 && dx<=8191 && run<=4095 && dy<=3) {
                            output.WriteByte((byte)(0x80|((dx>>8)&63))); output.WriteByte((byte)dx);
                            output.WriteByte((byte)(0x80|(dy<<4)|(run>>8))); output.WriteByte((byte)run);
                        } else {
                            int offset=dy*4960+dx;
                            output.WriteByte(0xc0); output.WriteByte((byte)(offset>>16)); output.WriteByte((byte)(offset>>8)); output.WriteByte((byte)offset);
                            output.WriteByte((byte)(0xc0|(run>>8))); output.WriteByte((byte)run);
                        }
                        previousX=start; previousY=y;
                        if(output.Length-lineStart>122 || output.Length+10>16388) return null;
                    }
                }
                int padding=4-(int)(output.Length&3);
                for(int i=0;i<padding;i++) output.WriteByte(0);
                return output.ToArray();
            }
        }
        // Input row-major, white=FF, black=00, exactly 620 bytes per row.
        // Each row is encoded independently; the printer's 0x0E wrap is 4960 px.
        public static byte[] Compress(byte[] data) {
            if(data.Length!=HardwareRowBytes*BandHeight) throw new ArgumentException("Invalid band size.");
            using(MemoryStream output=new MemoryStream()) {
                for(int y=0;y<BandHeight;y++) {
                    int pos=y*HardwareRowBytes, end=pos+HardwareRowBytes;
                    while(pos<end) {
                        int run=1; while(pos+run<end && data[pos+run]==data[pos]) run++;
                        if(run>=3) {
                            if(run<=65) output.WriteByte((byte)((1-run)&0x7f));
                            else U16(output,65537-run);
                            output.WriteByte(data[pos]); pos+=run;
                        } else {
                            int start=pos; pos+=run;
                            while(pos<end) {
                                run=1; while(pos+run<end && data[pos+run]==data[pos]) run++;
                                if(run>=3) break;
                                pos+=run;
                            }
                            U16(output,0x8000|(pos-start-1)); output.Write(data,start,pos-start);
                        }
                    }
                }
                while((output.Length&3)!=0) output.WriteByte(0);
                return output.ToArray();
            }
        }
        public static void Page(Stream s,Bitmap bitmap,Paper paper,int copies,CancellationToken token=default(CancellationToken)) {
            if(copies<1 || copies>99) throw new ArgumentOutOfRangeException("copies");
            if(bitmap.Width!=paper.Width || bitmap.Height!=paper.Height) throw new ArgumentException("Incorrect raster dimensions.");
            int height=paper.Height-MarginY;
            s.WriteByte(0); s.WriteByte(6); U16(s,copies); s.WriteByte((byte)paper.Code);
            U16(s,paper.Width); U16(s,height);
            byte[] rest={1,0,1,0,2,1,1,6}; s.Write(rest,0,rest.Length);
            BitmapData locked=bitmap.LockBits(new Rectangle(0,0,bitmap.Width,bitmap.Height),ImageLockMode.ReadOnly,PixelFormat.Format24bppRgb);
            try {
                byte[] row=new byte[paper.Width*3], band=new byte[HardwareRowBytes*BandHeight];
                // Ordered 8x8 dithering keeps grayscale and photographs printable.
                int[,] dither={{0,48,12,60,3,51,15,63},{32,16,44,28,35,19,47,31},{8,56,4,52,11,59,7,55},{40,24,36,20,43,27,39,23},{2,50,14,62,1,49,13,61},{34,18,46,30,33,17,45,29},{10,58,6,54,9,57,5,53},{42,26,38,22,41,25,37,21}};
                for(int n=0;n*BandHeight<height;n++) {
                    token.ThrowIfCancellationRequested();
                    for(int i=0;i<band.Length;i++) band[i]=255;
                    bool nonWhite=false;
                    for(int y=0;y<BandHeight;y++) {
                        int sy=MarginY+n*BandHeight+y; if(sy>=paper.Height) break;
                        Marshal.Copy(IntPtr.Add(locked.Scan0,sy*locked.Stride),row,0,row.Length);
                        int visible=Math.Min(4960,paper.Width-MarginX);
                        for(int x=0;x<visible;x++) {
                            int sx=x+MarginX,k=sx*3;
                            int gray=(row[k]*114+row[k+1]*587+row[k+2]*299)/1000;
                            if(gray<dither[sy&7,sx&7]*4+2) { band[y*HardwareRowBytes+x/8]&=(byte)~(128>>(x&7)); nonWhite=true; }
                        }
                    }
                    if(!nonWhite) continue;
                    byte[] compressed=CompressD(band); int compression=13;
                    if(compressed==null) { compressed=Compress(band); compression=14; }
                    uint checksum=0;
                    foreach(byte b in compressed) checksum+=b;
                    s.WriteByte(12); s.WriteByte((byte)n); U16(s,paper.Width); U16(s,BandHeight);
                    s.WriteByte((byte)compression); U32(s,(uint)compressed.Length+4); s.Write(compressed,0,compressed.Length); U32(s,checksum);
                }
            } finally { bitmap.UnlockBits(locked); }
            s.WriteByte(1); U16(s,copies);
        }
    }
    public static class Raster {
        public static Bitmap Fit(Image image,Paper p) {
            Bitmap b=new Bitmap(p.Width,p.Height,PixelFormat.Format24bppRgb); b.SetResolution(600,600);
            using(Graphics g=Graphics.FromImage(b)) {
                g.Clear(Color.White); g.InterpolationMode=InterpolationMode.HighQualityBicubic;
                int w=Math.Min(4960,p.Width-2*Qpdl.MarginX), h=p.Height-2*Qpdl.MarginY;
                double scale=Math.Min((double)w/image.Width,(double)h/image.Height);
                int iw=Math.Max(1,(int)Math.Round(image.Width*scale)), ih=Math.Max(1,(int)Math.Round(image.Height*scale));
                g.DrawImage(image,new Rectangle(Qpdl.MarginX+(w-iw)/2,Qpdl.MarginY+(h-ih)/2,iw,ih));
            } return b;
        }
        public static Bitmap Test(Paper p) {
            Bitmap b=new Bitmap(p.Width,p.Height,PixelFormat.Format24bppRgb); b.SetResolution(600,600);
            using(Graphics g=Graphics.FromImage(b)) using(Font title=new Font("Microsoft YaHei",28)) using(Font body=new Font("Microsoft YaHei",14)) using(Pen pen=new Pen(Color.Black,3)) {
                g.Clear(Color.White); g.PageUnit=GraphicsUnit.Pixel;
                Rectangle r=new Rectangle(250,250,Math.Min(4460,p.Width-500),p.Height-500); g.DrawRectangle(pen,r);
                g.DrawString("Samsung SCX-4521F",title,Brushes.Black,350,400);
                g.DrawString("Windows 打印移植测试 / QPDL v1 / 600 DPI\n\n纸张: "+p.Name+"\n\n如果中文、边框与灰阶都清楚，基本打印链路正常。",body,Brushes.Black,new RectangleF(350,850,r.Width-200,2000));
                for(int i=0;i<8;i++) using(Brush shade=new SolidBrush(Color.FromArgb(i*255/7,i*255/7,i*255/7))) g.FillRectangle(shade,350+i*400,2300,400,500);
            } return b;
        }
    }
    public static class Pdf {
        static string Run(string path,int page,string output,Paper p,CancellationToken token) {
            token.ThrowIfCancellationRequested();
            string script=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"src","PdfRender.ps1");
            if(!File.Exists(script)) throw new FileNotFoundException("缺少 src/PdfRender.ps1，请保留完整文件夹。",script);
            ProcessStartInfo info=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"System32","WindowsPowerShell","v1.0","powershell.exe"));
            string invocation="$env:SCX_PDF_INPUT='"+Path.GetFullPath(path).Replace("'","''")+"';"+
                "$env:SCX_PDF_PAGE='"+page+"';$env:SCX_PDF_OUTPUT='"+output.Replace("'","''")+"';"+
                "$env:SCX_PDF_WIDTH='"+Math.Min(4960,p.Width-2*Qpdl.MarginX)+"';"+
                "$env:SCX_PDF_HEIGHT='"+(p.Height-2*Qpdl.MarginY)+"';& '"+script.Replace("'","''")+"'";
            info.Arguments="-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand "+Convert.ToBase64String(Encoding.Unicode.GetBytes(invocation));
            info.UseShellExecute=false; info.CreateNoWindow=true; info.RedirectStandardOutput=true; info.RedirectStandardError=true;
            info.StandardOutputEncoding=Encoding.UTF8; info.StandardErrorEncoding=Encoding.UTF8;
            using(Process process=Process.Start(info)) {
                using(CancellationTokenRegistration registration=token.Register(delegate { try { if(!process.HasExited) process.Kill(); } catch {} })) {
                Task<string> stdout=process.StandardOutput.ReadToEndAsync(), stderr=process.StandardError.ReadToEndAsync();
                if(!process.WaitForExit(120000)) { process.Kill(); throw new TimeoutException("PDF 转换超过 120 秒，已停止。"); }
                token.ThrowIfCancellationRequested();
                if(process.ExitCode!=0) throw new InvalidOperationException("Windows PDF 转换失败："+stderr.Result);
                return stdout.Result.Trim();
                }
            }
        }
        public static int Count(string path,Paper p,CancellationToken token=default(CancellationToken)) { int n=int.Parse(Run(path,-1,"",p,token)); if(n<1 || n>1000) throw new InvalidOperationException("PDF 必须包含 1 到 1000 页。"); return n; }
        public static Bitmap Page(string path,int index,Paper p,CancellationToken token=default(CancellationToken)) {
            string dir=Path.Combine(Path.GetTempPath(),"SCX4521F-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
            string file=Path.Combine(dir,"page.png");
            try { Run(path,index,file,p,token); using(Image image=Image.FromFile(file)) return Raster.Fit(image,p); }
            finally { if(File.Exists(file)) File.Delete(file); Directory.Delete(dir); }
        }
    }
    public static class Job {
        public static void Convert(string input,string output,Paper p,int copies,Action<string> progress) {
            if(!File.Exists(input)) throw new FileNotFoundException("输入文件不存在。",input);
            string ext=Path.GetExtension(input).ToLowerInvariant();
            if(ext!=".pdf" && ext!=".png" && ext!=".jpg" && ext!=".jpeg" && ext!=".bmp" && ext!=".tif" && ext!=".tiff") throw new ArgumentException("只支持 PDF、PNG、JPEG、BMP、TIFF。");
            if(copies<1 || copies>99) throw new ArgumentException("份数必须在 1 到 99 之间。");
            string full=Path.GetFullPath(output); if(string.Equals(Path.GetFullPath(input),full,StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("输出文件不能覆盖输入文件。");
            // Commit only complete jobs. No truncated job is submitted on rendering failure.
            string temp=full+"."+Guid.NewGuid().ToString("N")+".tmp";
            try {
                using(FileStream s=new FileStream(temp,FileMode.CreateNew,FileAccess.Write)) {
                    Qpdl.Begin(s,Path.GetFileName(input));
                    if(ext==".pdf") {
                        int count=Pdf.Count(input,p);
                        for(int i=0;i<count;i++) { progress("转换 PDF 第 "+(i+1)+" / "+count+" 页"); using(Bitmap b=Pdf.Page(input,i,p)) Qpdl.Page(s,b,p,copies); }
                    } else {
                        using(Image image=Image.FromFile(input)) {
                            FrameDimension dim=new FrameDimension(image.FrameDimensionsList[0]);
                            int count=(ext==".tif" || ext==".tiff")?image.GetFrameCount(dim):1;
                            if(count>1000) throw new ArgumentException("图片页数超过 1000 页。");
                            for(int i=0;i<count;i++) { image.SelectActiveFrame(dim,i); progress("转换图片第 "+(i+1)+" / "+count+" 页"); using(Bitmap b=Raster.Fit(image,p)) Qpdl.Page(s,b,p,copies); }
                        }
                    }
                    Qpdl.End(s);
                }
                if(File.Exists(full)) File.Replace(temp,full,null); else File.Move(temp,full);
            } finally { if(File.Exists(temp)) File.Delete(temp); }
        }
        public static void Test(string path,Paper p) { using(FileStream s=File.Create(path)) { Qpdl.Begin(s,"SCX4521F Test"); using(Bitmap b=Raster.Test(p)) Qpdl.Page(s,b,p,1); Qpdl.End(s); } }
    }
    public static class Usb {
        public const string Target="USB 直连 · Samsung SCX-4521F";
        static string Run(string mode,string path,int timeout,Action<string> progress,CancellationToken token) {
            string executable=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"SCX4521F-usb.exe");
            if(!File.Exists(executable)) throw new FileNotFoundException("缺少 SCX4521F-usb.exe，请保留完整文件夹。",executable);
            string stopFile=mode=="send"?Path.Combine(Path.GetTempPath(),"SCX4521F-stop-"+Guid.NewGuid().ToString("N")):null;
            ProcessStartInfo info=new ProcessStartInfo(executable,mode+(path==null?"":" \""+Path.GetFullPath(path)+"\"")+(stopFile==null?"":" \""+stopFile+"\""));
            info.UseShellExecute=false; info.CreateNoWindow=true; info.RedirectStandardOutput=true; info.RedirectStandardError=true;
            // The transport writes ASCII diagnostics; the model identity is ASCII.
            try { using(Process process=Process.Start(info)) {
                StringBuilder output=new StringBuilder();
                process.OutputDataReceived+=delegate(object sender,DataReceivedEventArgs e) { if(e.Data==null) return; lock(output) output.AppendLine(e.Data); if(progress!=null) progress(e.Data); };
                process.BeginOutputReadLine(); Task<string> error=process.StandardError.ReadToEndAsync();
                using(CancellationTokenRegistration registration=token.Register(delegate { if(stopFile!=null) try { File.WriteAllText(stopFile,"stop"); } catch {} })) {
                    Stopwatch elapsed=Stopwatch.StartNew(); long cancelAt=-1;
                    while(!process.WaitForExit(200)) {
                        if(token.IsCancellationRequested && cancelAt<0) cancelAt=elapsed.ElapsedMilliseconds;
                        if(cancelAt>=0 && elapsed.ElapsedMilliseconds-cancelAt>20000) { process.Kill(); throw new OperationCanceledException("USB 发送未在 20 秒内停止，请清除设备任务。",token); }
                        if(elapsed.ElapsedMilliseconds>timeout) { process.Kill(); throw new TimeoutException("USB 操作超时。若已进纸，请先在打印机上停止任务，再重试。"); }
                    }
                    process.WaitForExit(); token.ThrowIfCancellationRequested();
                    if(process.ExitCode!=0) throw new IOException(error.Result.Trim());
                    return output.ToString().Trim();
                }
            } } finally { if(stopFile!=null && File.Exists(stopFile)) File.Delete(stopFile); }
        }
        public static string Probe() { return Run("probe",null,10000,null,CancellationToken.None); }
        public static string Send(string path) { return Send(path,null,CancellationToken.None); }
        public static string Send(string path,Action<string> progress,CancellationToken token) { return Run("send",path,1800000,progress,token); }
        public static string Clear() { return Run("cancel",null,15000,null,CancellationToken.None); }
    }
    public static partial class Spool {
        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct DocInfo { [MarshalAs(UnmanagedType.LPWStr)] public string Name; [MarshalAs(UnmanagedType.LPWStr)] public string Output; [MarshalAs(UnmanagedType.LPWStr)] public string Type; }
        [StructLayout(LayoutKind.Sequential)] struct Printer4 { public IntPtr Name; public IntPtr Server; public uint Attributes; }
        [DllImport("winspool.drv",EntryPoint="EnumPrintersW",SetLastError=true)] static extern bool EnumPrinters(uint flags,string name,uint level,IntPtr data,uint cb,out uint needed,out uint count);
        [DllImport("winspool.drv",EntryPoint="OpenPrinterW",SetLastError=true,CharSet=CharSet.Unicode)] static extern bool OpenPrinter(string name,out IntPtr handle,IntPtr defaults);
        [DllImport("winspool.drv",SetLastError=true)] static extern bool ClosePrinter(IntPtr h);
        [DllImport("winspool.drv",EntryPoint="StartDocPrinterW",SetLastError=true,CharSet=CharSet.Unicode)] static extern uint StartDocPrinter(IntPtr h,uint level,ref DocInfo doc);
        [DllImport("winspool.drv",SetLastError=true)] static extern bool EndDocPrinter(IntPtr h);
        [DllImport("winspool.drv",SetLastError=true)] static extern bool StartPagePrinter(IntPtr h);
        [DllImport("winspool.drv",SetLastError=true)] static extern bool EndPagePrinter(IntPtr h);
        [DllImport("winspool.drv",SetLastError=true)] static extern bool AbortPrinter(IntPtr h);
        [DllImport("winspool.drv",SetLastError=true)] static extern bool WritePrinter(IntPtr h,IntPtr data,uint length,out uint written);
        static void Check(bool result) { if(!result) throw new Win32Exception(Marshal.GetLastWin32Error()); }
        public static string[] Printers() {
            uint needed,count; bool ok=EnumPrinters(6,null,4,IntPtr.Zero,0,out needed,out count);
            if(!ok && Marshal.GetLastWin32Error()!=122) throw new Win32Exception(Marshal.GetLastWin32Error());
            if(needed==0) return new string[0];
            IntPtr buffer=Marshal.AllocHGlobal((int)needed);
            try { Check(EnumPrinters(6,null,4,buffer,needed,out needed,out count)); List<string> names=new List<string>(); int size=Marshal.SizeOf(typeof(Printer4));
                for(int i=0;i<count;i++) { Printer4 p=(Printer4)Marshal.PtrToStructure(IntPtr.Add(buffer,i*size),typeof(Printer4)); names.Add(Marshal.PtrToStringUni(p.Name)); } return names.ToArray();
            } finally { Marshal.FreeHGlobal(buffer); }
        }
        public static uint Send(string printer,string path,CancellationToken token=default(CancellationToken),Action<int,int> progress=null) {
            if(string.IsNullOrWhiteSpace(printer)) throw new ArgumentException("请选择连接 SCX-4521F 的打印队列。");
            IntPtr h; Check(OpenPrinter(printer,out h,IntPtr.Zero)); bool started=false;
            try {
                DocInfo doc=new DocInfo { Name="SCX4521F Windows", Type="RAW", Output=null };
                uint id=StartDocPrinter(h,1,ref doc); if(id==0) throw new Win32Exception(Marshal.GetLastWin32Error()); started=true;
                Check(StartPagePrinter(h));
                using(FileStream source=File.OpenRead(path)) {
                    QpdlDocument document=QpdlDocument.Scan(source);
                    document.Transmit(source,delegate(byte[] chunk,int start,int count) {
                        GCHandle pinned=GCHandle.Alloc(chunk,GCHandleType.Pinned);
                        try { int offset=0; while(offset<count) { uint written; Check(WritePrinter(h,IntPtr.Add(pinned.AddrOfPinnedObject(),start+offset),(uint)(count-offset),out written));
                            if(written==0 || written>(uint)(count-offset)) throw new IOException("打印接口没有接受有效数据。"); offset+=(int)written; }
                        } finally { pinned.Free(); }
                    },delegate { return token.IsCancellationRequested; },delegate(int n,int total) { if(progress!=null) progress(n,total); });
                }
                Check(EndPagePrinter(h)); Check(EndDocPrinter(h)); started=false; return id;
            } finally { if(started) AbortPrinter(h); ClosePrinter(h); }
        }
    }
    public static class Program {
        [STAThread] public static int Main(string[] args) {
            try {
                if(args.Length==0) { Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false); Application.Run(new MainWindow()); return 0; }
                string command=args[0].ToLowerInvariant();
                if(command=="list") { foreach(string name in Spool.Printers()) Console.WriteLine(name); return 0; }
                if(command=="test" && args.Length>=2 && args.Length<=3) { Job.Test(args[1],Paper.Get(args.Length==3?args[2]:"A4")); Console.WriteLine("Saved test job: "+args[1]); return 0; }
                if(command=="convert" && args.Length>=3 && args.Length<=5) { Job.Convert(args[1],args[2],Paper.Get(args.Length>=4?args[3]:"A4"),args.Length==5?int.Parse(args[4]):1,Console.WriteLine); Console.WriteLine("Saved: "+args[2]); return 0; }
                if(command=="send" && args.Length==3) { ValidateJob(args[2]); Console.WriteLine("Submitted job: "+Spool.Send(args[1],args[2])); return 0; }
                Console.WriteLine("SCX4521F-cli list\nSCX4521F-cli test output.qpdl [A4|Letter|Legal|A5|A6]\nSCX4521F-cli convert input.pdf output.qpdl [paper] [copies]\nSCX4521F-cli send \"SCX queue name\" output.qpdl"); return command=="help"?0:2;
            } catch(Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
        }
        public static void ValidateJob(string path) {
            byte[] begin=Encoding.ASCII.GetBytes("\x1b%-12345X"), end=Encoding.ASCII.GetBytes("\t\x1b%-12345X");
            using(FileStream s=File.OpenRead(path)) { if(s.Length<begin.Length+end.Length+20) throw new InvalidDataException("QPDL 文件过短。");
                foreach(byte b in begin) if(s.ReadByte()!=b) throw new InvalidDataException("缺少 QPDL 作业头。");
                s.Seek(-end.Length,SeekOrigin.End); foreach(byte b in end) if(s.ReadByte()!=b) throw new InvalidDataException("缺少 QPDL 作业结束标记，拒绝发送不完整作业。"); }
        }
    }
}
