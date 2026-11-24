using System;
using System.IO;
using System.Linq;
using System.Data;
using System.Drawing;
using System.Windows.Forms;
using System.Collections.Generic;

namespace LigPrint {
public class Studio:Form {
    static readonly Color Ink=Color.FromArgb(34,53,75),Accent=Color.FromArgb(30,103,179),Pale=Color.FromArgb(244,247,250);
    LabelFile doc=new LabelFile();
    readonly Canvas canvas=new Canvas{Dock=DockStyle.Fill};
    readonly PropertyGrid props=new PropertyGrid{Dock=DockStyle.Fill,ToolbarVisible=false,PropertySort=PropertySort.NoSort,HelpVisible=false};
    readonly ComboBox fields=new ComboBox{Dock=DockStyle.Top,DropDownStyle=ComboBoxStyle.DropDownList};
    readonly DataGridView grid=NewGrid();
    readonly Label status=new Label{Dock=DockStyle.Bottom,Height=29,TextAlign=ContentAlignment.MiddleLeft,BackColor=Ink,ForeColor=Color.White};
    readonly Label dataTitle=Caption("本地数据库"),pageTitle=Caption("标签设计"),inspectTitle=Caption("对象属性");
    sealed class HistoryEntry {
        public LabelFile Document;
        public int[] Selection;
        public int Row;
    }
    readonly Stack<HistoryEntry> undo=new Stack<HistoryEntry>(),redo=new Stack<HistoryEntry>();
    readonly ToolStrip arrange=new ToolStrip{Dock=DockStyle.Top,GripStyle=ToolStripGripStyle.Hidden,BackColor=Pale};
    Button previewButton;
    List<Element> objectClipboard=new List<Element>();
    int pasteNumber;
    string file,lastPrinter=PrinterQueues.ReadLastUsed();bool dirty,sync;
    class FieldChoice{public string Name;public bool Serial;public override string ToString(){return Serial?"流水号":Name==null?"固定内容":"Excel · "+Name;}}
    public Studio():this(null){}
    public Studio(string startPath){
        Text="LigPrint";Font=new Font("Microsoft YaHei UI",9);Icon=new Icon(typeof(Studio).Assembly.GetManifestResourceStream("LigPrint.Icon"));
        ClientSize=new Size(1160,750);MinimumSize=new Size(870,580);StartPosition=FormStartPosition.CenterScreen;
        var head=new FlowLayoutPanel{Dock=DockStyle.Top,Height=56,Padding=new Padding(8,7,0,0),BackColor=Color.White,WrapContents=false};
        var logo=new PictureBox{Image=Image.FromStream(typeof(Studio).Assembly.GetManifestResourceStream("LigPrint.ToolbarIcon")),SizeMode=PictureBoxSizeMode.Zoom,Width=32,Height=32,Margin=new Padding(5,4,0,0)};
        head.Controls.Add(logo);head.Controls.Add(new Label{Text="LigPrint",ForeColor=Ink,Font=new Font("Segoe UI",16,FontStyle.Bold),TextAlign=ContentAlignment.MiddleLeft,Width=105,Height=38});
        ButtonFor(head,"新建",()=>NewDoc());ButtonFor(head,"打开",Open);ButtonFor(head,"保存",()=>Save(false));ButtonFor(head,"另存为",()=>Save(true));
        ButtonFor(head,"撤销",()=>History(undo,redo));ButtonFor(head,"重做",()=>History(redo,undo));previewButton=ButtonFor(head,"预览",TogglePreview);ButtonFor(head,"打印",Print,true);
        var left=new FlowLayoutPanel{Dock=DockStyle.Left,Width=106,FlowDirection=FlowDirection.TopDown,WrapContents=false,Padding=new Padding(7,8,4,0),BackColor=Pale,AutoScroll=true};
        left.Controls.Add(new Label{Text="设计工具",ForeColor=Ink,Height=29,Width=88,TextAlign=ContentAlignment.MiddleCenter});
        foreach(Kind t in Enum.GetValues(typeof(Kind))){Kind type=t;ButtonFor(left,t.ToString(),()=>Insert(type),false,82);}
        ButtonFor(left,"创建副本",Duplicate,false,82);ButtonFor(left,"复制",()=>{CopyObjects();canvas.Focus();},false,82);ButtonFor(left,"粘贴",PasteObjects,false,82);ButtonFor(left,"删除",Delete,false,82);ButtonFor(left,"标签尺寸",()=>ResizeLabel(false),false,82);
        var right=new Panel{Dock=DockStyle.Right,Width=232,Padding=new Padding(9),BackColor=Color.White};
        var bindCaption=Caption("数据来源");right.Controls.Add(props);right.Controls.Add(fields);right.Controls.Add(bindCaption);right.Controls.Add(inspectTitle);
        props.PropertyValueChanged+=(s,e)=>{
            try{doc.Validate();}
            catch(Exception ex){e.ChangedItem.PropertyDescriptor.SetValue(canvas.Selected,e.OldValue);props.Refresh();MessageBox.Show(this,ex.Message);return;}
            var old=Snapshot();int i=doc.Items.IndexOf(canvas.Selected);e.ChangedItem.PropertyDescriptor.SetValue(old.Items[i],e.OldValue);Remember(old);RefreshUI();
        };
        fields.SelectedIndexChanged+=(s,e)=>{
            if(sync || canvas.Selected==null || fields.SelectedIndex<0)return;
            Remember(Snapshot());var source=(FieldChoice)fields.SelectedItem;canvas.Selected.Field=source.Name;canvas.Selected.Serial=source.Serial;RefreshUI();
        };
        var dataBar=new FlowLayoutPanel{Dock=DockStyle.Top,Height=43,Padding=new Padding(5,3,0,0),BackColor=Color.White,WrapContents=false};
        ButtonFor(dataBar,"导入 Excel",Import,true,102);ButtonFor(dataBar,"刷新数据",Reload);ButtonFor(dataBar,"上一条",()=>SelectRow(canvas.Row-1));ButtonFor(dataBar,"下一条",()=>SelectRow(canvas.Row+1));dataTitle.AutoSize=true;dataTitle.Padding=new Padding(10,7,0,0);dataBar.Controls.Add(dataTitle);
        var dataPanel=new Panel{Dock=DockStyle.Bottom,Height=168,BackColor=Pale};dataPanel.Controls.Add(grid);dataPanel.Controls.Add(dataBar);
        grid.CellValueNeeded+=(s,e)=>{if(e.RowIndex<doc.Data.Rows.Count && e.ColumnIndex>=0)e.Value=doc.Data.Rows[e.RowIndex][e.ColumnIndex];};
        grid.SelectionChanged+=(s,e)=>{if(!sync && grid.CurrentCell!=null){canvas.Row=grid.CurrentCell.RowIndex;canvas.Invalidate();UpdateStatus();}};
        var center=new Panel{Dock=DockStyle.Fill};center.Controls.Add(canvas);SetupArrange();center.Controls.Add(arrange);center.Controls.Add(pageTitle);center.Controls.Add(dataPanel);
        Controls.Add(center);Controls.Add(right);Controls.Add(left);Controls.Add(head);Controls.Add(status);
        canvas.MouseDoubleClick+=(s,e)=>{
            var item=canvas.Selected;
            if(e.Button!=MouseButtons.Left || canvas.Preview || canvas.Selection.Length!=1 ||
                item.Type!=Kind.文本 || item.Serial || !String.IsNullOrEmpty(item.Field))return;
            Guard(()=>{
                var value=LabelTextEditor.Edit(this,item.Text);
                if(value==item.Text)return;
                Remember(Snapshot());item.Text=value;RefreshUI();canvas.Focus();
            });
        };
        canvas.SelectionChanged+=RefreshUI;canvas.BeforeDrag+=()=>Remember(Snapshot());canvas.Moved+=RefreshUI;
        FormClosing+=(s,e)=>{if(!ConfirmChanges())e.Cancel=true;};
        AllowDrop=true;
        DragEnter+=(sender,e)=>e.Effect=(DroppedTemplate(e.Data)==null?DragDropEffects.None:DragDropEffects.Copy) & e.AllowedEffect;
        DragDrop+=(sender,e)=>{
            string path=DroppedTemplate(e.Data);
            if(path!=null)Guard(()=>{if(ConfirmChanges())OpenPath(path,false);});
        };
        if(startPath!=null)OpenPath(startPath,false);
        else ResetSelection();
    }
    static Label Caption(string title){return new Label{Text=title,Dock=DockStyle.Top,Height=34,Padding=new Padding(8,0,0,0),TextAlign=ContentAlignment.MiddleLeft,ForeColor=Ink,BackColor=Color.White};}
    static DataGridView NewGrid(){return new DataGridView{Dock=DockStyle.Fill,VirtualMode=true,ReadOnly=true,AllowUserToAddRows=false,AllowUserToDeleteRows=false,AllowUserToOrderColumns=false,MultiSelect=false,SelectionMode=DataGridViewSelectionMode.FullRowSelect,BackgroundColor=Pale,BorderStyle=BorderStyle.None,RowHeadersWidth=42,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.DisplayedCells};}
    Button ButtonFor(Control parent,string label,Action act,bool primary=false,int width=67){
        var b=new Button{Text=label,Width=width,Height=32,FlatStyle=FlatStyle.Flat,BackColor=primary?Accent:Color.White,ForeColor=primary?Color.White:Ink,Cursor=Cursors.Hand};
        b.FlatAppearance.BorderColor=primary?Accent:Color.FromArgb(213,222,232);b.Click+=(s,e)=>Guard(act);parent.Controls.Add(b);return b;
    }
    void Guard(Action act){try{act();}catch(Exception ex){MessageBox.Show(this,ex.GetBaseException().Message,"LigPrint",MessageBoxButtons.OK,MessageBoxIcon.Information);}}
    LabelFile Snapshot(){return new LabelFile{Width=doc.Width,Height=doc.Height,Items=doc.Items.Select(i=>i.Copy()).ToList(),Data=doc.Data};}
    HistoryEntry Entry(LabelFile document){
        return new HistoryEntry{Document=document,Selection=canvas.Selection.Select(item=>doc.Items.IndexOf(item)).Where(i=>i>=0).ToArray(),Row=canvas.Row};
    }
    void Remember(LabelFile previous){
        if(undo.Count>=32){var keep=undo.Take(31).Reverse().ToArray();undo.Clear();foreach(var v in keep)undo.Push(v);}
        undo.Push(Entry(previous));redo.Clear();dirty=true;
    }
    void History(Stack<HistoryEntry> src,Stack<HistoryEntry> dst){
        if(src.Count==0)return;
        dst.Push(Entry(Snapshot()));var entry=src.Pop();doc=entry.Document;dirty=true;
        canvas.Doc=doc;canvas.Preview=false;canvas.Row=entry.Row;
        canvas.SelectGroup(entry.Selection.Select(i=>doc.Items[i]));RefreshData();
    }
    void ResetSelection(){canvas.Selected=null;canvas.Doc=doc;canvas.Preview=false;canvas.Row=0;RefreshData();}
    void RefreshData(){
        sync=true;grid.RowCount=0;grid.Columns.Clear();
        foreach(var h in doc.Data.Headers){int i=grid.Columns.Add("c"+grid.Columns.Count,h);grid.Columns[i].SortMode=DataGridViewColumnSortMode.NotSortable;}
        if(grid.Columns.Count>0)grid.RowCount=doc.Data.Rows.Count;sync=false;SelectRow(canvas.Row);RefreshUI();
    }
    void SelectRow(int row){
        canvas.Row=Math.Max(0,Math.Min(doc.RecordLimit-1,row));
        if(grid.Rows.Count>0)grid.CurrentCell=grid.Rows[canvas.Row].Cells[0];canvas.Invalidate();UpdateStatus();
    }
    void UpdateStatus(){
        status.Text=String.Format("    {0:0.##} × {1:0.##} mm     |     {2} 个对象     |     {3}",doc.Width,doc.Height,doc.Items.Count,doc.Data.Rows.Count>0?"记录 "+(canvas.Row+1)+" / "+doc.Data.Rows.Count:doc.RecordLimit>1?"流水号记录 "+(canvas.Row+1):"固定内容");
    }
    void RefreshUI(){
        sync=true;var item=canvas.Selection.Length==1?canvas.Selected:null;arrange.Enabled=item!=null && !canvas.Preview;previewButton.Text=canvas.Preview?"返回设计":"预览";
        props.SelectedObject=canvas.Preview?null:item;props.Refresh();
        fields.Items.Clear();fields.Items.Add(new FieldChoice());fields.Items.Add(new FieldChoice{Serial=true});
        foreach(var h in doc.Data.Headers)fields.Items.Add(new FieldChoice{Name=h});
        fields.Enabled=!canvas.Preview && item!=null && (item.Type==Kind.文本||item.Type==Kind.条形码||item.Type==Kind.二维码);
        fields.SelectedIndex=item!=null && item.Serial?1:item==null||String.IsNullOrEmpty(item.Field)?0:doc.Data.Headers.IndexOf(item.Field)+2;
        inspectTitle.Text=canvas.Selection.Length>1?"已选 "+canvas.Selection.Length+" 个对象":item==null?"对象属性":"对象属性  /  "+item.Type;
        var title=file==null?"未命名标签":Path.GetFileName(file);Text=(dirty?"* ":"")+title+"  —  LigPrint";
        pageTitle.Text=(canvas.Preview?"打印预览":"标签设计")+"   /   "+title;
        dataTitle.Text=doc.Data.Rows.Count==0?"本地数据库":"工作表  "+doc.Data.Name+"    "+doc.Data.Rows.Count+" 条记录";
        UpdateStatus();sync=false;canvas.Invalidate();
    }
    void SetupArrange(){
        var align=new ToolStripDropDownButton("对齐");
        foreach(var text in new[]{"靠左","水平居中","靠右","靠上","垂直居中","靠下"}){
            string side=text;align.DropDownItems.Add(text,null,(s,e)=>EditObject(item=>{
                if(item.Locked)return;
                switch(side){
                    case "靠左":item.X=0;break;case "水平居中":item.X=Math.Max(0,(doc.Width-item.W)/2);break;
                    case "靠右":item.X=Math.Max(0,doc.Width-item.W);break;case "靠上":item.Y=0;break;
                    case "垂直居中":item.Y=Math.Max(0,(doc.Height-item.H)/2);break;case "靠下":item.Y=Math.Max(0,doc.Height-item.H);break;
                }
            }));
        }
        arrange.Items.Add(align);
        var layer=new ToolStripDropDownButton("图层");
        layer.DropDownItems.Add("置于顶层",null,(s,e)=>EditObject(item=>{doc.Items.Remove(item);doc.Items.Add(item);}));
        layer.DropDownItems.Add("置于底层",null,(s,e)=>EditObject(item=>{doc.Items.Remove(item);doc.Items.Insert(0,item);}));
        arrange.Items.Add(layer);arrange.Items.Add(new ToolStripSeparator());
        arrange.Items.Add("旋转 90°",null,(s,e)=>EditObject(item=>item.Rotation=(Turn)(((int)item.Rotation+1)%4)));
        arrange.Items.Add("水平镜像",null,(s,e)=>EditObject(item=>item.Mirror=!item.Mirror));
        arrange.Items.Add("锁定 / 解锁",null,(s,e)=>EditObject(item=>item.Locked=!item.Locked));
    }
    void EditObject(Action<Element> edit){
        if(canvas.Selected==null || canvas.Preview)return;
        Remember(Snapshot());edit(canvas.Selected);RefreshUI();
    }
    void TogglePreview(){canvas.Preview=!canvas.Preview;RefreshUI();}
    void Insert(Kind kind){
        var item=new Element{Type=kind};
        if(kind==Kind.图片){
            var p=LocalPicker.Pick(this,"插入图片",new[]{".png",".jpg",".jpeg",".bmp"});
            if(p==null)return;if(new FileInfo(p).Length>12*1024*1024)throw new IOException("图片须小于 12 MB。");
            item.ImageBytes=File.ReadAllBytes(p);using(var ms=new MemoryStream(item.ImageBytes))using(var img=Image.FromStream(ms)){item.W=Math.Min(30,doc.Width);item.H=item.W*img.Height/img.Width;}
        }
        if(kind==Kind.二维码){item.Text="LigPrint";item.W=item.H=18;}
        if(kind==Kind.条形码){item.Text="1234567890";item.H=10;item.W=42;}
        if(kind==Kind.横线)item.H=1;
        item.W=Math.Min(item.W,doc.Width);item.H=Math.Min(item.H,doc.Height);
        float marginX=Math.Min(2,doc.Width-item.W),marginY=Math.Min(2,doc.Height-item.H);
        var positions=new List<PointF>{new PointF(marginX,marginY)};
        foreach(var other in doc.Items){
            positions.Add(new PointF(other.X+other.W+1,other.Y));
            positions.Add(new PointF(marginX,other.Y+other.H+1));
        }
        item.X=marginX;item.Y=marginY;
        foreach(var pos in positions.OrderBy(pt=>pt.Y).ThenBy(pt=>pt.X)){
            var rect=new RectangleF(pos.X,pos.Y,item.W,item.H);
            if(rect.Right>doc.Width || rect.Bottom>doc.Height)continue;
            if(doc.Items.Exists(el=>rect.IntersectsWith(new RectangleF(el.X,el.Y,el.W,el.H))))continue;
            item.X=pos.X;item.Y=pos.Y;break;
        }
        Remember(Snapshot());doc.Items.Add(item);canvas.Preview=false;canvas.Selected=item;RefreshUI();canvas.Focus();
    }
    void CopyObjects(){
        if(canvas.Preview || canvas.Selected==null)return;
        var selected=new HashSet<Element>(canvas.Selection);
        objectClipboard=doc.Items.Where(selected.Contains).Select(item=>item.Copy()).ToList();
        pasteNumber=0;
    }
    static float Offset(float near,float far,float size,int number){
        float forward=Math.Max(0,size-far),back=Math.Max(0,near);
        int positive=(int)Math.Ceiling(forward/2),negative=(int)Math.Ceiling(back/2);
        int available=positive+negative;
        if(available==0)return 0;
        int cycle=(number-1)/available;
        int position=(number-1)%available;
        bool ahead=position<positive;
        int step=ahead?position:position-positive;
        float distance=Math.Min(ahead?forward:back,2*(step+1));
        // After both edges have been reached, use the gaps between earlier copies.
        distance-=Math.Min(distance,1f)*cycle/(cycle+1f);
        return ahead?distance:-distance;
    }
    void PasteObjects(){
        if(canvas.Preview || objectClipboard.Count==0)return;
        if(doc.Items.Count+objectClipboard.Count>2000)throw new InvalidOperationException("模板最多支持 2000 个对象。");
        var copies=objectClipboard.Select(item=>item.Copy()).ToList();
        if(copies.Any(i=>i.Type!=Kind.文本 && (i.X+i.W>doc.Width+.001f || i.Y+i.H>doc.Height+.001f)))
            throw new InvalidOperationException("复制的对象超出标签尺寸，请在相同或更大的标签上粘贴。");
        pasteNumber++;
        float dx=Offset(copies.Min(i=>i.X),copies.Max(i=>i.X+i.W),doc.Width,pasteNumber);
        float dy=Offset(copies.Min(i=>i.Y),copies.Max(i=>i.Y+i.H),doc.Height,pasteNumber);
        Remember(Snapshot());
        foreach(var item in copies){item.X+=dx;item.Y+=dy;doc.Items.Add(item);}
        canvas.SelectGroup(copies);RefreshUI();canvas.Focus();
    }
    void Duplicate(){if(canvas.Selected!=null && !canvas.Preview){CopyObjects();PasteObjects();}}
    void Delete(){
        if(canvas.Selected==null || canvas.Preview)return;
        var selected=new HashSet<Element>(canvas.Selection);
        Remember(Snapshot());doc.Items.RemoveAll(selected.Contains);canvas.Selected=null;RefreshUI();canvas.Focus();
    }
    bool ConfirmChanges(){
        if(!dirty)return true;var ans=MessageBox.Show(this,"保存当前标签的更改？","LigPrint",MessageBoxButtons.YesNoCancel,MessageBoxIcon.Question);
        if(ans==DialogResult.Cancel)return false;if(ans==DialogResult.No)return true;
        try{return Save(false);}catch(Exception ex){MessageBox.Show(this,ex.Message);return false;}
    }
    void NewDoc(){if(ConfirmChanges())ResizeLabel(true);}
    void ResizeLabel(bool fresh){
        using(var p=new Prompt(fresh?"新建标签":"标签尺寸",340,147)){
            var w=p.Number("宽度（mm）",(decimal)(fresh?60:doc.Width),5,500,2);var h=p.Number("高度（mm）",(decimal)(fresh?40:doc.Height),5,500,2);
            if(p.ShowDialog(this)!=DialogResult.OK)return;
            if(fresh){doc=new LabelFile();file=null;undo.Clear();redo.Clear();}
            else {
                if(doc.Items.Any(e=>e.Type!=Kind.文本 && (e.X+e.W>(float)w.Value+.001 || e.Y+e.H>(float)h.Value+.001)))throw new InvalidOperationException("对象超出新标签，请先调整位置或尺寸。");
                Remember(Snapshot());
            }
            dirty=true;doc.Width=(float)w.Value;doc.Height=(float)h.Value;ResetSelection();
        }
    }
    void Open(){
        if(!ConfirmChanges())return;
        string path=LocalPicker.Pick(this,"打开 LigPrint 标签或转换 DLabel 版式",new[]{".lig",".ddl",".png"});
        if(path==null)return;
        OpenPath(path,true);
    }
    static string DroppedTemplate(IDataObject data){
        if(data==null || !data.GetDataPresent(DataFormats.FileDrop,false))return null;
        var files=data.GetData(DataFormats.FileDrop,false) as string[];
        if(files==null || files.Length!=1)return null;
        return String.Equals(Path.GetExtension(files[0]),".lig",StringComparison.OrdinalIgnoreCase)?files[0]:null;
    }
    void OpenPath(string path,bool allowConversion){
        path=Disk.Local(Path.GetFullPath(path));
        bool converted=!String.Equals(Path.GetExtension(path),".lig",StringComparison.OrdinalIgnoreCase);
        if(converted && !allowConversion)throw new InvalidDataException("请选择 .lig 标签模板。");
        var loaded=converted?DLabelLayout.Import(this,path):LabelFile.Load(path);
        if(loaded==null)return;
        doc=loaded;file=converted?null:path;dirty=converted;
        undo.Clear();redo.Clear();ResetSelection();
        Audit.Write(converted?"import-layout":"open",doc.Items.Count);
    }
    bool Save(bool other){
        string p=other||file==null?LocalPicker.Pick(this,"保存标签",new[]{".lig"},true,file==null?"标签.lig":Path.GetFileName(file)):file;
        if(p==null)return false;doc.Save(p);file=p;dirty=false;RefreshUI();Audit.Write("save",doc.Items.Count);return true;
    }
    void Reload(){
        if(String.IsNullOrEmpty(doc.Data.LocalPath)){Import();return;}
        var sheets=ExcelBook.Read(doc.Data.LocalPath);
        var next=sheets.Find(s=>s.Name==doc.Data.Name);
        if(next==null)throw new IOException("原工作表已改名，请重新导入。");
        ApplySheet(next);
    }
    void ApplySheet(Sheet next){
        var missing=doc.Items.Where(i=>!String.IsNullOrEmpty(i.Field)&&!next.Headers.Contains(i.Field)).Select(i=>i.Field).Distinct().ToArray();
        if(missing.Length>0)throw new IOException("工作表缺少已绑定字段："+String.Join("、",missing)+"。请先将对应对象改为固定内容。");
        Remember(Snapshot());doc.Data=next;canvas.Row=0;RefreshData();Audit.Write("import",next.Rows.Count);
    }
    void Import(){
        string path=LocalPicker.Pick(this,"导入本地 Excel",new[]{".xls",".xlsx",".xlsb"});if(path==null)return;
        List<Sheet> sheets;UseWaitCursor=true;try{sheets=ExcelBook.Read(path);}finally{UseWaitCursor=false;}
        using(var p=new Prompt("导入本地数据库",640,320)){
            var choice=p.Choice("选择工作表",sheets.Select(s=>s.Name));
            var preview=NewGrid();preview.Dock=DockStyle.Bottom;preview.Height=193;
            // Place the preview above the confirmation buttons using the table's remaining row.
            var area=(TableLayoutPanel)choice.Parent;preview.Dock=DockStyle.Fill;area.Controls.Add(preview,0,1);area.SetColumnSpan(preview,2);area.RowCount=2;area.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            preview.CellValueNeeded+=(s,e)=>e.Value=sheets[choice.SelectedIndex].Rows[e.RowIndex][e.ColumnIndex];
            Action refresh=()=>{preview.RowCount=0;preview.Columns.Clear();var sheet=sheets[choice.SelectedIndex];foreach(string h in sheet.Headers)preview.Columns.Add("p"+preview.Columns.Count,h);preview.RowCount=Math.Min(100,sheet.Rows.Count);};
            choice.SelectedIndexChanged+=(s,e)=>refresh();refresh();if(p.ShowDialog(this)!=DialogResult.OK)return;
            ApplySheet(sheets[choice.SelectedIndex]);
        }
    }
    void Print(){
        if(doc.Items.Count==0)throw new IOException("请先添加标签内容。");
        List<string> queues;
        string notice=null;
        try{
            queues=Printing.Queues();
            if(queues.Count==0)notice="Windows 当前没有已安装的打印队列。";
        }catch(System.ComponentModel.Win32Exception ex){
            queues=new List<string>();
            notice="读取打印队列失败（"+ex.NativeErrorCode+"）："+ex.Message+"。请检查 Print Spooler 服务。";
        }
        using(var p=new BatchPreview(doc,file==null?"未命名标签":Path.GetFileName(file),queues,lastPrinter,notice,canvas.Row)){
            if(p.ShowDialog(this)!=DialogResult.OK)return;
            string target=p.Target;
            string extension=target==null || String.Equals(target,"Microsoft Print to PDF",StringComparison.OrdinalIgnoreCase)?".pdf":
                String.Equals(target,"Microsoft XPS Document Writer",StringComparison.OrdinalIgnoreCase)?".oxps":null;
            string output=null;
            if(extension!=null){
                output=LocalPicker.Pick(this,"选择输出文件",new[]{extension},true,"标签"+extension);
                if(output==null)return;
            }
            var records=p.Records;var outputDoc=p.Output;
            using(var job=new JobProgress(report=>Printing.SubmitRows(outputDoc,target,records,output,report))){
                job.ShowDialog(this);
                if(job.Error is OperationCanceledException){status.Text="    已取消后续输出";Audit.Write("print-cancel",0);return;}
                if(job.Error!=null)throw job.Error;
            }
            if(target!=null){lastPrinter=target;PrinterQueues.Remember(target);}
            int pages=records.Sum(entry=>entry.Copies);status.Text="    已提交 "+pages+" 张标签";Audit.Write("print-submitted",pages);
        }
    }
    protected override bool ProcessCmdKey(ref Message m,Keys key){
        Action act=null;
        switch(key){
            case Keys.Control|Keys.N:act=NewDoc;break;case Keys.Control|Keys.O:act=Open;break;
            case Keys.Control|Keys.S:act=()=>Save(false);break;case Keys.Control|Keys.P:act=Print;break;
            case Keys.Control|Keys.Z:if(!props.ContainsFocus)act=()=>History(undo,redo);break;
            case Keys.Control|Keys.Y:if(!props.ContainsFocus)act=()=>History(redo,undo);break;
            case Keys.Control|Keys.C:if(canvas.Focused && !canvas.Preview)act=CopyObjects;break;
            case Keys.Control|Keys.V:if(canvas.Focused && !canvas.Preview)act=PasteObjects;break;
            case Keys.Control|Keys.X:if(canvas.Focused && !canvas.Preview)act=()=>{CopyObjects();Delete();};break;
            case Keys.Control|Keys.A:if(canvas.Focused && !canvas.Preview)act=()=>{canvas.SelectGroup(doc.Items);RefreshUI();};break;
            case Keys.Delete:if(canvas.Focused && !canvas.Preview)act=Delete;break;
        }
        if(act==null)return base.ProcessCmdKey(ref m,key);Guard(act);return true;
    }
}
}












