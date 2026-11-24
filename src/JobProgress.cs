using System;
using System.Drawing;
using System.ComponentModel;
using System.Threading;
using System.Windows.Forms;
namespace LigPrint {
public class JobProgress:Form {
    readonly BackgroundWorker worker=new BackgroundWorker{WorkerReportsProgress=true,WorkerSupportsCancellation=true};
    readonly ProgressBar bar=new ProgressBar{Dock=DockStyle.Top,Height=22};
    readonly Label message=new Label{Dock=DockStyle.Top,Height=36,TextAlign=ContentAlignment.MiddleLeft,Text="正在准备标签…"};
    readonly Button cancel=new Button{Text="取消",Dock=DockStyle.Bottom,Height=30};
    bool finished;
    public Exception Error;
    public JobProgress(Action<Action<int,int>> run){
        Text="批量输出";Font=new Font("Microsoft YaHei UI",9);StartPosition=FormStartPosition.CenterParent;
        FormBorderStyle=FormBorderStyle.FixedDialog;MinimizeBox=false;MaximizeBox=false;Padding=new Padding(14);ClientSize=new Size(360,122);
        Controls.Add(cancel);Controls.Add(bar);Controls.Add(message);cancel.Click+=(s,e)=>Stop();
        worker.DoWork+=(s,e)=>{
            int previous=-1;
            run((done,total)=>{
                if(worker.CancellationPending)throw new OperationCanceledException();
                int percent=(int)(100.0*done/total);
                if(percent==previous)return;previous=percent;worker.ReportProgress(percent,done+" / "+total);
            });
        };
        worker.ProgressChanged+=(s,e)=>{bar.Value=e.ProgressPercentage;message.Text="正在输出标签    "+e.UserState;};
        worker.RunWorkerCompleted+=(s,e)=>{Error=e.Error;finished=true;DialogResult=Error==null?DialogResult.OK:DialogResult.Cancel;Close();};
        Shown+=(s,e)=>worker.RunWorkerAsync();FormClosing+=(s,e)=>{if(!finished){e.Cancel=true;Stop();}};
    }
    void Stop(){worker.CancelAsync();cancel.Enabled=false;message.Text="正在取消…";}
}
}
