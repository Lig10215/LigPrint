using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Linq;
using ZXing;
using ZXing.Common;

namespace LigPrint {
public static class Drawing {
    public static BitMatrix Code(Element e,string value){
        if(String.IsNullOrEmpty(value))throw new InvalidDataException("条码内容为空。");
        var hints=new Dictionary<EncodeHintType,object>();
        hints[EncodeHintType.MARGIN]=e.Type==Kind.二维码?4:20;
        if(e.Type==Kind.二维码)hints[EncodeHintType.CHARACTER_SET]="UTF-8";
        return new MultiFormatWriter().encode(value,e.Type==Kind.二维码?BarcodeFormat.QR_CODE:BarcodeFormat.CODE_128,1,1,hints);
    }
    public static void Paint(Graphics g,LabelFile doc,int row,bool editing,bool showWorkspace=false){
        var saved=g.Save();
        try{
            switch(doc.PrintTurn){
                case Turn.顺时针90度:g.TranslateTransform(doc.Width,0);g.RotateTransform(90);break;
                case Turn.旋转180度:g.TranslateTransform(doc.Width,doc.Height);g.RotateTransform(180);break;
                case Turn.顺时针270度:g.TranslateTransform(0,doc.Height);g.RotateTransform(270);break;
            }
            if(!showWorkspace)g.SetClip(new RectangleF(0,0,doc.ArtworkWidth,doc.ArtworkHeight),CombineMode.Intersect);
            using(var pen=new Pen(Color.Black,.35f))
            foreach(var source in doc.Items){
                var e=source;
                var state=g.Save();
                RectangleF box=e.Bounds(doc.Data,row);
                if(showWorkspace && e.Type!=Kind.文本)g.SetClip(new RectangleF(0,0,doc.Width,doc.Height),CombineMode.Intersect);
                if(e.Rotation!=Turn.正向 || e.Mirror){
                    g.TranslateTransform(box.X+box.Width/2,box.Y+box.Height/2);g.RotateTransform(90*(int)e.Rotation);if(e.Mirror)g.ScaleTransform(-1,1);
                    e=source.Copy();
                    e.W=source.Sideways?box.Height:box.Width;e.H=source.Sideways?box.Width:box.Height;
                    e.X=-e.W/2;e.Y=-e.H/2;box=new RectangleF(e.X,e.Y,e.W,e.H);
                }

                try{
                    switch(e.Type){
                        case Kind.矩形:g.DrawRectangle(pen,e.X,e.Y,e.W,e.H);break;
                        case Kind.椭圆:g.DrawEllipse(pen,box);break;
                        case Kind.横线:g.DrawLine(pen,e.X,e.Y+e.H/2,e.X+e.W,e.Y+e.H/2);break;
                        case Kind.图片:
                            using(var stream=new MemoryStream(e.ImageBytes))
                            using(var img=Image.FromStream(stream))g.DrawImage(img,box);
                            break;
                        case Kind.文本:
                            source.Layout(doc.Data,row).Paint(g,box.X,box.Y);
                            break;
                        default:
                            var matrix=Code(e,e.Value(doc.Data,row));
                            float dx=e.W/matrix.Width,dy=e.H/matrix.Height;
                            if(e.Type==Kind.二维码)dx=dy=Math.Min(dx,dy);
                            float left=e.X+(e.W-matrix.Width*dx)/2,top=e.Y+(e.H-matrix.Height*dy)/2;
                            for(int y=0;y<matrix.Height;y++){
                                for(int x=0;x<matrix.Width;){
                                    if(!matrix[x,y]){x++;continue;}
                                    int start=x++;
                                    while(x<matrix.Width && matrix[x,y])x++;
                                    g.FillRectangle(Brushes.Black,left+start*dx,top+y*dy,(x-start)*dx,dy);
                                }
                            }
                            break;
                    }
                }catch(Exception ex){
                    if(!editing)throw new InvalidDataException("第 "+(row+1)+" 条记录，"+e.Type+" 内容无效。"+ex.Message,ex);
                    using(var font=new Font("Microsoft YaHei",2.8f,GraphicsUnit.World))g.DrawString("内容无效",font,Brushes.Firebrick,box);
                }finally{g.Restore(state);}
            }
        }finally{g.Restore(saved);}
    }
}
public class Canvas:Control {
    public LabelFile Doc;
    public int Row;
    readonly List<Element> selectedItems=new List<Element>();
    public Element Selected {
        get{return selectedItems.LastOrDefault();}
        set{selectedItems.Clear();if(value!=null)selectedItems.Add(value);}
    }
    public Element[] Selection {get{return selectedItems.ToArray();}}
    public void SelectGroup(IEnumerable<Element> group){
        selectedItems.Clear();selectedItems.AddRange(group);Invalidate();
    }
    readonly Dictionary<Element,RectangleF> dragBounds=new Dictionary<Element,RectangleF>();
    public bool Preview;
    public event Action SelectionChanged,BeforeDrag,Moved;
    public float Zoom=1;
    float scale,ox,oy;
    bool resizing,drag,started;
    Point anchor;
    Point marqueeEnd;
    bool marqueePending, marqueeVisible;
    Element[] selectionBeforeMarquee;
    Rectangle MarqueeBounds {
        get{return Rectangle.FromLTRB(Math.Min(anchor.X,marqueeEnd.X),Math.Min(anchor.Y,marqueeEnd.Y),
                                      Math.Max(anchor.X,marqueeEnd.X),Math.Max(anchor.Y,marqueeEnd.Y));}
    }
    RectangleF original,originalDisplay;
    public Canvas(){DoubleBuffered=true; BackColor=Color.FromArgb(235,239,244);SetStyle(ControlStyles.Selectable,true);TabStop=true;}
    protected override void OnPaint(PaintEventArgs ev){
        base.OnPaint(ev);if(Doc==null)return;
        var g=ev.Graphics;
        scale=Math.Max(.1f,Math.Min((Width-84)/Doc.Width,(Height-82)/Doc.Height)*(Preview?1:.77f)*Zoom);
        ox=Math.Max(34,(Width-Doc.Width*scale)/2);oy=Math.Max(30,(Height-Doc.Height*scale)/2);
        float w=Doc.Width*scale,h=Doc.Height*scale;
        using(var ruler=new SolidBrush(Color.FromArgb(248,250,252))){
            g.FillRectangle(ruler,0,0,Width,22);g.FillRectangle(ruler,0,0,27,Height);
        }
        for(int x=0;x<=Doc.Width;x+=5){
            float pos=ox+x*scale;g.DrawLine(Pens.Silver,pos,x%10==0?13:17,pos,22);
            if(x%10==0 && 10*scale>20)g.DrawString(x.ToString(),SystemFonts.SmallCaptionFont,Brushes.DimGray,pos+2,0);
        }
        for(int y=0;y<=Doc.Height;y+=5){
            float pos=oy+y*scale;g.DrawLine(Pens.Silver,y%10==0?17:22,pos,27,pos);
            if(y%10==0 && 10*scale>16)g.DrawString(y.ToString(),SystemFonts.SmallCaptionFont,Brushes.DimGray,0,pos+1);
        }
        g.FillRectangle(Brushes.LightGray,ox+3,oy+4,w,h);g.FillRectangle(Brushes.White,ox,oy,w,h);
        var state=g.Save();g.SetClip(new Rectangle(27,22,Math.Max(0,Width-27),Math.Max(0,Height-22)),CombineMode.Intersect);g.TranslateTransform(ox,oy);g.ScaleTransform(scale,scale);
        Drawing.Paint(g,Doc,Row,true,!Preview);g.Restore(state);
        if(!Preview)foreach(var e in selectedItems){
            var box=e.Bounds(Doc.Data,Row);float x=ox+box.X*scale,y=oy+box.Y*scale;
            using(var p=new Pen(Color.RoyalBlue)){p.DashStyle=DashStyle.Dash;g.DrawRectangle(p,x,y,box.Width*scale,box.Height*scale);}
            if(selectedItems.Count==1)g.FillRectangle(Brushes.RoyalBlue,x+box.Width*scale-4,y+box.Height*scale-4,8,8);
        }
        if(marqueeVisible && !Preview){
            var rect=MarqueeBounds;
            using(var fill=new SolidBrush(Color.FromArgb(32,Color.RoyalBlue)))
            using(var border=new Pen(Color.RoyalBlue)){
                border.DashStyle=DashStyle.Dash;
                g.FillRectangle(fill,rect);g.DrawRectangle(border,rect);
            }
        }
    }
    void UpdateMarquee(Point end){
        marqueeEnd=end;
        if(!marqueeVisible){
            Size threshold=SystemInformation.DragSize;
            var deadZone=new Rectangle(anchor.X-threshold.Width/2,anchor.Y-threshold.Height/2,threshold.Width,threshold.Height);
            if(deadZone.Contains(end))return;
            marqueeVisible=true;
        }
        var rect=MarqueeBounds;
        var area=new RectangleF((rect.Left-ox)/scale,(rect.Top-oy)/scale,rect.Width/scale,rect.Height/scale);
        selectedItems.Clear();
        foreach(var item in Doc.Items){
            if(!item.Locked && area.IntersectsWith(item.Bounds(Doc.Data,Row)))selectedItems.Add(item);
        }
        Invalidate();
    }
    protected override void OnMouseWheel(MouseEventArgs e){
        base.OnMouseWheel(e);
        if(Capture)return;
        Zoom=Math.Max(.08f,Math.Min(2f,Zoom*(float)Math.Pow(1.2,e.Delta/120.0)));
        Invalidate();
    }
    protected override void OnMouseDown(MouseEventArgs e){
        base.OnMouseDown(e);Focus();if(Preview || Doc==null || e.Button!=MouseButtons.Left || scale<=0)return;
        float x=(e.X-ox)/scale,y=(e.Y-oy)/scale;
        var hit=Doc.Items.FindLast(a=>a.Bounds(Doc.Data,Row).Contains(x,y));
        bool toggle=(ModifierKeys&Keys.Control)!=0;
        var selectedBox=Selected==null?RectangleF.Empty:Selected.Bounds(Doc.Data,Row);
        resizing=!toggle && selectedItems.Count==1 && Math.Abs(x-selectedBox.Right)*scale<7 && Math.Abs(y-selectedBox.Bottom)*scale<7;
        if(!resizing && !toggle && (hit==null || hit.Locked)){
            // Locked template artwork can cover the entire label. A click selects it,
            // while dragging over it selects the movable objects above the artwork.
            selectionBeforeMarquee=Selection;
            marqueePending=true;marqueeVisible=false;anchor=marqueeEnd=e.Location;
            Selected=hit;Capture=true;
            if(SelectionChanged!=null)SelectionChanged();Invalidate();return;
        }
        if(toggle && hit!=null){
            if(!selectedItems.Remove(hit))selectedItems.Add(hit);
        }else if(!resizing && (hit==null || !selectedItems.Contains(hit)))Selected=hit;
        if(!toggle && Selected!=null){
            anchor=e.Location;original=new RectangleF(Selected.X,Selected.Y,Selected.W,Selected.H);
            originalDisplay=Selected.Bounds(Doc.Data,Row);
            dragBounds.Clear();
            foreach(var item in selectedItems)dragBounds[item]=new RectangleF(item.X,item.Y,item.W,item.H);
            drag=true;started=false;Capture=true;
        }
        if(SelectionChanged!=null)SelectionChanged();Invalidate();
    }
    protected override void OnMouseMove(MouseEventArgs e){
        base.OnMouseMove(e);
        if(marqueePending){UpdateMarquee(e.Location);return;}
        if(!drag || Selected==null || selectedItems.Any(item=>item.Locked) || anchor==e.Location && !started)return;
        if(!started && BeforeDrag!=null)BeforeDrag();started=true;
        float dx=(e.X-anchor.X)/scale,dy=(e.Y-anchor.Y)/scale;
        if(resizing){
            if(Selected.Type==Kind.文本){
                Selected.W=dx==0?original.Width:Math.Max(.2f,Math.Min(500,originalDisplay.Width+dx));
                Selected.H=dy==0?original.Height:Math.Max(.2f,Math.Min(500,originalDisplay.Height+dy));
                var bounds=Selected.Bounds(Doc.Data,Row);
                if(Selected.Sideways)Selected.H=bounds.Height;else Selected.W=bounds.Width;
            }else{
                Selected.W=Math.Max(.2f,Math.Min(Doc.Width-Selected.X,original.Width+dx));
                Selected.H=Math.Max(.2f,Math.Min(Doc.Height-Selected.Y,original.Height+dy));
            }
        }else{
            foreach(var pair in dragBounds){
                if(pair.Key.Type==Kind.文本)continue;
                var box=pair.Value;
                dx=Math.Max(-box.Left,Math.Min(Doc.Width-box.Right,dx));
                dy=Math.Max(-box.Top,Math.Min(Doc.Height-box.Bottom,dy));
            }
            foreach(var pair in dragBounds){pair.Key.X=pair.Value.X+dx;pair.Key.Y=pair.Value.Y+dy;}
        }
        Invalidate();
    }
    protected override void OnMouseUp(MouseEventArgs e){
        base.OnMouseUp(e);if(e.Button!=MouseButtons.Left)return;
        if(marqueePending){
            UpdateMarquee(e.Location);
            marqueePending=marqueeVisible=false;selectionBeforeMarquee=null;
            if(SelectionChanged!=null)SelectionChanged();Invalidate();
        }
        drag=false;if(started && Moved!=null)Moved();started=false;Capture=false;
    }
    protected override void OnMouseCaptureChanged(EventArgs e){
        base.OnMouseCaptureChanged(e);
        if(Capture)return;
        if(marqueePending){
            marqueePending=marqueeVisible=false;
            SelectGroup(selectionBeforeMarquee);selectionBeforeMarquee=null;
            if(SelectionChanged!=null)SelectionChanged();
        }
        drag=false;
        if(started && Moved!=null)Moved();
        started=false;
    }
    protected override bool ProcessCmdKey(ref Message message,Keys key){
        if(key==Keys.Escape && marqueePending){Capture=false;return true;}
        return base.ProcessCmdKey(ref message,key);
    }
}
}







