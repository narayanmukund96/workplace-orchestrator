using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WorkplaceOrchestrator;

static class UiSmoke
{
    static MainController controller;
    static Window window;
    static string output;
    static int failures;
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);File.AppendAllText(Path.Combine(output,"ui-results.txt"),"PASS "+message+Environment.NewLine);}
    static IEnumerable<T> Children<T>(DependencyObject parent) where T:DependencyObject
    {
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++){var child=VisualTreeHelper.GetChild(parent,i);if(child is T)yield return (T)child;foreach(var nested in Children<T>(child))yield return nested;}
    }
    static Button Button(string name){return (Button)window.FindName(name);}
    static void Click(Button button){button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));}
    static void FillNextDialog(Action<FormDialog> action)
    {
        window.Dispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>
        {
            try{var dialog=window.OwnedWindows.OfType<FormDialog>().Single();dialog.UpdateLayout();action(dialog);}
            catch(Exception ex){failures++;File.AppendAllText(Path.Combine(output,"ui-results.txt"),"FAIL dialog: "+ex+Environment.NewLine);foreach(var dialog in window.OwnedWindows.OfType<Window>().ToArray())dialog.Close();}
        }));
    }
    static void Accept(FormDialog dialog,string label){Click(Children<Button>(dialog).First(b=>Convert.ToString(b.Content)==label));}
    static async Task Run()
    {
        try
        {
            Check(window.WindowStyle==WindowStyle.SingleBorderWindow&&window.ResizeMode==ResizeMode.CanResize&&window.ShowInTaskbar&&window.Icon!=null,"Native window chrome, resize, taskbar and icon configured");
            window.WindowState=WindowState.Minimized;await Task.Delay(100);Check(window.WindowState==WindowState.Minimized,"Native minimise");
            window.WindowState=WindowState.Maximized;await Task.Delay(100);Check(window.WindowState==WindowState.Maximized,"Native maximise");
            window.WindowState=WindowState.Normal;window.Width=1000;window.Height=700;await Task.Delay(100);Check(window.WindowState==WindowState.Normal&&window.ActualWidth==1000,"Native restore and resize");
            Check(!Button("LaunchWorkspace").IsEnabled,"Empty workspace disables launch");
            FillNextDialog(d=>{Children<TextBox>(d).First().Text="UI verified workspace";Accept(d,"Create workspace");});Click(Button("NewWorkspace"));
            var workspaces=(ListBox)window.FindName("WorkspaceList");Check(workspaces.Items.Count==1,"Create workspace through dialog");
            var pickerData=new List<DiscoveredApp>{new DiscoveredApp{Name="Controlled application",Path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"notepad.exe"),Aumid=""}};
            typeof(MainController).GetField("catalogue",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(controller,pickerData);
            FillNextDialog(d=>{Children<ListBox>(d).First().SelectedIndex=0;Accept(d,"Add application");});Click(Button("AddApp"));
            await Task.Delay(250);window.UpdateLayout();
            var apps=(ListBox)window.FindName("AppList");Check(apps.Items.Count==1,"Add application through searchable picker");Check(Button("LaunchWorkspace").IsEnabled,"Configured workspace enables launch");
            var rowMenu=Children<Button>(apps).First(b=>Convert.ToString(b.Tag)=="menu");Click(rowMenu);
            FillNextDialog(d=>
            {
                var mode=Children<ComboBox>(d).First();mode.SelectedItem="Timed";
                var text=Children<TextBox>(d).ToArray();text[0].Text="My Notepad";text[1].Text="3.5";
                Accept(d,"Save launch settings");
            });
            ((MenuItem)rowMenu.ContextMenu.Items[0]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            var entry=(AppEntry)apps.Items[0];Check(entry.Name=="My Notepad"&&entry.Mode==LaunchMode.Timed&&entry.DelayMs==3500,"Edit per-app launch mode and delay through dialog");
            Click(Button("WorkspaceMenu"));
            FillNextDialog(d=>{Children<TextBox>(d).First().Text="Renamed workspace";Accept(d,"Save settings");});
            ((MenuItem)Button("WorkspaceMenu").ContextMenu.Items[0]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Check(((Workspace)workspaces.SelectedItem).Name=="Renamed workspace","Rename workspace through settings");
            Click(Button("WorkspaceMenu"));((MenuItem)Button("WorkspaceMenu").ContextMenu.Items[1]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Check(workspaces.Items.Count==2,"Duplicate workspace through menu");
            using(var store=new Store(Path.Combine(output,"data","workspaces.db"))){var saved=store.Load();Check(saved.Count==2&&saved[0].Apps[0].DelayMs==3500&&saved[1].Apps[0].Id!=saved[0].Apps[0].Id,"UI changes persist with independent duplicated app IDs");}
            await Task.Delay(1500);
            using(var process=Process.GetCurrentProcess())
            {
                process.Refresh();var cpu=process.TotalProcessorTime;var watch=Stopwatch.StartNew();await Task.Delay(10000);process.Refresh();
                double corePercent=(process.TotalProcessorTime-cpu).TotalMilliseconds/watch.Elapsed.TotalMilliseconds*100;
                File.AppendAllText(Path.Combine(output,"ui-results.txt"),"METRIC idle CPU single-core-equivalent="+corePercent.ToString("0.00")+"%; working set="+(process.WorkingSet64/1048576.0).ToString("0.0")+" MB; interval="+watch.ElapsedMilliseconds+"ms"+Environment.NewLine);
                // Record observed resource usage; acceptance is evidence-based, not an invented fixed budget.
            }
        }
        catch(Exception ex){failures++;File.AppendAllText(Path.Combine(output,"ui-results.txt"),"FAIL "+ex+Environment.NewLine);}
        finally{window.Close();}
    }
    [STAThread] static int Main(string[] args)
    {
        output=args[0];Directory.CreateDirectory(output);var watch=Stopwatch.StartNew();var app=new Application();
        try
        {
            controller=new MainController(false,Path.Combine(output,"data"));window=controller.Window;
            window.Loaded+=async(s,e)=>{File.AppendAllText(Path.Combine(output,"ui-results.txt"),"METRIC time to loaded UI="+watch.ElapsedMilliseconds+"ms"+Environment.NewLine);await Run();};
            app.Run(window);controller.Dispose();
        }
        catch(Exception ex){failures++;File.AppendAllText(Path.Combine(output,"ui-results.txt"),"FAIL startup "+ex+Environment.NewLine);}
        File.AppendAllText(Path.Combine(output,"ui-results.txt"),"RESULT UI failures="+failures+Environment.NewLine);return failures==0?0:1;
    }
}
