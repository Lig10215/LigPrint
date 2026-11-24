using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Windows.Forms;
using System.Collections.Generic;

namespace LigPrint {
public class Prompt:Form {
    protected TableLayoutPanel table=new TableLayoutPanel();
    protected FlowLayoutPanel footer=new FlowLayoutPanel();
    public Button OK=new Button{Text="确定",AutoSize=true};
    public Prompt(string title,int width,int height){
        Text=title;Font=new Font("Microsoft YaHei UI",9);StartPosition=FormStartPosition.CenterParent;
        ClientSize=new Size(width,height);MinimizeBox=false;MaximizeBox=false;FormBorderStyle=FormBorderStyle.FixedDialog;
        table.Dock=DockStyle.Fill;table.Padding=new Padding(14);table.ColumnCount=2;
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,104));table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        footer.FlowDirection=FlowDirection.RightToLeft;footer.Dock=DockStyle.Bottom;footer.Height=48;footer.Padding=new Padding(0,5,12,0);
        var cancel=new Button{Text="取消",DialogResult=DialogResult.Cancel,AutoSize=true};
        footer.Controls.Add(OK);footer.Controls.Add(cancel);Controls.Add(table);Controls.Add(footer);
        OK.DialogResult=DialogResult.OK;AcceptButton=OK;CancelButton=cancel;
    }
    public void Add(string title,Control c){
        int row=table.RowCount++;table.RowStyles.Add(new RowStyle(SizeType.Absolute,36));
        table.Controls.Add(new Label{Text=title,AutoSize=true,Anchor=AnchorStyles.Left},0,row);
        c.Anchor=AnchorStyles.Left|AnchorStyles.Right;table.Controls.Add(c,1,row);
    }
    public NumericUpDown Number(string title,decimal value,decimal min,decimal max,int decimals){
        var n=new NumericUpDown{Minimum=min,Maximum=max,DecimalPlaces=decimals,Value=value};Add(title,n);return n;
    }
    public ComboBox Choice(string title,IEnumerable<string> values){
        var c=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList};c.Items.AddRange(values.Cast<object>().ToArray());if(c.Items.Count>0)c.SelectedIndex=0;Add(title,c);return c;
    }
}
public class LocalPicker:Form {
    static string last;
    readonly string[] extensions;
    readonly bool save;
    string folder;
    readonly ListBox files=new ListBox{Dock=DockStyle.Fill,IntegralHeight=false};
    readonly TextBox path=new TextBox{Dock=DockStyle.Fill};
    readonly TextBox name=new TextBox{Dock=DockStyle.Fill};
    class Entry{public string Name;public bool Dir;public override string ToString(){return Dir?"▸   "+Name:Name;}}
    LocalPicker(string title,string[] exts,bool writing,string proposed){
        Text=title;extensions=exts;save=writing;StartPosition=FormStartPosition.CenterParent;Font=new Font("Microsoft YaHei UI",9);
        ClientSize=new Size(600,395);MinimumSize=new Size(440,300);
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3,RowCount=3,Padding=new Padding(12)};
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,80));layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,90));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,36));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,36));
        var drives=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Dock=DockStyle.Fill};
        foreach(var d in DriveInfo.GetDrives())if(d.DriveType==DriveType.Fixed || d.DriveType==DriveType.Removable)drives.Items.Add(d.Name);
        drives.SelectedIndexChanged+=(s,e)=>Attempt(()=>Navigate(drives.Text));
        var up=new Button{Text="上一级",Dock=DockStyle.Fill};up.Click+=(s,e)=>Attempt(()=>Navigate(Path.GetDirectoryName(folder)??folder));
        path.KeyDown+=(s,e)=>{if(e.KeyCode==Keys.Enter){e.SuppressKeyPress=true;Attempt(()=>Navigate(path.Text));}};
        layout.Controls.Add(drives,0,0);layout.Controls.Add(path,1,0);layout.Controls.Add(up,2,0);
        layout.Controls.Add(files,0,1);layout.SetColumnSpan(files,3);
        files.SelectedIndexChanged+=(s,e)=>{var item=files.SelectedItem as Entry;if(item!=null && !item.Dir)name.Text=item.Name;};
        files.DoubleClick+=(s,e)=>Attempt(()=>{var item=files.SelectedItem as Entry;if(item==null)return;if(item.Dir)Navigate(Path.Combine(folder,item.Name));else Finish();});
        layout.Controls.Add(new Label{Text="文件名",AutoSize=true,Anchor=AnchorStyles.Left},0,2);layout.Controls.Add(name,1,2);
        var ok=new Button{Text=save?"保存":"打开",Dock=DockStyle.Fill};layout.Controls.Add(ok,2,2);ok.Click+=(s,e)=>Attempt(Finish);
        Controls.Add(layout);AcceptButton=ok;
        name.Text=proposed??"";string start=last??AppDomain.CurrentDomain.BaseDirectory;
        try{Navigate(start);}catch{Navigate(Path.GetPathRoot(Environment.SystemDirectory));}
    }
    void Attempt(Action f){try{f();}catch(Exception ex){MessageBox.Show(this,ex.Message,Text);}}
    void Navigate(string target){
        target=Disk.Local(target);var dir=new DirectoryInfo(target);
        var entries=new List<Entry>();
        foreach(var d in dir.EnumerateDirectories())if((d.Attributes&(FileAttributes.Hidden|FileAttributes.ReparsePoint|FileAttributes.System))==0)entries.Add(new Entry{Name=d.Name,Dir=true});
        foreach(var f in dir.EnumerateFiles())if((f.Attributes&(FileAttributes.Hidden|FileAttributes.ReparsePoint|FileAttributes.System))==0 && extensions.Contains(f.Extension,StringComparer.OrdinalIgnoreCase))entries.Add(new Entry{Name=f.Name});
        files.BeginUpdate();files.Items.Clear();files.Items.AddRange(entries.OrderByDescending(i=>i.Dir).ThenBy(i=>i.Name).Cast<object>().ToArray());files.EndUpdate();
        folder=target;path.Text=target;
    }
    string result;
    void Finish(){
        string input=name.Text.Trim();if(input=="")return;
        var target=Disk.Local(Path.Combine(folder,input));
        if(save && Path.GetExtension(target)=="")target+=extensions[0];
        if(!extensions.Contains(Path.GetExtension(target),StringComparer.OrdinalIgnoreCase))throw new IOException("请选择 "+String.Join(" / ",extensions)+" 文件。");
        if(!save && !File.Exists(target))throw new FileNotFoundException("文件不存在。");
        if(save && File.Exists(target) && MessageBox.Show(this,"替换现有文件？",Text,MessageBoxButtons.YesNo)!=DialogResult.Yes)return;
        last=Path.GetDirectoryName(target);result=target;DialogResult=DialogResult.OK;
    }
    public static string Pick(IWin32Window owner,string title,string[] exts,bool save=false,string proposed=null){
        using(var p=new LocalPicker(title,exts,save,proposed))return p.ShowDialog(owner)==DialogResult.OK?p.result:null;
    }
}
}
