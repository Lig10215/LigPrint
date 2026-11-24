using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Design;
using System.Windows.Forms;

namespace LigPrint {
public sealed class LabelTextEditor : UITypeEditor {
    public override UITypeEditorEditStyle GetEditStyle(ITypeDescriptorContext context){
        return UITypeEditorEditStyle.Modal;
    }
    public override object EditValue(ITypeDescriptorContext context,IServiceProvider provider,object value){
        return Edit(Form.ActiveForm,value as string);
    }
    internal static string Edit(IWin32Window owner,string value){
        using(var form=new Form{Text="编辑文字",StartPosition=FormStartPosition.CenterParent,
                ClientSize=new Size(470,240),MinimumSize=new Size(300,180),Font=SystemFonts.MessageBoxFont}){
            var input=new TextBox{Dock=DockStyle.Fill,Multiline=true,AcceptsReturn=true,ScrollBars=ScrollBars.Vertical,
                Text=(value??"").Replace("\r\n","\n").Replace('\r','\n').Replace("\n",Environment.NewLine)};
            var buttons=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=42,FlowDirection=FlowDirection.RightToLeft};
            var ok=new Button{Text="确定",DialogResult=DialogResult.OK};
            var cancel=new Button{Text="取消",DialogResult=DialogResult.Cancel};
            buttons.Controls.Add(ok);buttons.Controls.Add(cancel);form.Padding=new Padding(10);
            form.Controls.Add(input);form.Controls.Add(buttons);form.CancelButton=cancel;form.ActiveControl=input;
            return form.ShowDialog(owner)==DialogResult.OK?input.Text:value;
        }
    }
}
}
