using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
[assembly: AssemblyFileVersion("1.3.0.0")]
[assembly: AssemblyInformationalVersion("1.3.0")]
namespace LigPrint {
public static class Startup {
    [DllImport("user32.dll")]static extern bool SetProcessDPIAware();
    public static void ResolveLibraries(){
        AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=>{
            string name=new AssemblyName(e.Name).Name;
            using(var f=typeof(Startup).Assembly.GetManifestResourceStream(name+".dll")){
                if(f==null)return null;
                using(var m=new MemoryStream()){f.CopyTo(m);return Assembly.Load(m.ToArray());}
            }
        };
    }
    [STAThread] public static void Main(string[] args){
        ResolveLibraries();SetProcessDPIAware();Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        Application.ThreadException+=(s,e)=>MessageBox.Show(e.Exception.Message,"LigPrint");
        try{Run(args);}catch(Exception ex){MessageBox.Show(ex.Message,"LigPrint");}
    }
    [MethodImpl(MethodImplOptions.NoInlining)]static void Run(string[] args){
        if(args.Length>1)throw new ArgumentException("请一次打开一个 .lig 模板。");
        Audit.Write("start",0);
        using(var studio=new Studio(args.Length==0?null:args[0]))Application.Run(studio);
    }
}
}






