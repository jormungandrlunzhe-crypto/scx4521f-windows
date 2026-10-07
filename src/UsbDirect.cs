// GPL-2.0-only. SCX-4521F direct USB test transport, 2026.
using System;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using System.ComponentModel;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using SCX4521F;
class UsbDirect {
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern SafeFileHandle CreateFile(string name,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool DeviceIoControl(SafeFileHandle h,uint code,IntPtr input,uint size,byte[] output,uint length,out uint returned,IntPtr overlap);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool WriteFile(SafeFileHandle h,IntPtr data,uint length,out uint written,IntPtr overlap);
    static int Main(string[] args) {
        try {
            bool simple=args.Length==1 && args[0]=="test-simple";
            bool external=(args.Length==2 || args.Length==3) && args[0]=="send";
            bool send=(args.Length==1 && args[0]=="test") || simple || external;
            bool reset=args.Length==1 && args[0]=="reset";
            bool cancel=args.Length==1 && args[0]=="cancel";
            string stopPath=external && args.Length==3?args[2]:null;
            if(args.Length>0 && !send && !reset && !cancel && !(args.Length==1 && args[0]=="probe")) throw new ArgumentException("Usage: SCX4521F-usb.exe probe | test | test-simple | reset | cancel | send job.qpdl [stop-file]");
            if(external) Program.ValidateJob(args[1]);
            string path=null;
            using(RegistryKey ports=Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Print\Monitors\USB Monitor\Ports")) {
                if(ports==null) throw new IOException("No USB printer ports.");
                foreach(string name in ports.GetSubKeyNames()) using(RegistryKey port=ports.OpenSubKey(name)) {
                    string parent=port.GetValue("Parent Device Id") as string, candidate=port.GetValue("Device Path") as string;
                    if(parent!=null && parent.StartsWith(@"USB\VID_04E8&PID_3419\",StringComparison.OrdinalIgnoreCase) && candidate!=null) {
                        if(path!=null) throw new IOException("Multiple SCX printers found; stopped to avoid ambiguous selection.");
                        path=candidate; Console.WriteLine("Port: "+name);
                    }
                }
            }
            if(path==null) throw new IOException("SCX-4521F USB port was not found.");
            using(SafeFileHandle h=CreateFile(path,0x40000000,3,IntPtr.Zero,3,0,IntPtr.Zero)) {
                if(h.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
                byte[] idBytes=new byte[4094]; uint returned;
                if(!DeviceIoControl(h,0x220034,IntPtr.Zero,0,idBytes,(uint)idBytes.Length,out returned,IntPtr.Zero)) throw new Win32Exception(Marshal.GetLastWin32Error(),"Unable to verify USB printer IEEE1284 identity.");
                string identity=Encoding.ASCII.GetString(idBytes,2,Math.Max(0,(int)returned-2)).TrimEnd('\0');
                Console.WriteLine("Device: "+identity);
                if(identity.IndexOf("Samsung",StringComparison.OrdinalIgnoreCase)<0 || (identity.IndexOf("4x21",StringComparison.OrdinalIgnoreCase)<0 && identity.IndexOf("4521",StringComparison.OrdinalIgnoreCase)<0)) throw new IOException("USB device model mismatch; no data sent.");
                if(reset || cancel) {
                    uint count;
                    if(!DeviceIoControl(h,0x220040,IntPtr.Zero,0,null,0,out count,IntPtr.Zero)) throw new Win32Exception(Marshal.GetLastWin32Error(),"USB soft reset failed.");
                    if(cancel) {
                        byte[] command=Encoding.ASCII.GetBytes("\x1b%-12345X@PJL RESET\n\x1b%-12345X");
                        WriteAll(h,command,0,command.Length);
                    }
                    Console.WriteLine(cancel?"CANCEL_REQUESTED USB reset and PJL reset sent; verify device becomes IDLE.":"USB soft reset accepted. No print job sent."); return 0;
                }
                if(send && identity.IndexOf("STATUS:IDLE",StringComparison.OrdinalIgnoreCase)<0) throw new IOException("Printer is not IDLE; clear the previous task before another test.");
                if(!send) { Console.WriteLine("Identity verified. No print data sent."); return 0; }
                using(MemoryStream job=new MemoryStream()) {
                    if(!external) {
                    Qpdl.Begin(job,"Windows USB Test");
                    using(System.Drawing.Bitmap raster=Raster.Test(Paper.Get("A4"))) {
                        if(simple) using(System.Drawing.Graphics g=System.Drawing.Graphics.FromImage(raster)) using(System.Drawing.Pen pen=new System.Drawing.Pen(System.Drawing.Color.Black,12)) {
                            g.Clear(System.Drawing.Color.White); g.DrawRectangle(pen,300,300,4200,6100);
                            g.FillRectangle(System.Drawing.Brushes.Black,700,1200,3000,20);
                            g.FillRectangle(System.Drawing.Brushes.Black,700,1800,2200,20);
                            g.FillRectangle(System.Drawing.Brushes.Black,700,2400,1400,20);
                        }
                        Qpdl.Page(job,raster,Paper.Get("A4"),1);
                    }
                    Qpdl.End(job);
                    job.Position=0;
                    }
                    long offset=0;
                    using(Stream input=external?(Stream)File.OpenRead(args[1]):job) {
                        QpdlDocument document=QpdlDocument.Scan(input);
                        int sent=document.Transmit(input,delegate(byte[] bytes,int start,int count) { WriteAll(h,bytes,start,count); offset+=count; },delegate { return stopPath!=null && File.Exists(stopPath); },delegate(int n,int total) { Console.WriteLine("PAGE "+n+" "+total); Console.Out.Flush(); });
                        Console.WriteLine((sent<document.Pages.Count?"CANCELLED ":"SENT ")+offset+" bytes; "+sent+"/"+document.Pages.Count+" pages sent. Check actual paper output.");
                    }
                }
            }
            return 0;
        } catch(Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
    }
    static void WriteAll(SafeFileHandle h,byte[] bytes,int start,int count) {
        GCHandle pinned=GCHandle.Alloc(bytes,GCHandleType.Pinned);
        try { int local=0; while(local<count) {
            uint written;
            if(!WriteFile(h,IntPtr.Add(pinned.AddrOfPinnedObject(),start+local),(uint)(count-local),out written,IntPtr.Zero)) throw new Win32Exception(Marshal.GetLastWin32Error(),"USB write failed.");
            if(written==0 || written>count-local) throw new IOException("Invalid USB write result."); local+=(int)written;
        } } finally { pinned.Free(); }
    }
}
