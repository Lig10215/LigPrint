using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using LigPrint;

// Run in an STA test process. All print output goes to files under the supplied directory.
class VerifyEditor {
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    static T Field<T>(object obj,string name){return (T)obj.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(obj);}
    static IEnumerable<Control> Children(Control root){
        foreach(Control child in root.Controls){yield return child;foreach(var nested in Children(child))yield return nested;}
    }
    static void Click(Control root,string text){Children(root).OfType<Button>().Single(b=>b.Text==text).PerformClick();Application.DoEvents();}
    static bool Key(Studio studio,Keys key){
        var method=typeof(Studio).GetMethod("ProcessCmdKey",BindingFlags.NonPublic|BindingFlags.Instance);
        return (bool)method.Invoke(studio,new object[]{new Message(),key});
    }
    static void Capture(Form f,string path){
        using(var bitmap=new Bitmap(f.Width,f.Height)){f.DrawToBitmap(bitmap,new Rectangle(Point.Empty,f.Size));bitmap.Save(path);}
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern IntPtr SendMessage(IntPtr hwnd,int msg,IntPtr key,IntPtr position);
    static void ClickCell(DataGridView grid,int col,int row,bool edge=false){
        var box=grid.GetCellDisplayRectangle(col,row,true);
        int x=edge?box.Left+3:box.Left+box.Width/2;
        int y=box.Top+box.Height/2;
        Check(grid.HitTest(x,y).RowIndex==row && grid.HitTest(x,y).ColumnIndex==col,"mouse hit test");
        var position=new IntPtr((y<<16)|x);
        SendMessage(grid.Handle,0x0200,IntPtr.Zero,position);
        SendMessage(grid.Handle,0x0201,new IntPtr(1),position);
        SendMessage(grid.Handle,0x0202,IntPtr.Zero,position);
        Application.DoEvents();
    }
    static void HeaderClick(DataGridView grid,CheckBox header){
        var cell=grid.GetCellDisplayRectangle(0,-1,true);
        var point=new Point(header.Left+header.Width/2,header.Top+header.Height/2);
        Check(cell.Contains(header.Bounds) && grid.GetChildAtPoint(point)==header,"header bounds "+header.Bounds+" should fit "+cell);
        var position=new IntPtr((header.Height/2<<16)|header.Width/2);
        SendMessage(header.Handle,0x0201,new IntPtr(1),position);
        SendMessage(header.Handle,0x0202,IntPtr.Zero,position);
        Application.DoEvents();
    }
    static void HeaderSelection(string folder){
        var doc=new LabelFile();
        doc.Data.Headers.Add("批号");
        for(int i=1;i<=12;i++)doc.Data.Rows.Add(new[]{i<=3?"A":"B"});
        doc.Items.Add(new Element{Type=Kind.文本,Field="批号"});
        using(var f=new BatchPreview(doc,"表头全选测试",new List<string>(),null,null,0)){
            f.Show();f.Activate();Application.DoEvents();
            var grid=Children(f).OfType<DataGridView>().Single();
            var box=Children(grid).OfType<CheckBox>().Single();
            var summary=Field<Label>(f,"summary");var preview=Field<Label>(f,"previewInfo");
            var submit=Field<Button>(f,"submit");var search=Field<TextBox>(f,"keyword");
            Action<CheckState,int,int> verify=(state,records,pages)=>{
                Check(box.CheckState==state && summary.Text.Contains("："+records+" / 12") &&
                    submit.Text.Contains("（"+pages+" 张）") && submit.Enabled==(pages>0),"header, count and enabled state");
                Check((preview.Text=="未选择打印记录")==(records==0),"preview follows empty selection");
            };
            verify(CheckState.Checked,12,12);
            Check(grid.Rows.Cast<DataGridViewRow>().All(r=>Convert.ToBoolean(r.Cells[0].Value)),"initial checkmarks");
            for(int i=0;i<2;i++){
                HeaderClick(grid,box);verify(CheckState.Unchecked,0,0);
                HeaderClick(grid,box);verify(CheckState.Checked,12,12);
            }
            ClickCell(grid,0,3);verify(CheckState.Indeterminate,11,11);
            HeaderClick(grid,box);verify(CheckState.Checked,12,12);
            grid.CurrentCell=grid.Rows[0].Cells[2];grid.BeginEdit(true);((TextBox)grid.EditingControl).Text="3";Check(grid.EndEdit(),"commit copies");
            search.Text="A";Click(f,"搜索");Check(grid.RowCount==3,"three matching records");
            Click(f,"清除结果");verify(CheckState.Indeterminate,9,9);
            Click(f,"反选结果");verify(CheckState.Checked,12,14);
            HeaderClick(grid,box);verify(CheckState.Unchecked,0,0);
            Click(f,"全选结果");verify(CheckState.Indeterminate,3,5);
            HeaderClick(grid,box);verify(CheckState.Checked,12,14);
            search.Text="absent";Click(f,"搜索");Check(grid.RowCount==0,"empty filter");
            HeaderClick(grid,box);verify(CheckState.Unchecked,0,0);
            HeaderClick(grid,box);verify(CheckState.Checked,12,14);
            search.Text="";Click(f,"搜索");
            Capture(f,Path.Combine(folder,"header-selection.png"));
            submit.PerformClick();
            Check(f.Records.Count==12 && f.Records.Sum(r=>r.Copies)==14,"global submitted records");
            Printing.SubmitRows(f.Output,null,f.Records,Path.Combine(folder,"header-selection.pdf"),(done,total)=>{});
        }
    }
    static void FeedbackCases(string folder){
        var doc=new LabelFile{Width=30,Height=40};
        doc.Data.Headers.Add("编码");
        for(int n=1;n<=8;n++)doc.Data.Rows.Add(new[]{n.ToString("0000")});
        doc.Items.Add(new Element{Type=Kind.文本,Field="编码",W=20,H=5});
        string path=Path.Combine(folder,"长标签 打印模板.lig");
        doc.Save(path);
        using(var f=new Studio(path)){
            f.Show();Application.DoEvents();
            var c=Children(f).OfType<Canvas>().Single();
            Check(f.Text.Contains(Path.GetFileName(path)) && c.Doc.Items.Count==1 && c.Doc.Data.Rows.Count==8 &&
                c.Doc.Items[0].Value(c.Doc.Data,0)=="0001","template already populated at first display");
            doc.Items.Add(new Element{Type=Kind.矩形,X=1,Y=8,W=20,H=10});
            string next=Path.Combine(folder,"拖入模板.lig");doc.Save(next);
            var data=new DataObject(DataFormats.FileDrop,new[]{next});
            var drag=new DragEventArgs(data,1,0,0,DragDropEffects.Copy,DragDropEffects.None);
            Check(f.AllowDrop,"window drop target");
            typeof(Control).GetMethod("OnDragEnter",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(f,new object[]{drag});
            Check(drag.Effect==DragDropEffects.Copy,"accept explorer file drop");
            typeof(Control).GetMethod("OnDragDrop",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(f,new object[]{drag});
            Check(f.Text.Contains("拖入模板.lig") && c.Doc.Items.Count==2 && c.Doc.Data.Rows.Count==8,"file drop loads document");
        }
        using(var f=new BatchPreview(doc,"长标签",new List<string>(),null,null,0)){
            f.Show();f.Activate();Application.DoEvents();
            var grid=Children(f).OfType<DataGridView>().Single();
            Field<NumericUpDown>(f,"last").Value=2;
            var ok=Field<Button>(f,"submit");var summary=Field<Label>(f,"summary");var preview=Field<Label>(f,"previewInfo");
            Check(ok.Text.Contains("2 张"),"initial sequential range");
            ClickCell(grid,1,3);
            Check(preview.Text.Contains("记录 4") && ok.Text.Contains("2 张"),"data click previews only");
            ClickCell(grid,0,3);
            Check(Convert.ToBoolean(grid.Rows[3].Cells[0].Value) && Field<ComboBox>(f,"mode").SelectedIndex==1 &&
                summary.Text.Contains("3 / 8") && ok.Text.Contains("3 张"),"one mouse click keeps range and adds record 4");
            grid.CurrentCell=grid.Rows[3].Cells[2];grid.BeginEdit(true);((TextBox)grid.EditingControl).Text="3";Check(grid.EndEdit(),"commit quantity");
            ClickCell(grid,0,3,true);
            Check(!Convert.ToBoolean(grid.Rows[3].Cells[0].Value) && summary.Text.Contains("2 / 8") && ok.Text.Contains("2 张"),"uncheck via cell padding");
            ClickCell(grid,0,3);
            Check(summary.Text.Contains("3 / 8") && ok.Text.Contains("5 张") && preview.Text.Contains("记录 4"),"recheck restores quantity and preview");
            ok.PerformClick();
            Check(f.Records.Select(r=>r.Index).SequenceEqual(new[]{0,1,3}) && f.Records[2].Copies==3,"submitted records reflect mouse selection");
            Printing.SubmitRows(f.Output,null,f.Records,Path.Combine(folder,"row-click.pdf"),(done,total)=>{});
        }
    }
    static void Batch(string folder){
        var doc=new LabelFile();
        doc.Data.Headers.AddRange(new[]{"编号","品名","份数"});
        for(int i=1;i<=3407;i++)doc.Data.Rows.Add(new[]{i.ToString("0000"),i==3||i==3407?"匹配":"其它",i==3?"2":"1"});
        doc.Items.Add(new Element{Type=Kind.二维码,Field="编号",X=2,Y=2,W=23,H=23});
        doc.Items.Add(new Element{Type=Kind.文本,Field="品名",X=26,Y=2,W=30,H=10});
        var queues=Printing.Queues();
        using(var f=new BatchPreview(doc,"回归验证.lig",queues,null,null,2)){
            f.Show();Application.DoEvents();
            var grid=Children(f).OfType<DataGridView>().Single();
            var mode=Field<ComboBox>(f,"mode");var qty=Field<ComboBox>(f,"quantityField");
            var cols=Field<CheckedListBox>(f,"columnList");var colQuery=Field<TextBox>(f,"findColumn");
            var query=Field<TextBox>(f,"keyword");var searchField=Field<ComboBox>(f,"searchField");
            var ok=Field<Button>(f,"submit");
            Check(grid.RowCount==3407 && ok.Enabled,"initial rows");
            mode.SelectedIndex=1;Check(ok.Enabled && ok.Text.Contains("3407 张"),"initial custom selection");Click(f,"清除结果");Check(!ok.Enabled,"clear before subset selection");
            qty.SelectedIndex=3;
            colQuery.Text="份";Click(f,"反选列");Check(!grid.Columns[5].Visible,"column name filter and invert");
            Click(f,"全选列");Check(grid.Columns[5].Visible,"select all matching fields");
            colQuery.Text="";
            searchField.SelectedItem="品名";query.Text="匹配";Click(f,"搜索");
            Check(grid.RowCount==2 && Convert.ToInt32(grid.Rows[1].Cells[1].Value)==3407,"filtered row identity");
            Click(f,"全选结果");Check(ok.Text.Contains("3 张"),"copies from spreadsheet");
            grid.CurrentCell=grid.Rows[1].Cells[2];grid.BeginEdit(true);((TextBox)grid.EditingControl).Text="3";
            Check(grid.EndEdit(),"edit copies");
            Check(ok.Text.Contains("5 张"),"per-record override");
            Click(f,"反选结果");Check(!ok.Enabled,"invert matching rows");Click(f,"全选结果");
            query.Text="其它";Click(f,"搜索");Check(grid.RowCount==3405 && ok.Text.Contains("5 张"),"filter preserves hidden selection");
            query.Text="无此记录";Click(f,"搜索");Check(grid.RowCount==0 && ok.Enabled,"empty list retains selections");
            query.Text="";Click(f,"搜索");
            ClickCell(grid,0,0);
            Check(ok.Text.Contains("6 张"),"single checkbox on");ClickCell(grid,0,0);
            cols.SetItemChecked(1,false);Application.DoEvents();
            Check(f.Output.Items.Count==1 && doc.Items.Count==2 && !grid.Columns[4].Visible,"binding participation");
            var turn=Field<ComboBox>(f,"orientation");turn.SelectedIndex=1;
            Check(f.Output.Width==40 && f.Output.Height==60,"orientation");
            var target=Field<ComboBox>(f,"printers");target.SelectedIndex=target.Items.Count-1;
            grid.CurrentCell=grid.Rows[2].Cells[1];Application.DoEvents();
            Capture(f,Path.Combine(folder,"batch.png"));
            ok.PerformClick();Check(f.Records.Count==2 && f.Records[0].Index==2 && f.Records[0].Copies==2 &&
                f.Records[1].Index==3406 && f.Records[1].Copies==3,"job records");
            Printing.SubmitRows(f.Output,f.Target,f.Records,Path.Combine(folder,"custom.pdf"),(done,total)=>{});
            if(queues.Contains("Microsoft Print to PDF"))
                Printing.SubmitRows(f.Output,"Microsoft Print to PDF",f.Records,Path.Combine(folder,"driver-custom.pdf"),(done,total)=>{});
        }
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool GetKeyboardState(byte[] state);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetKeyboardState(byte[] state);
    static void CanvasClick(Canvas c,Element item,bool control){
        int x=(int)(Field<float>(c,"ox")+(item.X+item.W/2)*Field<float>(c,"scale"));
        int y=(int)(Field<float>(c,"oy")+(item.Y+item.H/2)*Field<float>(c,"scale"));
        byte[] old=new byte[256];Check(GetKeyboardState(old),"keyboard state");
        var keys=(byte[])old.Clone();keys[(int)Keys.ControlKey]=control?(byte)128:(byte)0;
        try{
            Check(SetKeyboardState(keys),"set test thread modifier");
            var args=new MouseEventArgs(MouseButtons.Left,1,x,y,0);
            foreach(string method in new[]{"OnMouseDown","OnMouseUp"})
                typeof(Canvas).GetMethod(method,BindingFlags.NonPublic|BindingFlags.Instance).Invoke(c,new object[]{args});
        }finally{SetKeyboardState(old);}
    }
    static void CanvasMouse(Canvas canvas,int msg,int buttons,float x,float y){
        float scale=Field<float>(canvas,"scale");
        int px=(int)Math.Round(Field<float>(canvas,"ox")+x*scale);
        int py=(int)Math.Round(Field<float>(canvas,"oy")+y*scale);
        SendMessage(canvas.Handle,msg,new IntPtr(buttons),new IntPtr((py<<16)|(px&65535)));
        Application.DoEvents();
    }
    static void DragCanvas(Canvas canvas,float x,float y,float endX,float endY){
        CanvasMouse(canvas,0x0201,1,x,y);
        CanvasMouse(canvas,0x0200,1,endX,endY);
        CanvasMouse(canvas,0x0202,0,endX,endY);
    }
    static void MarqueeTest(string folder){
        var doc=new LabelFile();
        doc.Items.Add(new Element{Type=Kind.矩形,X=0,Y=0,W=60,H=40,Locked=true});
        doc.Data.Headers.Add("品名");doc.Data.Rows.Add(new[]{"样品"});
        doc.Items.Add(new Element{Type=Kind.文本,X=5,Y=5,W=12,H=4,Field="品名"});
        doc.Items.Add(new Element{Type=Kind.文本,X=5,Y=12,W=15,H=4,Bold=true});
        doc.Items.Add(new Element{Type=Kind.文本,X=35,Y=25,W=14,H=5});
        string path=Path.Combine(folder,"marquee.lig");doc.Save(path);
        using(var form=new Studio(path)){
            form.Show();form.Activate();Application.DoEvents();
            var c=Children(form).OfType<Canvas>().Single();
            DragCanvas(c,23,18,3,3);
            var group=c.Selection;
            Check(group.SequenceEqual(c.Doc.Items.Skip(1).Take(2)),"reverse marquee over locked background");
            DragCanvas(c,8,7,13,10);
            Check(Math.Abs(group[0].X-10)<.2 && Math.Abs(group[0].Y-8)<.2 &&
                group[0].X==group[1].X && Math.Abs(group[1].Y-group[0].Y-7)<.001 &&
                c.Doc.Items[3].X==35 && c.Doc.Items[0].X==0,"group drag with fixed spacing");
            Key(form,Keys.Control|Keys.Z);
            Check(c.Selection.Length==2 && c.Selection[0].X==5 && c.Selection[1].Y==12,"undo one group move");
            Key(form,Keys.Control|Keys.Y);
            Key(form,Keys.Control|Keys.C);Key(form,Keys.Control|Keys.V);
            var copies=c.Selection;
            Check(c.Doc.Items.Count==6 && copies.Length==2 && copies[0].X>c.Doc.Items[1].X &&
                copies[0].Field=="品名" && copies[1].Bold && Math.Abs(copies[1].Y-copies[0].Y-7)<.001,"paste marquee group intact");
            Key(form,Keys.Control|Keys.V);Check(c.Doc.Items.Count==8 && c.Selection[0].X>copies[0].X,"repeat group paste");
            Key(form,Keys.Control|Keys.Z);Key(form,Keys.Control|Keys.Z);Key(form,Keys.Control|Keys.Z);
            DragCanvas(c,-1,-1,22,18);
            Check(c.Selection.Length==2,"forward marquee from empty workspace");
            CanvasMouse(c,0x0201,1,25,20);CanvasMouse(c,0x0200,1,55,35);
            c.Capture=false;Check(c.Selection.SequenceEqual(c.Doc.Items.Skip(1).Take(2)),"capture loss restores selection");
        }
    }
    static RectangleF TextBounds(Element item,Canvas c){
        return (RectangleF)typeof(Element).GetMethod("Bounds",BindingFlags.NonPublic|BindingFlags.Instance)
            .Invoke(item,new object[]{c.Doc.Data,c.Row});
    }
    static void WrappedTextTest(string folder){
        using(var f=new Studio()){
            f.Show();f.Activate();Application.DoEvents();Click(f,"文本");
            var c=Children(f).OfType<Canvas>().Single();var item=c.Selected;
            bool edited=false;
            using(var timer=new Timer{Interval=30}){
                timer.Tick+=(sender,args)=>{
                    var dialog=Application.OpenForms.Cast<Form>().FirstOrDefault(form=>form.Text=="编辑文字");
                    if(dialog==null)return;timer.Stop();
                    var input=Children(dialog).OfType<TextBox>().Single();
                    input.Text="原料批号";input.SelectionStart=input.Text.Length;
                    SendMessage(input.Handle,0x0102,new IntPtr(13),IntPtr.Zero);
                    input.SelectedText="30TSH(F)-147-FJ封闭后";
                    edited=input.Lines.Length==2;
                    Click(dialog,"确定");
                };
                timer.Start();
                CanvasMouse(c,0x0203,1,item.X+2,item.Y+2);CanvasMouse(c,0x0202,0,item.X+2,item.Y+2);
            }
            Check(edited && item.Text.Contains("\n"),"Enter creates a second line in the same object");
            var wide=TextBounds(item,c);
            DragCanvas(c,wide.Right,wide.Bottom,item.X+8,wide.Bottom);
            var narrow=TextBounds(item,c);
            Check(narrow.Width<wide.Width && narrow.Height>wide.Height,"narrower text wraps and grows taller: "+wide+" -> "+narrow+" selected="+(item==c.Selected));
            DragCanvas(c,narrow.Right,narrow.Bottom,item.X-2,narrow.Bottom);
            var smallest=TextBounds(item,c);
            Check(smallest.Width>2 && smallest.Width<8 && smallest.Height>narrow.Height,"one-character minimum");
            DragCanvas(c,smallest.Right,smallest.Bottom,item.X-3,smallest.Bottom);
            Check(Math.Abs(TextBounds(item,c).Width-smallest.Width)<.01,"cannot shrink past the widest character");
            DragCanvas(c,item.X+1,item.Y+1,-3,5);
            Check(item.X<0,"drag left of the paper");
            string file=Path.Combine(folder,"纸外多行.lig");c.Doc.Save(file);var loaded=LabelFile.Load(file);
            Check(loaded.Items[0].Text.Replace("\r\n","\n")==item.Text.Replace("\r\n","\n") &&
                  Math.Abs(loaded.Items[0].X-item.X)<.0001,"off-page multiline round trip");
            c.Doc.Data.Headers.Add("批号");c.Doc.Data.Rows.Add(new[]{"中"});c.Doc.Data.Rows.Add(new[]{"中\n\n文"});
            var field=new Element{Field="批号",W=30,H=1};
            c.Row=0;var one=TextBounds(field,c);c.Row=1;var three=TextBounds(field,c);
            Check(Math.Abs(three.Height-3*one.Height)<.01,"bound text preserves explicit empty lines");
            c.Doc.Data.Rows[1][0]="中文批号中文批号";field.W=one.Width;
            var unwrapped=TextBounds(field,c);field.W=.2f;
            Check(TextBounds(field,c).Height>unwrapped.Height,"bound text wraps to the current width");
            c.Row=0;Check(Math.Abs(TextBounds(field,c).Height-one.Height)<.01,"row change recomputes wrapped height");
        }
        var d=new LabelFile{Width=40,Height=25};
        d.Items.Add(new Element{Type=Kind.文本,X=-7,Y=2,W=30,H=6,Text="ABCDEFGHIJ"});
        d.Items.Add(new Element{Type=Kind.文本,X=43,Y=2,W=20,H=6,Text="OUT"});
        d.Items.Add(new Element{Type=Kind.文本,X=2,Y=15,W=30,H=6,Text="纸内文字"});
        using(var design=new Bitmap(650,330))
        using(var print=new Bitmap(650,330)){
            foreach(var image in new[]{design,print})using(var g=Graphics.FromImage(image)){
                g.Clear(Color.White);g.TranslateTransform(80,20);g.ScaleTransform(10,10);
                LigPrint.Drawing.Paint(g,d,0,false,image==design);
            }
            int left=0,right=0,inside=0;
            for(int y=0;y<330;y++)for(int x=0;x<650;x++){
                bool onPaper=x>=80 && x<480 && y>=20 && y<270;
                if(onPaper){
                    Check(print.GetPixel(x,y)==design.GetPixel(x,y),"print and edit agree inside the page");
                    if(print.GetPixel(x,y).R<100)inside++;
                }else{
                    Check(print.GetPixel(x,y).R==255,"printed ink outside paper");
                    if(design.GetPixel(x,y).R<100){if(x<80)left++;if(x>=480)right++;}
                }
            }
            Check(left>0 && right>0 && inside>0,"visible partial, fully outside and inside text");
        }
        Printing.Run(d,LabelPdf.QueueName,0,0,1,Path.Combine(folder,"text-crop.pdf"));
        if(Printing.Queues().Contains("Microsoft Print to PDF"))
            Printing.Run(d,"Microsoft Print to PDF",0,0,1,Path.Combine(folder,"text-driver-crop.pdf"));
        Console.WriteLine("PASS  Multiline input, wrap on resize, minimum text width, off-page drag, template reload and print clipping.");
    }
    static void ClipboardTest(){
        using(var f=new Studio()){
            f.Show();Application.DoEvents();Click(f,"文本");Click(f,"二维码");
            var c=Children(f).OfType<Canvas>().Single();
            c.Focus();CanvasClick(c,c.Doc.Items[0],false);CanvasClick(c,c.Doc.Items[1],true);
            Check(c.Selection.Length==2,"Ctrl+click multi-select");
            CanvasClick(c,c.Doc.Items[0],true);Check(c.Selection.Length==1,"Ctrl+click deselect");
            Key(f,Keys.Control|Keys.A);Check(c.Selection.Length==2,"select all");
            Key(f,Keys.Control|Keys.C);Key(f,Keys.Control|Keys.V);var pasted=c.Selection;
            Check(pasted.Length==2 && c.Doc.Items.Count==4 && pasted[0].X!=c.Doc.Items[0].X,"first paste");
            Key(f,Keys.Control|Keys.V);Check(c.Doc.Items.Count==6 && c.Selection[0].X!=pasted[0].X,"repeat paste");
            Key(f,Keys.Control|Keys.Z);Check(c.Doc.Items.Count==4,"undo paste");
            Key(f,Keys.Control|Keys.Y);Check(c.Doc.Items.Count==6 && c.Selection.Length==2,"redo paste restores group selection");
            c.Selected=c.Doc.Items[0];Key(f,Keys.Control|Keys.X);Check(c.Doc.Items.Count==5,"cut");
            Key(f,Keys.Control|Keys.V);Check(c.Doc.Items.Count==6 && c.Selection.Length==1,"paste cut");
            Key(f,Keys.Delete);Check(c.Doc.Items.Count==5,"delete");
            CanvasClick(c,c.Doc.Items[0],false);
            var before=c.Selected;var props=Children(f).OfType<PropertyGrid>().Single();
            GridItem property=props.SelectedGridItem;
            while(property.Parent!=null)property=property.Parent;
            var textProperty=property.GridItems.Cast<GridItem>().Single(i=>i.PropertyDescriptor!=null && i.PropertyDescriptor.Name=="Text");
            props.Focus();props.SelectedGridItem=textProperty;textProperty.Select();Application.DoEvents();
            var editor=Children(props).OfType<TextBox>().First(t=>t.Visible && !t.ReadOnly);
            editor.Focus();editor.SelectAll();Check(editor.Focused,"property text editor focus");
            Key(f,Keys.Control|Keys.C);Key(f,Keys.Control|Keys.X);Key(f,Keys.Control|Keys.V);Key(f,Keys.Delete);
            Check(c.Doc.Items.Count==5 && c.Selected==before,"property editor focus gate");
        }
    }
    [STAThread] static int Main(string[] args){
        Startup.ResolveLibraries();Application.EnableVisualStyles();
        try{HeaderSelection(args[0]);FeedbackCases(args[0]);Batch(args[0]);MarqueeTest(args[0]);WrappedTextTest(args[0]);ClipboardTest();Console.WriteLine("PASS  12-row three-state header, filtering, repeated mouse clicks, 3407 records, filtered column/row selection, copies, preview, PDF, marquee selection, group dragging and clipboard shortcuts.");return 0;}
        catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
}











