using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Collections.Generic;

namespace LigPrint {
// Each label uses one lossless RGB image. Share its PDF page resources across copies.
public static class LabelPdf {
    public const string QueueName="导出标签 PDF（本地文件）";
    static void Write(Stream s,string text){var bytes=Encoding.ASCII.GetBytes(text);s.Write(bytes,0,bytes.Length);}
    static int Int32BE(byte[] b,int i){return (b[i]<<24)|(b[i+1]<<16)|(b[i+2]<<8)|b[i+3];}
    static byte[] ImageData(LabelFile doc,int row,int w,int h){
        using(var bmp=new Bitmap(w,h,PixelFormat.Format24bppRgb))
        using(var g=Graphics.FromImage(bmp))
        using(var png=new MemoryStream())
        using(var compressed=new MemoryStream()){
            g.Clear(Color.White);g.PageUnit=GraphicsUnit.Pixel;g.ScaleTransform(w/doc.Width,h/doc.Height);
            Drawing.Paint(g,doc,row,false);bmp.Save(png,ImageFormat.Png);
            byte[] b=png.ToArray();
            if(b[24]!=8 || b[25]!=2 || b[28]!=0)throw new InvalidDataException("PNG 像素格式异常。");
            for(int i=8;i<b.Length;){
                int n=Int32BE(b,i);
                if(Encoding.ASCII.GetString(b,i+4,4)=="IDAT")compressed.Write(b,i+8,n);
                i+=12+n;
            }
            return compressed.ToArray();
        }
    }
    public static void Save(LabelFile doc,List<PrintRow> rows,int total,string output,Action<int,int> progress){
        output=Disk.Local(output);
        string tmp=Path.Combine(Path.GetDirectoryName(output),Guid.NewGuid()+".tmp");
        try{
            using(var f=new FileStream(tmp,FileMode.CreateNew,FileAccess.Write)){
                var offsets=new List<long>{0};var pages=new List<int>();
                Func<int> obj=()=>{int n=offsets.Count;offsets.Add(f.Position);Write(f,n+" 0 obj\n");return n;};
                Write(f,"%PDF-1.4\n");obj();Write(f,"<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
                offsets.Add(0);
                string w=(doc.Width*72.0/25.4).ToString("0.####",CultureInfo.InvariantCulture),h=(doc.Height*72.0/25.4).ToString("0.####",CultureInfo.InvariantCulture);
                int pixelsW=(int)Math.Ceiling(doc.Width*300/25.4),pixelsH=(int)Math.Ceiling(doc.Height*300/25.4);
                foreach(var entry in rows){
                    var pixels=ImageData(doc,entry.Index,pixelsW,pixelsH);int img=obj();
                    Write(f,"<< /Type /XObject /Subtype /Image /Width "+pixelsW+" /Height "+pixelsH+" /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /FlateDecode /DecodeParms << /Predictor 15 /Colors 3 /BitsPerComponent 8 /Columns "+pixelsW+" >> /Length "+pixels.Length+" >>\nstream\n");
                    f.Write(pixels,0,pixels.Length);Write(f,"\nendstream\nendobj\n");
                    int contents=obj();string commands="q "+w+" 0 0 "+h+" 0 0 cm /Label Do Q\n";
                    Write(f,"<< /Length "+commands.Length+" >>\nstream\n"+commands+"endstream\nendobj\n");
                    for(int n=0;n<entry.Copies;n++){
                        progress(pages.Count,total);
                        pages.Add(obj());Write(f,"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 "+w+" "+h+"] /Resources << /XObject << /Label "+img+" 0 R >> >> /Contents "+contents+" 0 R >>\nendobj\n");
                    }
                }
                offsets[2]=f.Position;Write(f,"2 0 obj\n<< /Type /Pages /Count "+pages.Count+" /Kids [");
                foreach(int p in pages)Write(f,p+" 0 R ");Write(f,"] >>\nendobj\n");
                long start=f.Position;Write(f,"xref\n0 "+offsets.Count+"\n0000000000 65535 f \n");
                foreach(long pos in offsets.Skip(1))Write(f,pos.ToString("0000000000",CultureInfo.InvariantCulture)+" 00000 n \n");
                Write(f,"trailer\n<< /Size "+offsets.Count+" /Root 1 0 R >>\nstartxref\n"+start+"\n%%EOF\n");f.Flush(true);
            }
            if(File.Exists(output))File.Replace(tmp,output,null);else File.Move(tmp,output);
        }finally{if(File.Exists(tmp))File.Delete(tmp);}
    }
}
}


