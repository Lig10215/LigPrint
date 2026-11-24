using System;
using System.IO;
using System.Drawing;
using System.Drawing.Printing;
using System.Collections.Generic;



namespace LigPrint {
public sealed class PrintRow {
    public readonly int Index, Copies;
    public PrintRow(int index,int copies){Index=index;Copies=copies;}
}
public static class Printing {
    [System.Runtime.InteropServices.DllImport("gdi32.dll")] static extern int GetDeviceCaps(IntPtr hdc,int index);
    public static List<string> Queues(){ return PrinterQueues.Installed(); }
    public static void Run(LabelFile doc,string queue,int first,int last,int copies,string output){
        Submit(doc,queue==LabelPdf.QueueName ? null : queue,first,last,copies,output,(a,b)=>{});
    }
    public static void Submit(LabelFile doc,string queue,int first,int last,int copies,string output,Action<int,int> progress){
        if(first<0 || last<first || last>=doc.RecordLimit || copies<1 || copies>999)throw new ArgumentOutOfRangeException("记录范围或份数");
        var rows=new List<PrintRow>();
        for(int i=first;i<=last;i++) rows.Add(new PrintRow(i,copies));
        SubmitRows(doc,queue,rows,output,progress);
    }
    public static void SubmitRows(LabelFile doc,string queue,List<PrintRow> rows,string output,Action<int,int> progress){
        if(rows==null || rows.Count==0)throw new InvalidOperationException("请选择打印记录。");
        int total=0;
        foreach(var entry in rows){
            if(entry.Index<0 || entry.Index>=doc.RecordLimit || entry.Copies<1 || entry.Copies>999)
                throw new ArgumentOutOfRangeException("记录或份数");
            total=checked(total+entry.Copies);
        }
        if(doc.Items.Count==0)throw new InvalidDataException("请先添加打印对象。");
        doc.Validate();

        foreach(var entry in rows){int row=entry.Index;progress(0,total);
            foreach(var e in doc.Items){
                if(e.Type==Kind.条形码 || e.Type==Kind.二维码){
                    try{Drawing.Code(e,e.Value(doc.Data,row));}catch(Exception ex){throw new InvalidDataException("第 "+(row+1)+" 条记录无法生成"+e.Type+"。"+ex.Message,ex);}
                }else if(e.Type==Kind.文本)e.Value(doc.Data,row);
            }
        }
        if(queue==null){LabelPdf.Save(doc,rows,total,output,progress);return;}
        string staging=null;
        if(output!=null){
            output=Disk.Local(output);
            staging=Path.Combine(Path.GetDirectoryName(output),Guid.NewGuid()+Path.GetExtension(output));
        }
        try {
            using(var print=new PrintDocument()){
                print.PrinterSettings.PrinterName=queue;
                if(!print.PrinterSettings.IsValid)throw new IOException("Windows 无法打开打印队列「"+queue+"」。请检查驱动和连接。");
                if(output!=null){
                    print.PrinterSettings.PrintToFile=true;
                    print.PrinterSettings.PrintFileName=staging;
                }

                print.DocumentName="LigPrint";print.PrintController=new StandardPrintController();
                print.OriginAtMargins=false;var ps=print.DefaultPageSettings;
                ps.Margins=new Margins(0,0,0,0);ps.Landscape=false;
                int width=(int)Math.Round(doc.Width*100/25.4), height=(int)Math.Round(doc.Height*100/25.4);
                var paper=new PaperSize("LigPrint",width,height);
                foreach(PaperSize supported in print.PrinterSettings.PaperSizes){
                    if(Math.Abs(supported.Width-width)<=1 && Math.Abs(supported.Height-height)<=1){
                        paper=supported;
                        break;
                    }
                }
                ps.PaperSize=paper;
                int record=0,copy=0,printed=0;
                print.PrintPage+=(s,e)=>{
                    try{progress(printed,total);}catch(OperationCanceledException){e.Cancel=true;throw;}
                    var g=e.Graphics;
                    IntPtr dc=g.GetHdc();
                    try {
                        double actualW=25.4*GetDeviceCaps(dc,110)/GetDeviceCaps(dc,88);
                        double actualH=25.4*GetDeviceCaps(dc,111)/GetDeviceCaps(dc,90);
                        if(actualW+1.5<doc.Width || actualH+1.5<doc.Height){
                            e.Cancel=true;
                            throw new IOException("驱动返回的纸张小于标签，请在该打印机的首选项中设置正确尺寸。");
                        }
                    }finally{g.ReleaseHdc(dc);}
                    g.PageUnit=GraphicsUnit.Pixel;
                    g.TranslateTransform(-e.PageSettings.HardMarginX*g.DpiX/100,-e.PageSettings.HardMarginY*g.DpiY/100);
                    g.ScaleTransform(g.DpiX/25.4f,g.DpiY/25.4f);Drawing.Paint(g,doc,rows[record].Index,false);
                    printed++;if(++copy>=rows[record].Copies){copy=0;record++;}e.HasMorePages=record<rows.Count;
                };
                print.Print();
            }
            if(staging!=null){
                if(!File.Exists(staging) || new FileInfo(staging).Length==0)throw new IOException("驱动没有生成输出文件。");
                if(File.Exists(output))File.Replace(staging,output,null);else File.Move(staging,output);
            }
        }finally{if(staging!=null && File.Exists(staging))File.Delete(staging);}
    }
}
}





