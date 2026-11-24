using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.Globalization;

namespace LigPrint {
// Measure in a fixed, high-resolution coordinate system so changing printer or canvas zoom
// does not change which characters belong on a line.
internal sealed class TextBlock {
    const float UnitsPerMm=16;
    readonly string text, family;
    readonly float points, requestedWidth;
    readonly bool bold;
    readonly List<string> lines=new List<string>();
    readonly float lineHeight;
    public readonly float Width;
    public float Height {get{return lines.Count*lineHeight;}}
    public bool Matches(Element e,string value,float width){
        return text==value && family==e.Typeface && points==e.Points && bold==e.Bold && requestedWidth==width;
    }
    Font MakeFont(float scale){
        return new Font(family,points*25.4f/72*scale,bold?FontStyle.Bold:FontStyle.Regular,GraphicsUnit.World);
    }
    static StringFormat Format(){
        var f=StringFormat.GenericTypographic;
        f.FormatFlags|=StringFormatFlags.NoWrap|StringFormatFlags.MeasureTrailingSpaces;
        return f;
    }
    public TextBlock(Element e,string value,float width){
        text=value;family=e.Typeface;points=e.Points;bold=e.Bold;requestedWidth=width;
        using(var bmp=new Bitmap(1,1))
        using(var g=Graphics.FromImage(bmp))
        using(var font=MakeFont(UnitsPerMm))
        using(var format=Format()){
            g.PageUnit=GraphicsUnit.Pixel;g.TextRenderingHint=TextRenderingHint.AntiAlias;
            lineHeight=font.GetHeight(g)/UnitsPerMm;
            var paragraphs=text.Replace("\r\n","\n").Replace('\r','\n').Split('\n');
            var widths=new Dictionary<string,float>();
            float minimum=.2f;
            foreach(var paragraph in paragraphs){
                var chars=StringInfo.GetTextElementEnumerator(paragraph);
                while(chars.MoveNext()){
                    string ch=chars.GetTextElement();float advance;
                    if(!widths.TryGetValue(ch,out advance)){
                        advance=g.MeasureString(ch,font,PointF.Empty,format).Width/UnitsPerMm;
                        widths[ch]=advance;
                    }
                    minimum=Math.Max(minimum,advance);
                }
            }
            Width=Math.Max(width,minimum);
            foreach(var paragraph in paragraphs){
                if(paragraph.Length==0){lines.Add("");continue;}
                int[] offsets=StringInfo.ParseCombiningCharacters(paragraph);
                int start=0;
                while(start<offsets.Length){
                    int remaining=offsets.Length-start;
                    Func<int,string> segment=n=>{
                        int end=start+n==offsets.Length?paragraph.Length:offsets[start+n];
                        return paragraph.Substring(offsets[start],end-offsets[start]);
                    };
                    Func<int,bool> fits=n=>g.MeasureString(segment(n),font,PointF.Empty,format).Width<=Width*UnitsPerMm+.01f;
                    int fit=1,probe=Math.Min(2,remaining);
                    while(probe>fit && fits(probe)){fit=probe;probe=Math.Min(remaining,fit*2);}
                    while(probe-fit>1){int mid=fit+(probe-fit)/2;if(fits(mid))fit=mid;else probe=mid;}
                    lines.Add(segment(fit));start+=fit;
                }
            }
        }
    }
    public void Paint(Graphics g,float x,float y){
        using(var font=MakeFont(1))
        using(var format=Format()){
            g.TextRenderingHint=TextRenderingHint.AntiAlias;
            foreach(string line in lines){
                if(line.Length!=0)g.DrawString(line,font,Brushes.Black,new PointF(x,y),format);
                y+=lineHeight;
            }
        }
    }
}
}
