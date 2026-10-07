using System;
using System.IO;
using System.Text;
using System.Drawing;
using System.Collections.Generic;
using System.Threading;
using SCX4521F;
class WorkflowTests {
    static void Assert(bool condition,string label) { if(!condition) throw new Exception(label); Console.WriteLine("PASS "+label); }
    static string Order(List<PlannedPage> pages) { return PrintPlan.Order(pages); }
    [STAThread] static int Main(string[] args) {
        try {
            string dir=Path.GetFullPath(args[0]); Directory.CreateDirectory(dir); Paper paper=Paper.Get("A4");
            List<int> selected=PageSelection.Parse("1,3,3,5-7",8,PageFilter.All);
            Assert(string.Join(",",selected)=="0,2,4,5,6","page ranges deduplicate and normalize");
            Assert(string.Join(",",PageSelection.Parse("2-8",8,PageFilter.Odd))=="2,4,6","odd filter uses original document numbers");
            foreach(string invalid in new[]{"0","9","4-2","1,","x","1-"}) { bool rejected=false; try { PageSelection.Parse(invalid,8,PageFilter.All); } catch(ArgumentException) { rejected=true; } Assert(rejected,"reject "+invalid); }
            PrintPlan collated=PrintPlan.Create(3,new PrintOptions { Copies=2 });
            Assert(Order(collated.Front)=="1, 2, 3, 1, 2, 3","collated copy order");
            PrintPlan uncollated=PrintPlan.Create(3,new PrintOptions { Copies=2,Collate=false });
            Assert(Order(uncollated.Front)=="1, 1, 2, 2, 3, 3","uncollated copy order");
            Assert(Order(PrintPlan.Create(3,new PrintOptions { Reverse=true }).Front)=="3, 2, 1","reverse simplex order");
            PrintOptions oddOptions=new PrintOptions { Range="1,3-4",Copies=2,Duplex=true };
            PrintPlan odd=PrintPlan.Create(4,oddOptions);
            Assert(Order(odd.Front)=="1, 4, 1, 4" && Order(odd.Back)=="空白, 3, 空白, 3" && odd.Sheets==4,"odd selected duplex padding and collated sheets");
            Assert(Order(PrintPlan.Create(4,new PrintOptions { Duplex=true }).Back)=="4, 2","default reverse back order");
            Assert(Order(PrintPlan.Create(4,new PrintOptions { Duplex=true,BackReverse=false }).Back)=="2, 4","alternate paper feed order");
            Assert(PrintPlan.Create(4,new PrintOptions { Duplex=true,ShortEdge=true }).Back[0].Rotate,"short edge rotates backs");
            Assert(!PrintPlan.Create(4,new PrintOptions { Duplex=true,ShortEdge=true,BackRotate=true }).Back[0].Rotate,"rotation calibration composes with binding");
            if(args.Length>1) { using(PrintSession session=PrintSession.Prepare(args[1],paper,new PrintOptions { Range="2",Copies=2 },Console.WriteLine,CancellationToken.None)) { File.Copy(session.FrontPath,Path.Combine(dir,"pdf-selected.qpdl"),true); using(FileStream stream=File.OpenRead(session.FrontPath)) Assert(QpdlDocument.Scan(stream).Pages.Count==2,"PDF selected page and software copies"); } }
            string fixture=Path.Combine(dir,"calibration.tiff"); Calibration.Create(fixture);
            using(DocumentSource doc=new DocumentSource(fixture,paper,CancellationToken.None)) Assert(doc.Count==4,"four-page duplex calibration TIFF");
            Job.Convert(fixture,Path.Combine(dir,"baseline.qpdl"),paper,1,Console.WriteLine);
            using(PrintSession session=PrintSession.Prepare(fixture,paper,oddOptions,Console.WriteLine,CancellationToken.None)) {
                File.Copy(session.FrontPath,Path.Combine(dir,"duplex-front.qpdl"),true); File.Copy(session.BackPath,Path.Combine(dir,"duplex-back.qpdl"),true);
                session.Export(Path.Combine(dir,"export.qpdl"));
                Assert(File.Exists(Path.Combine(dir,"export-正面.qpdl")) && File.Exists(Path.Combine(dir,"export-背面.qpdl")),"duplex exports separate stages");
            }
            using(PrintSession session=PrintSession.Prepare(fixture,paper,new PrintOptions { Range="2",Copies=2 },Console.WriteLine,CancellationToken.None)) File.Copy(session.FrontPath,Path.Combine(dir,"single-page-copies.qpdl"),true);
            using(PrintSession session=PrintSession.Prepare(fixture,paper,new PrintOptions { Duplex=true,ShortEdge=true },Console.WriteLine,CancellationToken.None)) File.Copy(session.BackPath,Path.Combine(dir,"short-edge-back.qpdl"),true);
            byte[] original=File.ReadAllBytes(Path.Combine(dir,"baseline.qpdl"));
            using(MemoryStream input=new MemoryStream(original)) {
                QpdlDocument document=QpdlDocument.Scan(input); Assert(document.Pages.Count==4,"transmission parser finds page boundaries");
                using(MemoryStream sink=new MemoryStream()) {
                    int sent=document.Transmit(input,delegate(byte[] bytes,int start,int count) { sink.Write(bytes,start,count); },delegate { return false; },delegate(int n,int total) {});
                    Assert(sent==4 && Convert.ToBase64String(sink.ToArray())==Convert.ToBase64String(original),"normal transport preserves exact stream");
                }
                bool stop=false;
                using(MemoryStream sink=new MemoryStream()) {
                    int sent=document.Transmit(input,delegate(byte[] bytes,int start,int count) { sink.Write(bytes,start,count); if(sink.Length>document.HeaderEnd+10) stop=true; },delegate { return stop; },delegate(int n,int total) {});
                    Assert(sent==1,"cancellation during a page stops after that page"); File.WriteAllBytes(Path.Combine(dir,"cancel-after-page.qpdl"),sink.ToArray());
                    using(MemoryStream check=new MemoryStream(sink.ToArray())) Assert(QpdlDocument.Scan(check).Pages.Count==1,"cancelled stream has valid page and job ending");
                }
                using(MemoryStream sink=new MemoryStream()) Assert(document.Transmit(input,delegate(byte[] b,int s,int n) { sink.Write(b,s,n); },delegate { return true; },delegate(int n,int total) {})==0 && sink.Length==0,"cancel before submission writes nothing");
            }
            bool truncated=false; try { using(MemoryStream input=new MemoryStream(original,0,original.Length/2)) QpdlDocument.Scan(input); } catch(InvalidDataException) { truncated=true; } catch(IOException) { truncated=true; } Assert(truncated,"truncated jobs are rejected");
            CancellationTokenSource cancelled=new CancellationTokenSource(); cancelled.Cancel(); bool stopped=false;
            try { using(PrintSession session=PrintSession.Prepare(fixture,paper,new PrintOptions(),Console.WriteLine,cancelled.Token)) {} } catch(OperationCanceledException) { stopped=true; }
            Assert(stopped,"cancelled preparation produces no print session");
            System.Windows.Forms.Application.EnableVisualStyles();
            using(MainWindow window=new MainWindow()) { window.ShowInTaskbar=false; window.Opacity=0; window.Show(); System.Windows.Forms.Application.DoEvents(); DateTime limit=DateTime.Now.AddSeconds(20); while(window.IsRefreshing && DateTime.Now<limit) { System.Windows.Forms.Application.DoEvents(); Thread.Sleep(10); } System.Windows.Forms.Application.DoEvents(); window.PerformLayout();
                using(Bitmap bitmap=new Bitmap(window.Width,window.Height)) { window.DrawToBitmap(bitmap,new Rectangle(0,0,bitmap.Width,bitmap.Height)); bitmap.Save(Path.Combine(dir,"ui-v03.png")); }
                var fields=typeof(MainWindow); var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
                var modeControl=(System.Windows.Forms.ComboBox)fields.GetField("mode",flags).GetValue(window); modeControl.SelectedIndex=1;
                Assert(((System.Windows.Forms.ComboBox)fields.GetField("binding",flags).GetValue(window)).Enabled,"duplex settings activate with mode");
                var panel=(System.Windows.Forms.Panel)fields.GetField("flipPanel",flags).GetValue(window); panel.Visible=true;
                ((System.Windows.Forms.Label)fields.GetField("flipText",flags).GetValue(window)).Text="正面已发送，请确认两张纸已经输出。\n将整叠纸的空白面朝上（已印面朝下）放回纸盘，保持纸张顺序。\n背面页序：4, 2。首次使用请先做双面走纸校准。"; window.PerformLayout();
                using(Bitmap bitmap=new Bitmap(window.Width,window.Height)) { window.DrawToBitmap(bitmap,new Rectangle(0,0,bitmap.Width,bitmap.Height)); bitmap.Save(Path.Combine(dir,"ui-duplex-v03.png")); } window.Close(); }
            Console.WriteLine("ALL WORKFLOW TESTS PASSED. No printer write or reset was performed."); return 0;
        } catch(Exception ex) { Console.Error.WriteLine(ex.ToString()); return 1; }
    }
}
