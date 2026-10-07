using System;
using System.IO;
using System.Drawing;
using SCX4521F;
class BandVectors {
    [STAThread] static void Main(string[] args) {
        string dir=args[0]; Directory.CreateDirectory(dir); Random rng=new Random(4521);
        for(int n=0;n<12;n++) {
            byte[] b=new byte[620*128];
            for(int i=0;i<b.Length;i++) {
                if(n==0) b[i]=255;
                if(n==1) b[i]=0;
                if(n==2) b[i]=(byte)(i%2==0?0:255);
                if(n==3) b[i]=(byte)rng.Next(256);
                if(n==4) b[i]=(byte)((i/65)%2==0?0:255);
                if(n==5) b[i]=(byte)((i/66)%2==0?0:255);
                if(n==6) b[i]=(byte)((i/2)%2==0?0:255);
                if(n==7) b[i]=(byte)((i/3)%2==0?0:255);
                if(n>=8) b[i]=255;
            }
            if(n==8) b[7*620+4200/8]&=(byte)(255^(128>>(4200&7)));
            if(n==9) for(int y=0;y<128;y++) { int x=y%2==0?4800:1; b[y*620+x/8]&=(byte)~(128>>(x&7)); }
            if(n==10) for(int y=0;y<128;y++) for(int x=10;x<70;x++) b[y*620+x/8]&=(byte)~(128>>(x&7));
            if(n==11) { b[600]=0; b[127*620]=0; }
            File.WriteAllBytes(Path.Combine(dir,n+".raw"),b); File.WriteAllBytes(Path.Combine(dir,n+".encoded"),Qpdl.Compress(b));
            byte[] d=Qpdl.CompressD(b); if(d!=null) File.WriteAllBytes(Path.Combine(dir,n+".encoded-d"),d);
        }
        System.Windows.Forms.Application.EnableVisualStyles();
        using(MainWindow f=new MainWindow()) { f.ShowInTaskbar=false; f.Opacity=0; f.Show(); System.Windows.Forms.Application.DoEvents(); DateTime deadline=DateTime.Now.AddSeconds(35); while(f.IsRefreshing && DateTime.Now<deadline) { System.Windows.Forms.Application.DoEvents(); System.Threading.Thread.Sleep(10); } if(f.IsRefreshing) throw new Exception("Printer discovery timed out."); f.PerformLayout(); using(Bitmap b=new Bitmap(f.Width,f.Height)) { f.DrawToBitmap(b,new Rectangle(0,0,b.Width,b.Height)); b.Save(Path.Combine(dir,"ui.png")); } f.Close(); }
        Console.WriteLine("12 band fixtures and UI rendered.");
    }
}
