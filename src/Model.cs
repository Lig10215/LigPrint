using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Data;
using System.Drawing;
using System.ComponentModel;
using System.Globalization;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Xml;
using System.Xml.Serialization;
using ExcelDataReader;
using ExcelNumberFormat;

namespace LigPrint {
public static class Disk {
    public static string Local(string path) {
        if (string.IsNullOrWhiteSpace(path) || path.Length < 3 || path[1] != ':' || path[2] != '\\' || path.Substring(2).Contains(":"))
            throw new IOException("请选择本机磁盘上的文件。");
        path = Path.GetFullPath(path);
        var drive = new DriveInfo(Path.GetPathRoot(path));
        if (drive.DriveType != DriveType.Fixed && drive.DriveType != DriveType.Removable)
            throw new IOException("仅支持本机磁盘和移动存储。");
        var chain=new Stack<string>();
        for (string p=path;p!=null;p=Path.GetDirectoryName(p))chain.Push(p);
        foreach(string p in chain){
            try {
                if ((File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0) throw new IOException("请选择真实本地目录，不能使用链接或云占位文件。");
            } catch (FileNotFoundException) {} catch (DirectoryNotFoundException) {}
        }
        return path;
    }
}
public enum Kind { 文本, 条形码, 二维码, 矩形, 椭圆, 横线, 图片 }
public enum Turn { 正向, 顺时针90度, 旋转180度, 顺时针270度 }
public class Element {
    [DisplayName("锁定位置")] public bool Locked {get;set;}
    [DisplayName("水平镜像")] public bool Mirror {get;set;}
    [DisplayName("旋转方向")] public Turn Rotation {get;set;}
    [Browsable(false)] public bool Serial {get;set;}
    [DisplayName("序号起点")] public int Start {get;set;}
    [DisplayName("序号步长")] public int Step {get;set;}
    [DisplayName("序号位数")] public int Digits {get;set;}
    [ReadOnly(true), DisplayName("对象类型")] public Kind Type {get;set;}
    [DisplayName("固定内容"),Editor(typeof(LabelTextEditor),typeof(System.Drawing.Design.UITypeEditor))] public string Text {get;set;}
    [DisplayName("绑定字段"), ReadOnly(true)] public string Field {get;set;}
    [DisplayName("左边距 mm")] public float X {get;set;}
    [DisplayName("上边距 mm")] public float Y {get;set;}
    [DisplayName("宽度 mm")] public float W {get;set;}
    [DisplayName("高度 mm")] public float H {get;set;}
    [DisplayName("字号 pt")] public float Points {get;set;}
    [DisplayName("粗体")] public bool Bold {get;set;}
    [DisplayName("字体")] public string Typeface {get;set;}
    [Browsable(false)] public byte[] ImageBytes {get;set;}
    public Element(){Start=1;Step=1;Digits=4;W=30;H=8;Points=12;Typeface="Microsoft YaHei";Text="标签文字";}
    TextBlock cachedLayout;
    internal bool Sideways {get{return Rotation==Turn.顺时针90度 || Rotation==Turn.顺时针270度;}}
    internal TextBlock Layout(Sheet sheet,int row){
        string value=Value(sheet,row)??"";float width=Sideways?H:W;
        if(cachedLayout==null || !cachedLayout.Matches(this,value,width))cachedLayout=new TextBlock(this,value,width);
        return cachedLayout;
    }
    internal RectangleF Bounds(Sheet sheet,int row){
        if(Type!=Kind.文本)return new RectangleF(X,Y,W,H);
        try{
            var block=Layout(sheet,row);
            return Sideways?new RectangleF(X,Y,Math.Max(W,block.Height),block.Width):
                            new RectangleF(X,Y,block.Width,Math.Max(H,block.Height));
        }catch(InvalidDataException){return new RectangleF(X,Y,W,H);}
    }
    public Element Copy(){return (Element)MemberwiseClone();}
    public string Value(Sheet s,int row){
        if(Serial)return ((long)Start+(long)row*Step).ToString("D"+Digits,CultureInfo.InvariantCulture);
        if (String.IsNullOrEmpty(Field)) return Text ?? "";
        int col=s.Headers.IndexOf(Field);
        if (col<0 || row<0 || row>=s.Rows.Count) throw new InvalidDataException("字段缺失或没有记录："+Field);
        return s.Rows[row][col];
    }
}
public class Sheet {
    public string Name="";
    public string Source="";
    public string LocalPath="";
    public List<string> Headers=new List<string>();
    public List<string[]> Rows=new List<string[]>();
}
public class LabelFile {
    // Only a print snapshot has a page rotation. Element positions stay in design coordinates.
    internal Turn PrintTurn;
    internal float ArtworkWidth {get{return (int)PrintTurn%2==0?Width:Height;}}
    internal float ArtworkHeight {get{return (int)PrintTurn%2==0?Height:Width;}}
    public int Version=1;
    public float Width=60,Height=40;
    public List<Element> Items=new List<Element>();
    public Sheet Data=new Sheet();
    public void Validate(){
        if(Version!=1 || !(Width>=5 && Width<=500 && Height>=5 && Height<=500) || Items==null || Items.Count>2000 || Data==null || Data.Headers==null || Data.Rows==null || Data.Headers.Distinct().Count()!=Data.Headers.Count || Data.Rows.Any(r=>r==null || r.Length!=Data.Headers.Count))
            throw new InvalidDataException("模板格式或标签尺寸无效。");
        foreach(var e in Items){
            if(e==null || !(e.W>0 && e.W<=500 && e.H>0 && e.H<=500 && (e.Type==Kind.文本 ? !float.IsNaN(e.X) && !float.IsInfinity(e.X) && !float.IsNaN(e.Y) && !float.IsInfinity(e.Y) : e.X>=0 && e.X<ArtworkWidth && e.Y>=0 && e.Y<ArtworkHeight) && e.Points>=1 && e.Points<=300 && e.Digits>=1 && e.Digits<=18))
                throw new InvalidDataException("请检查对象坐标、尺寸和字号。");
        }
    }
    [XmlIgnore] public int RecordLimit {get{return Data.Rows.Count>0?Data.Rows.Count:Items.Any(e=>e.Serial)?100000:1;}}
    static readonly XmlSerializer Serializer=new XmlSerializer(typeof(LabelFile));
    public void Save(string path){
        Validate(); path=Disk.Local(path);
        string tmp=Path.Combine(Path.GetDirectoryName(path), Guid.NewGuid()+".tmp");
        try {
            using(var f=new FileStream(tmp,FileMode.CreateNew,FileAccess.Write)){Serializer.Serialize(f,this);f.Flush(true);}
            if(new FileInfo(tmp).Length>128L*1024*1024) throw new IOException("模板超过 128 MB。");
            if(File.Exists(path))File.Replace(tmp,path,null);else File.Move(tmp,path);
        } finally {if(File.Exists(tmp))File.Delete(tmp);}
    }
    public static LabelFile Load(string path) {
        path=Disk.Local(path);
        if(new FileInfo(path).Length>128L*1024*1024)throw new IOException("模板超过 128 MB。");
        using(var f=File.OpenRead(path))
        using(var x=XmlReader.Create(f,new XmlReaderSettings{XmlResolver=null,DtdProcessing=DtdProcessing.Prohibit,MaxCharactersInDocument=134217728})){
            var doc=(LabelFile)Serializer.Deserialize(x);doc.Validate();return doc;
        }
    }
}
public static class ExcelBook {
    public static List<Sheet> Read(string path){
        path=Disk.Local(path);
        var sheets=new List<Sheet>();
        using(var f=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite))
        using(var r=ExcelReaderFactory.CreateReader(f)){
            long cells=0;
            do {
                var s=new Sheet{Name=r.Name,Source=Path.GetFileName(path),LocalPath=path};
                while(r.Read()){
                    if(r.FieldCount>2048 || (cells+=r.FieldCount)>4000000)throw new InvalidDataException("工作簿过大，请按工作表拆分后导入。");
                    var values=new string[r.FieldCount];
                    for(int i=0;i<values.Length;i++){
                        object v=r.GetValue(i);string fmt=r.GetNumberFormatString(i);
                        values[i]=v==null?"":v is string?(string)v:!String.IsNullOrEmpty(fmt)?new NumberFormat(fmt).Format(v,CultureInfo.CurrentCulture):Convert.ToString(v,CultureInfo.CurrentCulture);
                    }
                    if(values.All(String.IsNullOrWhiteSpace))continue;
                    if(s.Headers.Count==0){
                        foreach(string raw in values){
                            string baseName=raw.Trim();if(baseName=="")baseName="列"+(s.Headers.Count+1);
                            string name=baseName;int suffix=2;
                            while(s.Headers.Contains(name))name=baseName+"_"+suffix++;
                            s.Headers.Add(name);
                        }
                    } else s.Rows.Add(values);
                }
                if(s.Rows.Count>0)sheets.Add(s);
            }while(r.NextResult());
        }
        if(sheets.Count==0)throw new InvalidDataException("没有找到包含表头和数据行的工作表。");
        return sheets;
    }
}
public static class Audit {
    public static void Write(string action,int count){
        var dir=Disk.Local(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LigPrint","Logs"));
        Directory.CreateDirectory(dir);
        string path=Disk.Local(Path.Combine(dir, DateTime.UtcNow.ToString("yyyy-MM-dd")+"-"+System.Diagnostics.Process.GetCurrentProcess().Id+".lplog"));
        var bytes=Encoding.UTF8.GetBytes(DateTime.UtcNow.ToString("o")+"\t"+action+"\t"+count);
        using(var key=new RSACryptoServiceProvider(new CspParameters{Flags=CspProviderFlags.CreateEphemeralKey}))
        using(var reader=new StreamReader(typeof(Audit).Assembly.GetManifestResourceStream("LigPrint.PublicKey"))){
            key.FromXmlString(reader.ReadToEnd());
            File.AppendAllText(path,Convert.ToBase64String(key.Encrypt(bytes,true))+Environment.NewLine,Encoding.ASCII);
        }
    }
}
}





