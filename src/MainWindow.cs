// GPL-2.0-only. Print settings, two-pass duplex workflow and task monitor.
using System;
using System.IO;
using System.Text;
using System.Drawing;
using System.Drawing.Imaging;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
namespace SCX4521F {
    public class LocalTask {
        public int Id { get; set; } public string File { get; set; } public string Mode { get; set; }
        public string Progress { get; set; } public string State { get; set; }
        [Browsable(false)] public string Input,Target; [Browsable(false)] public Paper Paper;
        [Browsable(false)] public PrintOptions Options; [Browsable(false)] public bool Cancelled,Test,Calibration,Started,ClearRequested,AnyDataSent;
        [Browsable(false)] public uint QueueId;
    }
    public class MainWindow : Form {
        public bool IsRefreshing { get; private set; }
        TextBox file=new TextBox(),range=new TextBox(),log=new TextBox();
        ComboBox targets=new ComboBox(),paper=new ComboBox(),mode=new ComboBox(),filter=new ComboBox(),binding=new ComboBox();
        NumericUpDown copies=new NumericUpDown(); CheckBox collate=new CheckBox(),reverse=new CheckBox(),backReverse=new CheckBox(),backRotate=new CheckBox();
        Label pageInfo=new Label(),summary=new Label(),device=new Label(),flipText=new Label();
        Button open=new Button(),refresh=new Button(),print=new Button(),export=new Button(),test=new Button(),calibrate=new Button(),continueBack=new Button(),stop=new Button(),pause=new Button(),clear=new Button();
        Panel flipPanel=new Panel(); DataGridView tasks=new DataGridView(),windowsTasks=new DataGridView();
        BindingList<LocalTask> records=new BindingList<LocalTask>(); List<LocalTask> pending=new List<LocalTask>();
        System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer();
        CancellationTokenSource activeCancel; LocalTask active; TaskCompletionSource<bool> flipReady;
        bool worker,paused,polling,exporting; int totalPages,fileVersion,nextId=1; string lastDevice="";
        static ComboBox Choice(string[] items,int width) { ComboBox b=new ComboBox { DropDownStyle=ComboBoxStyle.DropDownList,Width=width }; b.Items.AddRange(items); b.SelectedIndex=0; return b; }
        static Button ActionButton(string text) { return new Button { Text=text,AutoSize=true,Height=30,Margin=new Padding(3) }; }
        public MainWindow() {
            Text="SCX-4521F Windows 打印工具 · v0.3"; Width=1000; Height=950; MinimumSize=new Size(900,900);
            Font=new Font("Microsoft YaHei UI",10); StartPosition=FormStartPosition.CenterScreen;
            TableLayoutPanel root=new TableLayoutPanel { Dock=DockStyle.Fill,Padding=new Padding(18),ColumnCount=1,RowCount=5 };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent,100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute,120)); Controls.Add(root);
            FlowLayoutPanel heading=new FlowLayoutPanel { AutoSize=true,Dock=DockStyle.Fill };
            heading.Controls.Add(new Label { Text="Samsung SCX-4521F",Font=new Font(Font.FontFamily,20,FontStyle.Bold),AutoSize=true }); device.Text="正在检查打印机…"; device.AutoSize=true; device.Margin=new Padding(24,14,0,0); heading.Controls.Add(device); root.Controls.Add(heading,0,0);
            TableLayoutPanel settings=new TableLayoutPanel { AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Dock=DockStyle.Top,ColumnCount=3,RowCount=7,Padding=new Padding(0,12,0,0) };
            settings.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,95)); settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); settings.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,125)); root.Controls.Add(settings,0,1);
            file.ReadOnly=true; file.Dock=DockStyle.Fill; open=ActionButton("选择文件"); Row(settings,0,"输入文件",file,open);
            targets.DropDownStyle=ComboBoxStyle.DropDownList; targets.Dock=DockStyle.Fill; refresh=ActionButton("刷新设备"); Row(settings,1,"打印目标",targets,refresh);
            FlowLayoutPanel paperRow=new FlowLayoutPanel { AutoSize=true,Dock=DockStyle.Fill };
            paper=Choice(new[]{"A4","Letter","Legal","A5","A6"},115); copies.Minimum=1; copies.Maximum=99; copies.Value=1; copies.Width=65;
            collate.Text="逐份整理"; collate.Checked=true; collate.AutoSize=true; reverse.Text="逆序（单面）"; reverse.AutoSize=true;
            paperRow.Controls.Add(paper); paperRow.Controls.Add(new Label { Text="份数",AutoSize=true,Margin=new Padding(15,5,3,0) }); paperRow.Controls.Add(copies); paperRow.Controls.Add(collate); paperRow.Controls.Add(reverse); Row(settings,2,"纸张 / 份数",paperRow,null);
            FlowLayoutPanel pageRow=new FlowLayoutPanel { AutoSize=true,Dock=DockStyle.Fill };
            range.Width=245; range.Text=""; filter=Choice(new[]{"全部选中页","仅奇数页","仅偶数页"},130); pageInfo.Text="未选择文件"; pageInfo.AutoSize=true; pageInfo.Margin=new Padding(10,5,0,0);
            pageRow.Controls.Add(range); pageRow.Controls.Add(filter); pageRow.Controls.Add(pageInfo); Row(settings,3,"页码范围",pageRow,null);
            FlowLayoutPanel modeRow=new FlowLayoutPanel { AutoSize=true,Dock=DockStyle.Fill };
            mode=Choice(new[]{"单面打印","双面：翻纸后继续"},200); binding=Choice(new[]{"长边装订（翻书）","短边装订（翻日历）"},185); modeRow.Controls.Add(mode); modeRow.Controls.Add(binding); Row(settings,4,"打印模式",modeRow,null);
            FlowLayoutPanel duplexRow=new FlowLayoutPanel { AutoSize=true,Dock=DockStyle.Fill };
            backReverse.Text="背面逆序"; backReverse.Checked=true; backReverse.AutoSize=true; backRotate.Text="校准：背面再转 180°"; backRotate.AutoSize=true; calibrate=ActionButton("双面走纸校准");
            duplexRow.Controls.Add(backReverse); duplexRow.Controls.Add(backRotate); duplexRow.Controls.Add(calibrate); Row(settings,5,"双面设置",duplexRow,null);
            summary.AutoSize=true; summary.MaximumSize=new Size(760,90); summary.ForeColor=Color.DimGray; summary.Text="页码示例：1,3,5-8；留空表示全部。双面打印中途需将纸张放回。"; Row(settings,6,"打印计划",summary,null);
            FlowLayoutPanel commands=new FlowLayoutPanel { AutoSize=true,Dock=DockStyle.Fill,Padding=new Padding(0,10,0,8) };
            print=ActionButton("加入打印任务"); export=ActionButton("导出打印数据"); test=ActionButton("打印测试页"); stop=ActionButton("停止选中任务"); pause=ActionButton("暂停等待队列"); clear=ActionButton("清除设备任务");
            foreach(Control c in new Control[]{print,export,test,stop,pause,clear}) commands.Controls.Add(c); root.Controls.Add(commands,0,2);
            TabControl tabs=new TabControl { Dock=DockStyle.Fill }; TabPage taskTab=new TabPage("本工具任务"),queueTab=new TabPage("Windows 打印队列"); tabs.TabPages.Add(taskTab); tabs.TabPages.Add(queueTab); root.Controls.Add(tabs,0,3);
            tasks.Dock=DockStyle.Fill; tasks.ReadOnly=true; tasks.AllowUserToAddRows=false; tasks.SelectionMode=DataGridViewSelectionMode.FullRowSelect; tasks.MultiSelect=false; tasks.AutoGenerateColumns=false; tasks.RowHeadersVisible=false;
            AddColumn(tasks,"Id","编号",55); AddColumn(tasks,"File","文件",240); AddColumn(tasks,"Mode","模式",145); AddColumn(tasks,"Progress","进度",160); AddColumn(tasks,"State","状态",250); tasks.DataSource=records; taskTab.Controls.Add(tasks);
            flipPanel.Dock=DockStyle.Bottom; flipPanel.Height=170; flipPanel.BackColor=Color.FromArgb(241,246,253); flipPanel.Visible=false;
            flipText.Dock=DockStyle.Fill; flipText.Padding=new Padding(12); continueBack=ActionButton("纸已放回，继续打印背面"); continueBack.Dock=DockStyle.Bottom; continueBack.Height=35; flipPanel.Controls.Add(flipText); flipPanel.Controls.Add(continueBack); taskTab.Controls.Add(flipPanel);
            windowsTasks.Dock=DockStyle.Fill; windowsTasks.ReadOnly=true; windowsTasks.AllowUserToAddRows=false; windowsTasks.MultiSelect=false; windowsTasks.SelectionMode=DataGridViewSelectionMode.FullRowSelect; windowsTasks.RowHeadersVisible=false; windowsTasks.AutoGenerateColumns=false;
            AddColumn(windowsTasks,"Id","任务号",70); AddColumn(windowsTasks,"Document","文档",300); AddColumn(windowsTasks,"State","状态",170); AddColumn(windowsTasks,"SentPages","队列报告页数",140); AddColumn(windowsTasks,"TotalPages","总页数",90);
            queueTab.Controls.Add(windowsTasks); FlowLayoutPanel queueActions=new FlowLayoutPanel { Dock=DockStyle.Bottom,Height=45 };
            foreach(KeyValuePair<string,uint> item in new Dictionary<string,uint>{{"暂停选中",1},{"恢复选中",2},{"删除选中",5}}) { Button b=ActionButton(item.Key); uint command=item.Value; b.Click+=async delegate { await QueueControl(command); }; queueActions.Controls.Add(b); }
            queueActions.Controls.Add(new Label { Text="USB 直连任务显示在“本工具任务”中。",AutoSize=true,Margin=new Padding(20,10,0,0) }); queueTab.Controls.Add(queueActions);
            log.Multiline=true; log.ReadOnly=true; log.ScrollBars=ScrollBars.Vertical; log.Dock=DockStyle.Fill; root.Controls.Add(log,0,4);
            ToolTip tips=new ToolTip(); tips.SetToolTip(range,"示例：2 或 1,3,5-8；重复页码会自动去重，按页码从小到大排列。"); tips.SetToolTip(backReverse,"默认按输出纸叠顶页先走纸安排背面。校准若发现 1/4、3/2 错配，请切换此项。"); tips.SetToolTip(backRotate,"校准若背面倒置，勾选此项；短边装订在此基础上再旋转一次。");
            open.Click+=async delegate { using(OpenFileDialog d=new OpenFileDialog { Filter="PDF 和图片|*.pdf;*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff" }) if(d.ShowDialog()==DialogResult.OK) { file.Text=d.FileName; await LoadCount(); } };
            refresh.Click+=delegate { RefreshPrinters(); }; print.Click+=delegate { Enqueue(false,false); }; test.Click+=delegate { Enqueue(true,false); }; calibrate.Click+=delegate { Enqueue(false,true); }; export.Click+=async delegate { await Export(); };
            stop.Click+=delegate { StopSelected(); }; clear.Click+=async delegate { await ClearDevice(); }; pause.Click+=delegate { paused=!paused; pause.Text=paused?"继续等待队列":"暂停等待队列"; AddLog(paused?"等待队列已暂停，当前任务继续。":"等待队列已恢复。"); StartWorker(); };
            continueBack.Click+=delegate { continueBack.Enabled=false; if(flipReady!=null) flipReady.TrySetResult(true); };
            foreach(ComboBox b in new[]{mode,filter,binding}) b.SelectedIndexChanged+=delegate { UpdatePlan(); };
            range.TextChanged+=delegate { UpdatePlan(); }; copies.ValueChanged+=delegate { UpdatePlan(); }; collate.CheckedChanged+=delegate { UpdatePlan(); }; reverse.CheckedChanged+=delegate { UpdatePlan(); }; backReverse.CheckedChanged+=delegate { UpdatePlan(); }; backRotate.CheckedChanged+=delegate { UpdatePlan(); };
            timer.Interval=3000; timer.Tick+=async delegate { await Poll(); }; Shown+=delegate { RefreshPrinters(); timer.Start(); UpdatePlan(); };
            FormClosing+=delegate(object sender,FormClosingEventArgs e) { if(worker || exporting || pending.Exists(delegate(LocalTask r) { return !r.Cancelled; })) { e.Cancel=true; AddLog("请先停止当前任务并取消等待任务，再关闭程序。"); } else timer.Stop(); };
        }
        static void Row(TableLayoutPanel table,int row,string title,Control value,Control action) { table.RowStyles.Add(new RowStyle(SizeType.AutoSize)); table.Controls.Add(new Label { Text=title,AutoSize=true,Margin=new Padding(0,7,3,8) },0,row); value.Margin=new Padding(3,4,3,4); table.Controls.Add(value,1,row); if(action!=null) table.Controls.Add(action,2,row); }
        static void AddColumn(DataGridView grid,string name,string title,int width) { grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName=name,HeaderText=title,Width=width,SortMode=DataGridViewColumnSortMode.NotSortable }); }
        void Ui(Action action) { if(IsDisposed) return; if(InvokeRequired) BeginInvoke(action); else action(); }
        void AddLog(string text) { Ui(delegate { log.AppendText(DateTime.Now.ToString("HH:mm:ss")+"  "+text+Environment.NewLine); }); }
        PrintOptions Options() { return new PrintOptions { Range=range.Text,Filter=(PageFilter)filter.SelectedIndex,Copies=(int)copies.Value,Collate=collate.Checked,Duplex=mode.SelectedIndex==1,ShortEdge=binding.SelectedIndex==1,Reverse=reverse.Checked,BackReverse=backReverse.Checked,BackRotate=backRotate.Checked }; }
        void UpdatePlan() {
            bool duplex=mode.SelectedIndex==1; binding.Enabled=backReverse.Enabled=backRotate.Enabled=duplex; reverse.Enabled=collate.Enabled=!duplex;
            try { summary.Text=totalPages>0?PrintPlan.Create(totalPages,Options()).Summary:"页码示例：1,3,5-8；留空表示全部。双面打印中途需将纸张放回。"; summary.ForeColor=Color.DimGray; }
            catch(Exception ex) { summary.Text=ex.Message; summary.ForeColor=Color.Firebrick; }
        }
        async Task LoadCount() { int version=++fileVersion; string path=file.Text; totalPages=0; pageInfo.Text="读取页数…";
            try { int count=await Task.Run(delegate { using(DocumentSource s=new DocumentSource(path,Paper.Get("A4"),CancellationToken.None)) return s.Count; }); if(version==fileVersion) { totalPages=count; pageInfo.Text="共 "+count+" 页"; } }
            catch(Exception ex) { if(version==fileVersion) { pageInfo.Text="读取失败"; AddLog(ex.Message); } } UpdatePlan();
        }
        async void RefreshPrinters() {
            if(IsRefreshing) return; IsRefreshing=true; refresh.Enabled=false; string old=targets.SelectedItem as string; targets.Items.Clear();
            try { string info=await Task.Run(delegate { try { return Usb.Probe(); } catch { return null; } }); if(info!=null) { targets.Items.Add(Usb.Target); targets.SelectedIndex=0; UpdateDevice(info); } else { device.Text="打印机：未连接或状态查询失败"; device.ForeColor=Color.Firebrick; }
                targets.Items.AddRange(Spool.Printers()); if(old!=null && targets.Items.Contains(old)) targets.SelectedItem=old;
                if(targets.SelectedIndex<0) for(int i=0;i<targets.Items.Count;i++) if(targets.Items[i].ToString().IndexOf("SCX",StringComparison.OrdinalIgnoreCase)>=0) { targets.SelectedIndex=i; break; }
            } catch(Exception ex) { AddLog("读取目标失败："+ex.Message); } finally { IsRefreshing=false; refresh.Enabled=true; }
        }
        void UpdateDevice(string info) { string state=info.IndexOf("STATUS:IDLE",StringComparison.OrdinalIgnoreCase)>=0?"空闲":info.IndexOf("STATUS:BUSY",StringComparison.OrdinalIgnoreCase)>=0?"忙碌":"已连接（状态未知）";
            device.Text="打印机："+state; device.ForeColor=state=="空闲"?Color.DarkGreen:Color.DarkOrange; if(lastDevice!=state) { lastDevice=state; AddLog("打印机状态："+state); } }
        async Task Poll() {
            if(polling || IsRefreshing) return; polling=true;
            try { if(active==null || active.State=="等待翻纸" || active.State=="等待打印机空闲") { try { string info=await Task.Run(delegate { return Usb.Probe(); }); UpdateDevice(info); } catch { device.Text="打印机：未连接或状态查询失败"; device.ForeColor=Color.Firebrick; } }
                string target=targets.SelectedItem as string; if(target!=null && target!=Usb.Target) { List<WindowsJob> list=await Task.Run(delegate { return Spool.Jobs(target); }); windowsTasks.DataSource=list; } else windowsTasks.DataSource=new List<WindowsJob>();
            } catch(Exception ex) { device.Text="队列监控失败："+ex.Message; } finally { polling=false; }
        }
        void Enqueue(bool testOnly,bool calibration) {
            try { string target=targets.SelectedItem as string; if(target==null) throw new Exception("请选择 USB 直连或 SCX 打印队列。");
                if(!testOnly && !calibration && totalPages==0) throw new Exception("请先选择文件并等待读取页数。");
                PrintOptions options=Options(); if(!testOnly && !calibration) PrintPlan.Create(totalPages,options);
                if(calibration) options=new PrintOptions { Duplex=true,BackReverse=backReverse.Checked,BackRotate=backRotate.Checked };
                LocalTask record=new LocalTask { Id=nextId++,File=testOnly?"中文灰阶测试页":calibration?"双面走纸校准（4 页 / 2 张）":Path.GetFileName(file.Text),Input=file.Text,Target=target,Paper=Paper.Get(paper.Text),Options=options,Test=testOnly,Calibration=calibration,Mode=calibration?"双面校准":testOnly?"单面 / 1 份":(options.Duplex?"双面":"单面")+" / "+options.Copies+" 份",Progress="等待",State="等待中" };
                records.Add(record); pending.Add(record); AddLog("已加入任务 #"+record.Id+"："+record.File); StartWorker();
            } catch(Exception ex) { AddLog(ex.Message); }
        }
        async void StartWorker() {
            if(worker || paused) return; worker=true;
            try { while(pending.Count>0 && !paused) {
                LocalTask record=pending[0]; pending.RemoveAt(0); if(record.Cancelled) continue;
                active=record; record.Started=true; activeCancel=new CancellationTokenSource(); CancellationToken token=activeCancel.Token;
                PrintSession session=null; string testPath=null,calibrationFile=null;
                try {
                    SetState(record,"准备中");
                    if(record.Test) { testPath=Path.Combine(Path.GetTempPath(),"SCX4521F-test-"+Guid.NewGuid().ToString("N")+".qpdl"); await Task.Run(delegate { token.ThrowIfCancellationRequested(); Job.Test(testPath,record.Paper); token.ThrowIfCancellationRequested(); }); await Send(record,testPath,token); }
                    else {
                        if(record.Calibration) { calibrationFile=Path.Combine(Path.GetTempPath(),"SCX4521F-calibration-"+Guid.NewGuid().ToString("N")+".tiff"); await Task.Run(delegate { Calibration.Create(calibrationFile); }); }
                        session=await Task.Run(delegate { return PrintSession.Prepare(record.Calibration?calibrationFile:record.Input,record.Paper,record.Options,delegate(string t) { AddLog("#"+record.Id+" "+t); },token); });
                        AddLog("#"+record.Id+" "+session.Plan.Summary); await Send(record,session.FrontPath,token);
                        if(session.BackPath!=null) {
                            await WaitIdle(record,token); SetState(record,"等待翻纸"); flipReady=new TaskCompletionSource<bool>();
                            flipText.Text="任务 #"+record.Id+" 的正面已发送，请确认全部 "+session.Plan.Sheets+" 张纸已经输出。\n将整叠纸的空白面朝上（已印面朝下）放回纸盘，不单独打乱纸张顺序。\n背面页序："+PrintPlan.Order(session.Plan.Back)+"。奇数页数量时保留整叠纸，末页背面自动留白。\n首次使用先完成校准；背面倒置可切换“背面再转 180°”，配对错误可切换“背面逆序”。";
                            flipPanel.Visible=true; continueBack.Enabled=true;
                            using(CancellationTokenRegistration registration=token.Register(delegate { flipReady.TrySetCanceled(); })) await flipReady.Task;
                            flipPanel.Visible=false; token.ThrowIfCancellationRequested(); await Send(record,session.BackPath,token);
                            if(record.Calibration) AddLog("校准应得到两张纸：1 的背面是 2，3 的背面是 4；同一张纸的上下箭头应一致。根据结果调整双面设置，再打印正式文档。");
                        }
                    }
                    SetState(record,"数据已发送"); record.Progress="发送完成（请确认出纸）"; records.ResetBindings();
                } catch(OperationCanceledException) { record.Cancelled=true; SetState(record,"已停止继续发送"); AddLog("任务 #"+record.Id+" 已停止；已进入设备的纸张可能继续输出。"); }
                catch(Exception ex) { if(record.State=="正在发送" || record.State=="等待打印机空闲") { paused=true; pause.Text="继续等待队列"; } SetState(record,"失败（不自动重试）"); AddLog("任务 #"+record.Id+"："+ex.Message); }
                finally {
                    flipPanel.Visible=false; flipReady=null;
                    try { if(session!=null) session.Dispose(); if(testPath!=null && File.Exists(testPath)) File.Delete(testPath); if(calibrationFile!=null && File.Exists(calibrationFile)) File.Delete(calibrationFile); } catch(Exception ex) { AddLog("清理临时文件失败："+ex.Message); }
                    activeCancel.Dispose(); activeCancel=null; active=null;
                }
                if(record.Cancelled && record.QueueId>0) { try { await Task.Run(delegate { Spool.Control(record.Target,record.QueueId,5); }); } catch(Exception ex) { AddLog("删除本任务的队列记录："+ex.Message); } }
                if(record.ClearRequested && record.Target==Usb.Target) await RequestDeviceClear();
            } } finally { worker=false; }
        }
        void SetState(LocalTask record,string state) { record.State=state; records.ResetBindings(); }
        async Task WaitIdle(LocalTask record,CancellationToken token) {
            if(record.Target!=Usb.Target) return;
            SetState(record,"等待打印机空闲"); DateTime until=DateTime.Now.AddMinutes(10);
            while(true) { token.ThrowIfCancellationRequested(); string info=await Task.Run(delegate { return Usb.Probe(); }); UpdateDevice(info); if(info.IndexOf("STATUS:IDLE",StringComparison.OrdinalIgnoreCase)>=0) return;
                if(DateTime.Now>until) throw new TimeoutException("打印机 10 分钟内未恢复空闲，请检查缺纸、卡纸或面板提示。"); await Task.Delay(1500,token);
            }
        }
        async Task Send(LocalTask record,string path,CancellationToken token) {
            await WaitIdle(record,token); token.ThrowIfCancellationRequested(); SetState(record,"正在发送");
            await Task.Run(delegate {
                if(record.Target==Usb.Target) { string result=Usb.Send(path,delegate(string line) { if(line.StartsWith("PAGE ")) { string[] parts=line.Split(' '); Ui(delegate { record.AnyDataSent=true; record.Progress="本阶段已发送 "+parts[1]+" / "+parts[2]+" 页"; records.ResetBindings(); }); } },token); AddLog("#"+record.Id+" "+result); }
                else { record.QueueId=Spool.Send(record.Target,path,token,delegate(int n,int total) { Ui(delegate { record.AnyDataSent=true; record.Progress="已发送 "+n+" / "+total+" 页"; records.ResetBindings(); }); }); token.ThrowIfCancellationRequested(); AddLog("Windows 队列任务号："+record.QueueId); }
            });
        }
        void StopSelected() {
            LocalTask selected=tasks.CurrentRow==null?active:tasks.CurrentRow.DataBoundItem as LocalTask;
            if(selected==null) return;
            if(selected==active) { selected.ClearRequested=selected.State=="正在发送" || (selected.AnyDataSent && selected.State=="等待打印机空闲"); activeCancel.Cancel(); SetState(selected,"正在停止"); }
            else if(!selected.Started) { selected.Cancelled=true; SetState(selected,"已取消等待任务"); }
            else if(selected.QueueId>0) { Task.Run(delegate { try { Spool.Control(selected.Target,selected.QueueId,5); AddLog("已请求删除 Windows 队列任务。"); } catch(Exception ex) { AddLog(ex.Message); } }); }
            else AddLog("此任务数据已经发送。要取消设备内部剩余页面，请点击“清除设备任务”。");
        }
        async Task ClearDevice() {
            paused=true; pause.Text="继续等待队列";
            if(active!=null) { active.ClearRequested=true; activeCancel.Cancel(); SetState(active,"正在停止并请求清除"); return; }
            await RequestDeviceClear();
        }
        async Task RequestDeviceClear() {
            clear.Enabled=false;
            try { AddLog(await Task.Run(delegate { return Usb.Clear(); })); await Task.Delay(1200); string info=await Task.Run(delegate { return Usb.Probe(); }); UpdateDevice(info);
                AddLog(info.IndexOf("STATUS:IDLE",StringComparison.OrdinalIgnoreCase)>=0?"设备已返回空闲。请核对已经出纸的页数，再继续等待队列。":"取消请求已发送，但设备尚未空闲。若仍卡住，请按打印机停止键或重新开机；软件不会宣称已清除成功。");
            } catch(Exception ex) { AddLog("设备清除请求失败："+ex.Message); } finally { clear.Enabled=true; }
        }
        async Task QueueControl(uint command) { string target=targets.SelectedItem as string; WindowsJob job=windowsTasks.CurrentRow==null?null:windowsTasks.CurrentRow.DataBoundItem as WindowsJob; if(target==null || target==Usb.Target || job==null) { AddLog("请在 Windows 队列页选择一个已有任务。"); return; }
            try { await Task.Run(delegate { Spool.Control(target,job.Id,command); }); AddLog("队列任务 "+job.Id+" 控制请求已执行。"); await Poll(); } catch(Exception ex) { AddLog("队列操作失败："+ex.Message); }
        }
        async Task Export() {
            if(exporting || totalPages==0) return; string input=file.Text; PrintOptions options=Options(); Paper p=Paper.Get(paper.Text);
            try { PrintPlan.Create(totalPages,options); using(SaveFileDialog d=new SaveFileDialog { Filter="QPDL 数据|*.qpdl",FileName=Path.GetFileNameWithoutExtension(input)+".qpdl" }) {
                if(d.ShowDialog()!=DialogResult.OK) return; string path=d.FileName; if(string.Equals(Path.GetFullPath(input),Path.GetFullPath(path),StringComparison.OrdinalIgnoreCase)) throw new Exception("输出不能覆盖输入文件。");
                exporting=true; export.Enabled=false; await Task.Run(delegate { using(PrintSession session=PrintSession.Prepare(input,p,options,AddLog,CancellationToken.None)) session.Export(path); }); AddLog(options.Duplex?"已导出正面、背面及翻纸说明三个文件。":"已导出："+path);
            } } catch(Exception ex) { AddLog("导出失败："+ex.Message); } finally { exporting=false; export.Enabled=true; }
        }
    }
    public static class Calibration {
        public static void Create(string path) {
            ImageCodecInfo codec=null; foreach(ImageCodecInfo c in ImageCodecInfo.GetImageEncoders()) if(c.MimeType=="image/tiff") codec=c;
            using(Bitmap first=Sheet(1)) using(EncoderParameters parameters=new EncoderParameters(1)) {
                parameters.Param[0]=new EncoderParameter(System.Drawing.Imaging.Encoder.SaveFlag,(long)EncoderValue.MultiFrame); first.Save(path,codec,parameters);
                for(int n=2;n<=4;n++) using(Bitmap page=Sheet(n)) { parameters.Param[0].Dispose(); parameters.Param[0]=new EncoderParameter(System.Drawing.Imaging.Encoder.SaveFlag,(long)EncoderValue.FrameDimensionPage); first.SaveAdd(page,parameters); }
                parameters.Param[0].Dispose(); parameters.Param[0]=new EncoderParameter(System.Drawing.Imaging.Encoder.SaveFlag,(long)EncoderValue.Flush); first.SaveAdd(parameters);
            }
        }
        static Bitmap Sheet(int page) { Bitmap b=new Bitmap(1240,1754); using(Graphics g=Graphics.FromImage(b)) using(Font title=new Font("Microsoft YaHei",110,FontStyle.Bold)) using(Font body=new Font("Microsoft YaHei",22)) using(Pen p=new Pen(Color.Black,6)) {
            g.Clear(Color.White); g.DrawString(page.ToString(),title,Brushes.Black,500,500); g.DrawString("双面校准 · 原文第 "+page+" 页\n1 背面应为 2；3 背面应为 4\n此箭头是页面顶部",body,Brushes.Black,150,850);
            g.DrawLine(p,620,400,620,130); g.DrawLine(p,620,130,570,200); g.DrawLine(p,620,130,670,200); g.DrawRectangle(p,80,80,1080,1594);
        } return b; }
    }
}
