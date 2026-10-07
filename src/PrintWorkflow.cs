// GPL-2.0-only. Page selection, collating, guided duplex and cancellable transmission.
using System;
using System.IO;
using System.Text;
using System.Drawing;
using System.Drawing.Imaging;
using System.Collections.Generic;
using System.Threading;

namespace SCX4521F {
    public enum PageFilter { All, Odd, Even }
    public class PrintOptions {
        public string Range=""; public PageFilter Filter=PageFilter.All;
        public int Copies=1; public bool Collate=true, Duplex=false, ShortEdge=false;
        public bool Reverse=false, BackReverse=true, BackRotate=false;
        public PrintOptions Clone() { return (PrintOptions)MemberwiseClone(); }
    }
    public static class PageSelection {
        public static List<int> Parse(string text,int total,PageFilter filter) {
            if(total<1 || total>1000) throw new ArgumentException("文档页数必须在 1 到 1000 之间。");
            SortedSet<int> pages=new SortedSet<int>();
            if(string.IsNullOrWhiteSpace(text) || text.Trim()=="全部") for(int n=1;n<=total;n++) pages.Add(n);
            else {
                string[] parts=text.Replace('，',',').Replace('；',',').Replace(';',',').Split(',');
                foreach(string raw in parts) {
                    string item=raw.Trim(); string[] bounds=item.Split('-'); int first,last;
                    if(bounds.Length>2 || !int.TryParse(bounds[0].Trim(),out first)) throw new ArgumentException("页码格式示例：1,3,5-8。页码从 1 开始。");
                    last=first;
                    if(bounds.Length==2 && !int.TryParse(bounds[1].Trim(),out last)) throw new ArgumentException("页码范围缺少终点。");
                    if(first<1 || last<first || last>total) throw new ArgumentException("页码超出文档范围或范围倒序，文档共 "+total+" 页。");
                    for(int n=first;n<=last;n++) pages.Add(n);
                }
            }
            List<int> result=new List<int>();
            foreach(int n in pages) if(filter==PageFilter.All || (filter==PageFilter.Odd && n%2==1) || (filter==PageFilter.Even && n%2==0)) result.Add(n-1);
            if(result.Count==0) throw new ArgumentException("当前页码和奇偶筛选没有选中任何页面。");
            return result;
        }
    }
    public class PlannedPage {
        public int Source, Copy; public bool Rotate;
        public PlannedPage(int source,int copy,bool rotate) { Source=source; Copy=copy; Rotate=rotate; }
        public string Label { get { return Source<0?"空白":(Source+1).ToString(); } }
    }
    public class PrintPlan {
        public List<int> Selected;
        public List<PlannedPage> Front=new List<PlannedPage>(), Back=new List<PlannedPage>();
        public int Sheets; public bool Duplex;
        public static PrintPlan Create(int total,PrintOptions options) {
            if(options.Copies<1 || options.Copies>99) throw new ArgumentException("份数必须在 1 到 99 之间。");
            PrintPlan p=new PrintPlan { Selected=PageSelection.Parse(options.Range,total,options.Filter), Duplex=options.Duplex };
            if(!options.Duplex) {
                if(options.Reverse) p.Selected.Reverse();
                if(options.Collate) for(int c=1;c<=options.Copies;c++) foreach(int page in p.Selected) p.Front.Add(new PlannedPage(page,c,false));
                else foreach(int page in p.Selected) for(int c=1;c<=options.Copies;c++) p.Front.Add(new PlannedPage(page,c,false));
                p.Sheets=p.Front.Count;
            } else {
                for(int c=1;c<=options.Copies;c++) for(int i=0;i<p.Selected.Count;i+=2) {
                    p.Front.Add(new PlannedPage(p.Selected[i],c,false));
                    p.Back.Add(new PlannedPage(i+1<p.Selected.Count?p.Selected[i+1]:-1,c,options.ShortEdge ^ options.BackRotate));
                }
                if(options.BackReverse) p.Back.Reverse();
                p.Sheets=p.Front.Count;
            }
            return p;
        }
        public static string Order(List<PlannedPage> pages) {
            StringBuilder b=new StringBuilder();
            for(int i=0;i<pages.Count && i<40;i++) { if(i>0) b.Append(", "); b.Append(pages[i].Label); }
            if(pages.Count>40) b.Append(" …（共 "+pages.Count+" 面）");
            return b.ToString();
        }
        public string Summary { get { return "选中 "+Selected.Count+" 页，"+Sheets+" 张纸；"+(Duplex?"正面："+Order(Front)+"\n背面："+Order(Back):"页序："+Order(Front)); } }
    }
    public class DocumentSource : IDisposable {
        readonly string path; readonly Paper paper; readonly Image image; readonly FrameDimension dimension;
        public readonly int Count;
        public DocumentSource(string input,Paper p,CancellationToken token) {
            path=Path.GetFullPath(input); paper=p;
            if(!File.Exists(path)) throw new FileNotFoundException("输入文件不存在。",path);
            string ext=Path.GetExtension(path).ToLowerInvariant();
            if(ext==".pdf") Count=Pdf.Count(path,p,token);
            else {
                if(ext!=".png" && ext!=".jpg" && ext!=".jpeg" && ext!=".bmp" && ext!=".tif" && ext!=".tiff") throw new ArgumentException("只支持 PDF、PNG、JPEG、BMP、TIFF。");
                image=Image.FromFile(path); dimension=new FrameDimension(image.FrameDimensionsList[0]);
                Count=(ext==".tif" || ext==".tiff")?image.GetFrameCount(dimension):1;
            }
            if(Count<1 || Count>1000) { Dispose(); throw new ArgumentException("文档必须包含 1 到 1000 页。"); }
        }
        public Bitmap Render(int index,CancellationToken token) {
            token.ThrowIfCancellationRequested();
            if(index<0 || index>=Count) throw new ArgumentOutOfRangeException("index");
            if(image==null) return Pdf.Page(path,index,paper,token);
            image.SelectActiveFrame(dimension,index); return Raster.Fit(image,paper);
        }
        public void Dispose() { if(image!=null) image.Dispose(); }
    }
    public class PrintSession : IDisposable {
        public string DirectoryPath,FrontPath,BackPath; public PrintPlan Plan;
        public static PrintSession Prepare(string input,Paper paper,PrintOptions options,Action<string> progress,CancellationToken token) {
            PrintSession session=new PrintSession();
            session.DirectoryPath=Path.Combine(Path.GetTempPath(),"SCX4521F-session-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(session.DirectoryPath);
            try {
                using(DocumentSource source=new DocumentSource(input,paper,token)) {
                    session.Plan=PrintPlan.Create(source.Count,options);
                    Dictionary<string,string> cache=new Dictionary<string,string>();
                    List<PlannedPage> needed=new List<PlannedPage>(session.Plan.Front); needed.AddRange(session.Plan.Back);
                    foreach(PlannedPage page in needed) {
                        token.ThrowIfCancellationRequested(); string key=page.Source+"-"+page.Rotate;
                        if(cache.ContainsKey(key)) continue;
                        progress(page.Source<0?"准备空白背面":"准备原文第 "+(page.Source+1)+" 页");
                        string fragment=Path.Combine(session.DirectoryPath,"page-"+cache.Count+".bin");
                        using(Bitmap bitmap=page.Source<0?new Bitmap(paper.Width,paper.Height,PixelFormat.Format24bppRgb):source.Render(page.Source,token)) {
                            if(page.Source<0) using(Graphics g=Graphics.FromImage(bitmap)) g.Clear(Color.White);
                            if(page.Rotate) bitmap.RotateFlip(RotateFlipType.Rotate180FlipNone);
                            using(FileStream file=File.Create(fragment)) Qpdl.Page(file,bitmap,paper,1,token);
                        }
                        cache.Add(key,fragment);
                    }
                    session.FrontPath=Path.Combine(session.DirectoryPath,"front.qpdl");
                    Assemble(session.FrontPath,Path.GetFileName(input)+" front",session.Plan.Front,cache,token);
                    if(options.Duplex) { session.BackPath=Path.Combine(session.DirectoryPath,"back.qpdl"); Assemble(session.BackPath,Path.GetFileName(input)+" back",session.Plan.Back,cache,token); }
                }
                return session;
            } catch { session.Dispose(); throw; }
        }
        static void Assemble(string path,string title,List<PlannedPage> pages,Dictionary<string,string> cache,CancellationToken token) {
            using(FileStream output=File.Create(path)) {
                Qpdl.Begin(output,title);
                foreach(PlannedPage page in pages) { token.ThrowIfCancellationRequested(); using(FileStream input=File.OpenRead(cache[page.Source+"-"+page.Rotate])) input.CopyTo(output); }
                Qpdl.End(output);
            }
        }
        public void Export(string path) {
            string full=Path.GetFullPath(path), stem=Path.Combine(Path.GetDirectoryName(full),Path.GetFileNameWithoutExtension(full));
            if(BackPath==null) File.Copy(FrontPath,full,true);
            else {
                File.Copy(FrontPath,stem+"-正面.qpdl",true); File.Copy(BackPath,stem+"-背面.qpdl",true);
                File.WriteAllText(stem+"-翻纸说明.txt",Plan.Summary+"\r\n先发送正面，待全部出纸后，将空白面朝上放回纸盘，再发送背面。保留全部纸张，包括最后一页的空白背面。",Encoding.UTF8);
            }
        }
        public void Dispose() {
            if(DirectoryPath==null || !Directory.Exists(DirectoryPath)) return;
            // Only remove flat files from this session's uniquely created directory.
            foreach(string path in Directory.GetFiles(DirectoryPath)) File.Delete(path);
            Directory.Delete(DirectoryPath);
        }
    }
    public class PageSpan { public long Start, End; }
    public class QpdlDocument {
        public long HeaderEnd, FooterStart; public List<PageSpan> Pages=new List<PageSpan>();
        static int Read(Stream s) { int b=s.ReadByte(); if(b<0) throw new EndOfStreamException("打印数据被截断。"); return b; }
        public static QpdlDocument Scan(Stream s) {
            s.Position=0; StringBuilder line=new StringBuilder(); int count=0;
            while(true) {
                int b=Read(s); count++; if(count>4096) throw new InvalidDataException("打印数据缺少 QPDL 入口。");
                if(b==10) { if(line.ToString().EndsWith("@PJL ENTER LANGUAGE = QPDL")) break; line.Clear(); } else line.Append((char)b);
            }
            QpdlDocument result=new QpdlDocument { HeaderEnd=s.Position };
            while(true) {
                long start=s.Position; int type=Read(s);
                if(type==9) { result.FooterStart=start; break; }
                if(type!=0) throw new InvalidDataException("无效页面头。");
                byte[] header=new byte[16]; for(int i=0;i<16;i++) header[i]=(byte)Read(s);
                int copies=(header[1]<<8)|header[2];
                while(true) {
                    int record=Read(s);
                    if(record==1) { int footerCopies=(Read(s)<<8)|Read(s); if(footerCopies!=copies) throw new InvalidDataException("页尾份数不一致。"); break; }
                    if(record!=12) throw new InvalidDataException("无效带记录。");
                    for(int i=0;i<6;i++) Read(s);
                    uint length=0; for(int i=0;i<4;i++) length=(length<<8)|(uint)Read(s);
                    if(length<4 || length>10000000 || s.Position+length>s.Length) throw new InvalidDataException("带长度无效或数据被截断。");
                    s.Position+=length;
                }
                result.Pages.Add(new PageSpan { Start=start,End=s.Position });
            }
            if(result.Pages.Count==0) throw new InvalidDataException("没有打印页。");
            s.Position=0; return result;
        }
        static void Copy(Stream source,long start,long end,Action<byte[],int,int> write) {
            byte[] chunk=new byte[16384]; source.Position=start;
            while(source.Position<end) { int n=source.Read(chunk,0,(int)Math.Min(chunk.Length,end-source.Position)); if(n==0) throw new EndOfStreamException(); write(chunk,0,n); }
        }
        // Stop at a page boundary and write a valid job terminator. Never resend pages.
        public int Transmit(Stream source,Action<byte[],int,int> write,Func<bool> stop,Action<int,int> progress) {
            if(stop()) return 0;
            Copy(source,0,HeaderEnd,write); int sent=0;
            foreach(PageSpan page in Pages) {
                if(stop()) break;
                Copy(source,page.Start,page.End,write); sent++;
                progress(sent,Pages.Count);
            }
            if(sent==Pages.Count) Copy(source,FooterStart,source.Length,write);
            else { byte[] terminator=Encoding.ASCII.GetBytes("\t\x1b%-12345X"); write(terminator,0,terminator.Length); }
            return sent;
        }
    }
}
