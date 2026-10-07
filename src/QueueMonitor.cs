// GPL-2.0-only. Native Windows queue monitoring; USB jobs are tracked by the application.
using System;
using System.Runtime.InteropServices;
using System.ComponentModel;
using System.Collections.Generic;
namespace SCX4521F {
    public class WindowsJob {
        public uint Id { get; set; } public string Document { get; set; }
        public string State { get; set; } public uint SentPages { get; set; } public uint TotalPages { get; set; }
    }
    public static partial class Spool {
        [StructLayout(LayoutKind.Sequential)] struct SystemTime { public ushort Year,Month,DayOfWeek,Day,Hour,Minute,Second,Milliseconds; }
        [StructLayout(LayoutKind.Sequential)] struct JobInfo1 {
            public uint Id; public IntPtr Printer,Machine,User,Document,DataType,TextStatus;
            public uint Status,Priority,Position,TotalPages,PagesPrinted; public SystemTime Submitted;
        }
        [DllImport("winspool.drv",EntryPoint="EnumJobsW",SetLastError=true)] static extern bool EnumJobs(IntPtr h,uint first,uint number,uint level,IntPtr data,uint size,out uint needed,out uint returned);
        [DllImport("winspool.drv",EntryPoint="SetJobW",SetLastError=true)] static extern bool SetJob(IntPtr h,uint id,uint level,IntPtr data,uint command);
        public static List<WindowsJob> Jobs(string printer) {
            IntPtr h; Check(OpenPrinter(printer,out h,IntPtr.Zero));
            try {
                uint needed,count; bool ok=EnumJobs(h,0,1000,1,IntPtr.Zero,0,out needed,out count);
                if(!ok && Marshal.GetLastWin32Error()!=122) throw new Win32Exception(Marshal.GetLastWin32Error());
                List<WindowsJob> result=new List<WindowsJob>(); if(needed==0) return result;
                IntPtr buffer=Marshal.AllocHGlobal((int)needed);
                try { Check(EnumJobs(h,0,1000,1,buffer,needed,out needed,out count)); int size=Marshal.SizeOf(typeof(JobInfo1));
                    for(int i=0;i<count;i++) { JobInfo1 j=(JobInfo1)Marshal.PtrToStructure(IntPtr.Add(buffer,i*size),typeof(JobInfo1));
                        string state=Marshal.PtrToStringUni(j.TextStatus);
                        if(string.IsNullOrWhiteSpace(state)) state=(j.Status&1)!=0?"暂停":(j.Status&2)!=0?"错误":(j.Status&16)!=0?"打印中":(j.Status&8)!=0?"正在后台处理":(j.Status&128)!=0?"已发送":(j.Status&4)!=0?"正在删除":"等待";
                        result.Add(new WindowsJob { Id=j.Id,Document=Marshal.PtrToStringUni(j.Document),State=state,SentPages=j.PagesPrinted,TotalPages=j.TotalPages });
                    }
                } finally { Marshal.FreeHGlobal(buffer); } return result;
            } finally { ClosePrinter(h); }
        }
        public static void Control(string printer,uint id,uint command) {
            if(command!=1 && command!=2 && command!=5) throw new ArgumentException("Unsupported queue command.");
            IntPtr h; Check(OpenPrinter(printer,out h,IntPtr.Zero)); try { Check(SetJob(h,id,0,IntPtr.Zero,command)); } finally { ClosePrinter(h); }
        }
    }
}
