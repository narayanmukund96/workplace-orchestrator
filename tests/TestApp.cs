using System;
using System.Windows.Forms;
using System.Diagnostics;
[assembly:System.Reflection.AssemblyProduct("Workplace test fixture — not for distribution")]
static class TestApp
{
    [STAThread] static void Main(string[] args)
    {
        if(args.Length>0&&args[0]=="--child"){Application.Run(new ApplicationContext());return;}
        if(Environment.GetEnvironmentVariable("WORKPLACE_TEST_TREE")=="1")Process.Start(new ProcessStartInfo(Application.ExecutablePath,"--child"){UseShellExecute=false,CreateNoWindow=true});
        Application.Run(new Form{Text="Orchestrator controlled test application",Width=350,Height=100});
    }
}
