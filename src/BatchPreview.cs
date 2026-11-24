using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace LigPrint {
public sealed class BatchPreview : Form {
    readonly LabelFile original;
    public LabelFile Output {get;private set;}
    public List<PrintRow> Records {get;private set;}
    public string Target {get{return printers.SelectedIndex==printers.Items.Count-1?null:printers.Text;}}
    int count;
    readonly CheckBox selectAll=new CheckBox{
        Text="全选",AccessibleName="全选所有记录",AutoCheck=false,ThreeState=true,
        TextAlign=ContentAlignment.MiddleLeft,BackColor=Color.FromArgb(234,240,246)
    };
    readonly bool[] chosen, activeFields;
    readonly int[] overrideCopies, fieldCopies;
    readonly List<int> visibleRows=new List<int>();
    readonly List<int> listedFields=new List<int>();
    readonly ComboBox printers=Choice(), mode=Choice(), orientation=Choice(), quantityField=Choice(), searchField=Choice();
    readonly NumericUpDown first, last, defaultCopies;
    readonly TextBox findColumn=new TextBox(), keyword=new TextBox();
    readonly CheckedListBox columnList=new CheckedListBox{CheckOnClick=true,IntegralHeight=false};
    readonly DataGridView grid=new DataGridView();
    readonly Label summary=new Label(), previewInfo=new Label();
    readonly LabelThumbnail thumbnail=new LabelThumbnail();
    readonly Button submit=new Button(), allRows=new Button(), invertRows=new Button(), clearRows=new Button();
    bool syncing;
    int current=-1;
    public BatchPreview(LabelFile doc,string title,List<string> queues,string preferred,string notice,int initialRow){
        original=doc;count=Math.Max(1,doc.Data.Rows.Count);
        int capacity=doc.RecordLimit;
        chosen=Enumerable.Repeat(true,capacity).ToArray();overrideCopies=new int[capacity];fieldCopies=new int[capacity];
        activeFields=Enumerable.Repeat(true,doc.Data.Headers.Count).ToArray();
        first=Number(capacity,1);last=Number(capacity,count);defaultCopies=Number(999,1);
        Text="批量打印预览";Font=new Font("Microsoft YaHei UI",9);BackColor=Color.White;
        StartPosition=FormStartPosition.CenterParent;MinimumSize=new Size(850,555);
        var area=Screen.FromPoint(Cursor.Position).WorkingArea;
        Size=new Size(Math.Min(1250,area.Width),Math.Min(790,area.Height));
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3,RowCount=1,Padding=new Padding(7)};
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,223));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,36));
        Controls.Add(layout);

        var left=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true};
        layout.Controls.Add(left,0,0);left.Controls.Add(Heading("打印设置"));
        printers.Items.AddRange(queues.Cast<object>().ToArray());printers.Items.Add(LabelPdf.QueueName);
        printers.SelectedIndex=PrinterQueues.PreferredIndex(queues,preferred);AddInput(left,"打印机",printers);
        if(notice!=null)left.Controls.Add(new Label{Text=notice,Width=190,AutoSize=false,Height=48,ForeColor=Color.Firebrick});
        orientation.Items.AddRange(new[]{"0°","90°","180°","270°"});orientation.SelectedIndex=0;AddInput(left,"打印方向",orientation);
        mode.Items.AddRange(new[]{"按顺序打印","自定义打印"});mode.SelectedIndex=0;AddInput(left,"打印方式",mode);
        var range=Bar(32);range.Width=192;first.Width=76;last.Width=76;
        range.Controls.Add(first);range.Controls.Add(new Label{Text="至",AutoSize=true,Padding=new Padding(0,5,0,0)});range.Controls.Add(last);
        AddInput(left,"顺序记录范围",range);
        AddInput(left,"默认份数",defaultCopies);
        quantityField.Items.Add("使用默认份数");
        quantityField.Items.AddRange(doc.Data.Headers.Cast<object>().ToArray());quantityField.SelectedIndex=0;
        AddInput(left,"从 Excel 列读取份数",quantityField);
        if(activeFields.Length>0){
            AddInput(left,"参与本次绑定的列 · 搜索列名",findColumn);
            var columnActions=Bar(33);columnActions.Width=196;
            AddButton(columnActions,"全选列",()=>ChangeColumns(1));AddButton(columnActions,"反选列",()=>ChangeColumns(0));
            left.Controls.Add(columnActions);columnList.Width=190;columnList.Height=145;left.Controls.Add(columnList);
        }

        var middle=new Panel{Dock=DockStyle.Fill,Padding=new Padding(7,0,7,0)};layout.Controls.Add(middle,1,0);
        var head=Heading("数据库列表");head.Dock=DockStyle.Top;
        var search=new TableLayoutPanel{Dock=DockStyle.Top,Height=36,ColumnCount=3,RowCount=1};
        search.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,40));search.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,60));search.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,65));
        searchField.Dock=DockStyle.Fill;keyword.Dock=DockStyle.Fill;
        var find=new Button{Text="搜索",Dock=DockStyle.Fill};find.Click+=(s,e)=>RefreshRows(current);
        search.Controls.Add(searchField,0,0);search.Controls.Add(keyword,1,0);search.Controls.Add(find,2,0);
        keyword.KeyDown+=(s,e)=>{if(e.KeyCode==Keys.Enter){e.SuppressKeyPress=true;RefreshRows(current);}};
        var rowActions=Bar(36);rowActions.Dock=DockStyle.Top;
        AddButton(rowActions,allRows,"全选结果",()=>ChangeRows(1));AddButton(rowActions,invertRows,"反选结果",()=>ChangeRows(0));AddButton(rowActions,clearRows,"清除结果",()=>ChangeRows(-1));
        summary.Dock=DockStyle.Bottom;summary.Height=47;summary.TextAlign=ContentAlignment.MiddleLeft;
        ConfigureGrid();
        middle.Controls.Add(grid);middle.Controls.Add(summary);middle.Controls.Add(rowActions);middle.Controls.Add(search);middle.Controls.Add(head);

        var right=new Panel{Dock=DockStyle.Fill,Padding=new Padding(8),BackColor=Color.FromArgb(238,242,247)};
        layout.Controls.Add(right,2,0);
        var info=Heading("标签信息");info.Dock=DockStyle.Top;
        var label=new Label{Text=title+"\n"+doc.Width+" × "+doc.Height+" mm",Dock=DockStyle.Top,Height=56,AutoEllipsis=true};
        var previewHeading=Heading("标签预览");previewHeading.Dock=DockStyle.Top;
        previewInfo.Dock=DockStyle.Bottom;previewInfo.Height=36;previewInfo.TextAlign=ContentAlignment.MiddleCenter;
        thumbnail.Dock=DockStyle.Fill;right.Controls.Add(thumbnail);right.Controls.Add(previewInfo);
        right.Controls.Add(previewHeading);right.Controls.Add(label);right.Controls.Add(info);
        submit.Dock=DockStyle.Bottom;submit.Height=42;submit.BackColor=Color.FromArgb(26,107,182);submit.ForeColor=Color.White;submit.FlatStyle=FlatStyle.Flat;
        submit.Click+=(s,e)=>{if(!grid.EndEdit())return;Records=SelectedRecords();if(Records.Count>0 && submit.Enabled)DialogResult=DialogResult.OK;};
        right.Controls.Add(submit);
        var cancel=new Button{Text="取消",Dock=DockStyle.Bottom,Height=30,DialogResult=DialogResult.Cancel,CausesValidation=false};right.Controls.Add(cancel);CancelButton=cancel;

        findColumn.TextChanged+=(s,e)=>ListColumns();
        columnList.ItemCheck+=(s,e)=>{
            if(syncing)return;
            activeFields[listedFields[e.Index]]=e.NewValue==CheckState.Checked;
            UpdateFields();
        };
        orientation.SelectedIndexChanged+=(s,e)=>RefreshOutput();
        mode.SelectedIndexChanged+=(s,e)=>RefreshMode();first.ValueChanged+=(s,e)=>UpdateCounts();
        last.ValueChanged+=(s,e)=>{
            // A sequence without Excel has only the records requested by its end number.
            if(last.Value>count){count=(int)last.Value;RefreshRows(current);}
            else UpdateCounts();
        };defaultCopies.ValueChanged+=(s,e)=>UpdateCounts();
        quantityField.SelectedIndexChanged+=(s,e)=>ReadQuantities();
        searchField.SelectedIndexChanged+=(s,e)=>{if(!syncing)RefreshRows(current);};
        ListColumns();UpdateFields();RefreshMode();RefreshRows(initialRow);
    }
    static ComboBox Choice(){return new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList};}
    static NumericUpDown Number(int max,int value){return new NumericUpDown{Minimum=1,Maximum=max,Value=value};}
    static Label Heading(string text){return new Label{Text=text,Height=38,Width=184,Font=new Font("Microsoft YaHei UI",11,FontStyle.Bold),TextAlign=ContentAlignment.MiddleLeft};}
    static FlowLayoutPanel Bar(int height){return new FlowLayoutPanel{Height=height,WrapContents=false,Margin=Padding.Empty};}
    static void AddInput(Control parent,string label,Control input){
        parent.Controls.Add(new Label{Text=label,Width=192,Height=22,TextAlign=ContentAlignment.BottomLeft});input.Width=190;parent.Controls.Add(input);
    }
    static void AddButton(Control panel,string text,Action action){AddButton(panel,new Button(),text,action);}
    static void AddButton(Control panel,Button button,string text,Action action){
        button.Text=text;button.AutoSize=true;button.AutoSizeMode=AutoSizeMode.GrowAndShrink;button.Height=27;
        button.Click+=(s,e)=>action();panel.Controls.Add(button);
    }
    void ConfigureGrid(){
        grid.Dock=DockStyle.Fill;grid.VirtualMode=true;grid.AllowUserToAddRows=false;grid.AllowUserToDeleteRows=false;
        grid.AllowUserToResizeRows=false;grid.RowHeadersVisible=false;grid.MultiSelect=false;
        grid.SelectionMode=DataGridViewSelectionMode.FullRowSelect;grid.BackgroundColor=Color.White;
        grid.BorderStyle=BorderStyle.None;grid.GridColor=Color.FromArgb(216,223,232);
        grid.EnableHeadersVisualStyles=false;grid.ColumnHeadersHeight=34;grid.RowTemplate.Height=27;
        grid.ColumnHeadersDefaultCellStyle.BackColor=Color.FromArgb(234,240,246);
        grid.DefaultCellStyle.SelectionBackColor=Color.FromArgb(35,104,164);
        grid.AlternatingRowsDefaultCellStyle.BackColor=Color.FromArgb(247,249,252);
        grid.Columns.Add(new DataGridViewCheckBoxColumn{Name="pick",HeaderText="",Width=65,MinimumWidth=60,Frozen=true,ReadOnly=true});
        grid.Columns.Add(new DataGridViewTextBoxColumn{Name="number",HeaderText="记录",Width=53,ReadOnly=true,Frozen=true});
        grid.Columns.Add(new DataGridViewTextBoxColumn{Name="copies",HeaderText="份数",Width=48,Frozen=true});
        foreach(string h in original.Data.Headers)grid.Columns.Add(new DataGridViewTextBoxColumn{HeaderText=h,ReadOnly=true,MinimumWidth=108,FillWeight=1f,AutoSizeMode=DataGridViewAutoSizeColumnMode.Fill});
        foreach(DataGridViewColumn c in grid.Columns)c.SortMode=DataGridViewColumnSortMode.NotSortable;
        grid.Controls.Add(selectAll);
        Shown+=(s,e)=>PositionSelectAll();
        grid.SizeChanged+=(s,e)=>PositionSelectAll();
        grid.ColumnWidthChanged+=(s,e)=>PositionSelectAll();
        grid.ColumnHeadersHeightChanged+=(s,e)=>PositionSelectAll();
        selectAll.Click+=(s,e)=>ChangeRows(selectAll.CheckState==CheckState.Checked?-1:1,true);
        grid.CellValueNeeded+=(s,e)=>{
            if(e.RowIndex<0 || e.RowIndex>=visibleRows.Count)return;
            int r=visibleRows[e.RowIndex];
            switch(e.ColumnIndex){
                case 0:e.Value=IsSelected(r);break;case 1:e.Value=r+1;break;
                case 2:e.Value=Copies(r)>0?Copies(r).ToString():"无效";break;
                default:e.Value=original.Data.Rows[r][e.ColumnIndex-3];break;
            }
        };
        // The virtual checkbox mirrors the job selection. One mouse event owns its toggle,
        // including clicks on the padding around the glyph.
        grid.CellMouseClick+=(s,e)=>{
            if(e.Button==MouseButtons.Left && e.ColumnIndex==0 && e.RowIndex>=0 &&
                grid.CurrentCell!=null && grid.CurrentCell.RowIndex==e.RowIndex)ToggleRecord(e.RowIndex);
        };
        grid.KeyDown+=(s,e)=>{
            if(e.KeyCode==Keys.Space && e.Modifiers==Keys.None && grid.CurrentCell!=null && grid.CurrentCell.ColumnIndex==0){
                e.SuppressKeyPress=true;ToggleRecord(grid.CurrentCell.RowIndex);
            }
        };
        grid.CellValidating+=(s,e)=>{
            if(e.ColumnIndex!=2 || !grid.IsCurrentCellInEditMode)return;
            int value;
            if(!Int32.TryParse(Convert.ToString(e.FormattedValue),out value)||value<1||value>999){
                e.Cancel=true;summary.Text="份数须为 1 至 999 的整数。按 Esc 可取消修改。";
            }
        };
        grid.CellValuePushed+=(s,e)=>{
            if(e.RowIndex<0||e.RowIndex>=visibleRows.Count)return;
            int r=visibleRows[e.RowIndex];

            if(e.ColumnIndex==2){int value;if(Int32.TryParse(Convert.ToString(e.Value),out value) && value>=1 && value<=999)overrideCopies[r]=value;}
            UpdateCounts();
        };
        grid.CurrentCellChanged+=(s,e)=>{
            if(syncing)return;
            int i=grid.CurrentCell==null?-1:grid.CurrentCell.RowIndex;
            current=i>=0 && i<visibleRows.Count?visibleRows[i]:-1;RefreshPreview();
        };
    }
    void PositionSelectAll(){
        var cell=grid.GetCellDisplayRectangle(0,-1,true);
        if(cell.Width<=4 || cell.Height<=4)return;
        var bounds=Rectangle.Inflate(cell,-2,-2);
        if(selectAll.Bounds!=bounds)selectAll.Bounds=bounds;
    }
    bool Custom {get{return mode.SelectedIndex==1;}}
    bool IsSelected(int row){return Custom?chosen[row]:row+1>=first.Value && row+1<=last.Value;}
    int Copies(int row){return overrideCopies[row]>0?overrideCopies[row]:quantityField.SelectedIndex>0?fieldCopies[row]:(int)defaultCopies.Value;}
    void ReadQuantities(){
        Array.Clear(overrideCopies,0,count);
        int col=quantityField.SelectedIndex-1;
        if(col>=0)for(int r=0;r<count;r++){
            int q;fieldCopies[r]=Int32.TryParse(original.Data.Rows[r][col],out q) && q>=1 && q<=999?q:0;
        }
        UpdateCounts();
    }
    void RefreshMode(){
        first.Enabled=last.Enabled=!Custom;
        allRows.Enabled=invertRows.Enabled=clearRows.Enabled=Custom;
        UpdateCounts();
    }
    void ToggleRecord(int visibleIndex){
        if(visibleIndex<0 || visibleIndex>=visibleRows.Count || !grid.EndEdit())return;
        int row=visibleRows[visibleIndex];
        if(!Custom){
            int start=(int)first.Value-1,end=(int)last.Value-1;
            for(int i=0;i<count;i++)chosen[i]=i>=start && i<=end;
            mode.SelectedIndex=1;
        }
        chosen[row]=!chosen[row];
        current=row;
        RefreshPreview();
        UpdateCounts();
    }
    void ChangeRows(int op,bool entireTable=false){
        if(!grid.EndEdit())return;
        IEnumerable<int> scope=entireTable?Enumerable.Range(0,count):visibleRows;
        foreach(int r in scope)chosen[r]=op==1 || (op==0 && !chosen[r]);
        if(!Custom)mode.SelectedIndex=1;  // RefreshMode updates the header and counts.
        else UpdateCounts();
    }
    void ListColumns(){
        syncing=true;columnList.BeginUpdate();columnList.Items.Clear();listedFields.Clear();
        for(int i=0;i<activeFields.Length;i++){
            string h=original.Data.Headers[i];if(!Matches(h,findColumn.Text))continue;
            listedFields.Add(i);columnList.Items.Add(h,activeFields[i]);
        }
        columnList.EndUpdate();syncing=false;
    }
    static bool Matches(string text,string term){return (text??"").IndexOf(term,StringComparison.CurrentCultureIgnoreCase)>=0;}
    void ChangeColumns(int op){
        foreach(int i in listedFields)activeFields[i]=op==1||!activeFields[i];ListColumns();UpdateFields();
    }
    void UpdateFields(){
        syncing=true;string prev=searchField.SelectedIndex>0?searchField.Text:null;
        searchField.Items.Clear();searchField.Items.Add("全部已选列");
        for(int i=0;i<activeFields.Length;i++){
            grid.Columns[i+3].Visible=activeFields[i];if(activeFields[i])searchField.Items.Add(original.Data.Headers[i]);
        }
        searchField.SelectedIndex=prev==null?0:Math.Max(0,searchField.Items.IndexOf(prev));syncing=false;
        RefreshOutput();RefreshRows(current);
    }
    void RefreshRows(int preferred){
        if(!grid.EndEdit())return;
        syncing=true;grid.RowCount=0;visibleRows.Clear();
        string term=keyword.Text.Trim();int col=searchField.SelectedIndex>0?original.Data.Headers.IndexOf(searchField.Text):-1;
        for(int r=0;r<count;r++){
            bool match=term.Length==0;
            if(!match && original.Data.Rows.Count>0){
                var values=original.Data.Rows[r];
                if(col>=0)match=Matches(values[col],term);
                else for(int c=0;c<activeFields.Length && !match;c++)match=activeFields[c] && Matches(values[c],term);
            }
            if(match)visibleRows.Add(r);
        }
        grid.RowCount=visibleRows.Count;
        int index=Math.Max(0,visibleRows.IndexOf(preferred));
        current=visibleRows.Count==0?-1:visibleRows[index];
        if(current>=0)grid.CurrentCell=grid.Rows[index].Cells[1];
        syncing=false;RefreshPreview();UpdateCounts();
    }
    void RefreshOutput(){
        var allowed=new HashSet<string>(original.Data.Headers.Where((h,i)=>activeFields[i]));
        int turn=orientation.SelectedIndex;
        var adjusted=new LabelFile{Data=original.Data,Width=turn%2==0?original.Width:original.Height,Height=turn%2==0?original.Height:original.Width};
        foreach(var e in original.Items){
            if(!e.Serial && !String.IsNullOrEmpty(e.Field) && !allowed.Contains(e.Field))continue;
            adjusted.Items.Add(e.Copy());
        }
        adjusted.PrintTurn=(Turn)turn;Output=adjusted;thumbnail.Doc=Output;RefreshPreview();UpdateCounts();
    }
    void RefreshPreview(){
        if(selectAll.CheckState==CheckState.Unchecked){
            thumbnail.Row=-1;thumbnail.Invalidate();previewInfo.Text="未选择打印记录";return;
        }
        thumbnail.Row=current;thumbnail.Invalidate();
        previewInfo.Text=current>=0?"记录 "+(current+1)+"   ·   "+Output.Width+" × "+Output.Height+" mm":"无匹配记录";
    }
    List<PrintRow> SelectedRecords(){
        var records=new List<PrintRow>();
        for(int r=0;r<count;r++)if(IsSelected(r))records.Add(new PrintRow(r,Copies(r)));
        return records;
    }
    void UpdateCounts(){
        if(Output==null)return;
        int selected=0,invalid=0;long total=0;
        for(int r=0;r<count;r++)if(IsSelected(r)){
            selected++;int copies=Copies(r);if(copies<1)invalid++;total+=copies;
        }
        selectAll.CheckState=selected==0?CheckState.Unchecked:
            selected==count?CheckState.Checked:CheckState.Indeterminate;
        RefreshPreview();
        summary.Text=String.Format("选定记录：{0} / {1}　 当前列表：{2}　 共 {3} 张",selected,count,visibleRows.Count,total);
        if(visibleRows.Count<count){int outside=selected-visibleRows.Count(r=>IsSelected(r));if(outside>0)summary.Text+="   筛选外已选 "+outside+" 条";}
        if(invalid>0)summary.Text+="\n其中 "+invalid+" 条份数无效，请修改或取消勾选。";
        if(Output.Items.Count==0)summary.Text+="\n请勾选至少一个绑定对象所在列。";
        submit.Text="打印（"+total+" 张）";submit.Enabled=selected>0 && invalid==0 && total<=Int32.MaxValue && Output.Items.Count>0;
        grid.Invalidate();
    }
}
sealed class LabelThumbnail : Control {
    public LabelFile Doc;public int Row=-1;
    public LabelThumbnail(){DoubleBuffered=true;ResizeRedraw=true;}
    protected override void OnPaint(PaintEventArgs e){
        base.OnPaint(e);if(Doc==null || Row<0)return;
        float scale=Math.Min((Width-12)/Doc.Width,(Height-16)/Doc.Height);
        if(scale<=0)return;float w=Doc.Width*scale,h=Doc.Height*scale;
        var g=e.Graphics;g.TranslateTransform((Width-w)/2,(Height-h)/2);g.FillRectangle(Brushes.White,0,0,w,h);
        g.ScaleTransform(scale,scale);Drawing.Paint(g,Doc,Row,true);
    }
}
}






