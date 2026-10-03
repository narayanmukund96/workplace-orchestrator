using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

[assembly: AssemblyTitle("Workplace Orchestrator")]
[assembly: AssemblyProduct("Workplace Orchestrator")]
[assembly: AssemblyDescription("Local application workspaces for Windows")]
[assembly: AssemblyCompany("Workplace Orchestrator")]
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]
[assembly: AssemblyInformationalVersion("1.1.0")]

namespace WorkplaceOrchestrator
{
    public static class Program
    {
        [STAThread] public static int Main(string[] args)
        {
            if(args.Length==1&&args[0]=="--register-shell"){try{WindowsIntegration.RegisterShortcut();return 0;}catch{return 1;}}
            WindowsIntegration.SetIdentity();
            bool demo=args.Length==2 && args[0]=="--render-demo";
            bool first;
            using(var mutex=new Mutex(true,@"Local\WorkplaceOrchestrator-"+Environment.UserName,out first))
            {
                if(!first&&!demo){try{using(var signal=EventWaitHandle.OpenExisting(@"Local\WorkplaceOrchestrator-Activate-"+Environment.UserName))signal.Set();}catch{}return 0;}
                try
                {
                    var application=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
                    using(var activation=new EventWaitHandle(false,EventResetMode.AutoReset,@"Local\WorkplaceOrchestrator-Activate-"+Environment.UserName))
                    {
                    var controller=new MainController(demo);
                    application.DispatcherUnhandledException+=(s,e)=>{MessageBox.Show(controller.Window,e.Exception.Message,"Something needs attention",MessageBoxButton.OK,MessageBoxImage.Warning);e.Handled=true;};
                    if(demo)
                    {
                        controller.Window.Width=1220;controller.Window.Height=820;
                        var content=(Grid)controller.Window.Content;content.Background=controller.Window.Background;
                        content.Measure(new Size(1220,820));content.Arrange(new Rect(0,0,1220,820));content.UpdateLayout();
                        var bitmap=new RenderTargetBitmap(1220,820,96,96,PixelFormats.Pbgra32);bitmap.Render(content);
                        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
                        using(var file=File.Create(args[1]))encoder.Save(file);
                        controller.Dispose();return 0;
                    }
                    application.MainWindow=controller.Window;
                    controller.EnableTray();
                    var registration=ThreadPool.RegisterWaitForSingleObject(activation,(state,timedOut)=>application.Dispatcher.BeginInvoke(new Action(controller.OpenWindow)),null,Timeout.Infinite,false);
                    application.SessionEnding+=(s,e)=>controller.EndSession();
                    controller.Start(args.Length==1&&args[0]=="--background");
                    application.Run();registration.Unregister(null);controller.Dispose();return 0;
                    }
                }
                catch(Exception ex){if(demo){File.WriteAllText(args[1]+".error.txt",ex.ToString());return 1;}MessageBox.Show("Workplace Orchestrator could not open. Your saved data has not been reset.\n\n"+ex.Message,"Startup problem",MessageBoxButton.OK,MessageBoxImage.Error);return 1;}
            }
        }
    }
    public sealed class MainController : IDisposable
    {
        public Window Window { get; private set; }
        readonly List<Workspace> workspaces;
        readonly Store store;
        readonly WindowsPlatform platform;
        readonly ProcessMonitor monitor;
        readonly Engine engine;
        readonly DispatcherTimer timer;
        readonly string dataDirectory;
        readonly bool demo;
        readonly bool isolated;
        Preferences preferences;
        ThemeManager theme;
        System.Windows.Forms.NotifyIcon tray;
        bool refreshing,closing,disposed,started;
        public bool TrayVisible {get{return tray!=null&&tray.Visible;}}
        public Preferences CurrentPreferences {get{return new Preferences{Theme=preferences.Theme,CloseToTray=preferences.CloseToTray,StartWithWindows=preferences.StartWithWindows};}}
        List<DiscoveredApp> catalogue;
        Workspace selected;
        T Get<T>(string name) where T:FrameworkElement { return (T)Window.FindName(name); }
        void Text(string name,string text) { Get<TextBlock>(name).Text=text; }
        void Click(string name,RoutedEventHandler handler) { Get<Button>(name).Click+=handler; }
        public MainController(bool demo,string dataDirectoryOverride=null)
        {
            this.demo=demo;
            isolated=dataDirectoryOverride!=null;
            using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("MainWindow.xaml")) Window=(Window)XamlReader.Load(stream);
            dataDirectory=dataDirectoryOverride??System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"WorkplaceOrchestrator");
            ApplicationMetadata.ConfigureCache(dataDirectory);
            Window.WindowStyle=WindowStyle.SingleBorderWindow;Window.ResizeMode=ResizeMode.CanResize;Window.ShowInTaskbar=true;
            using(var icon=Assembly.GetExecutingAssembly().GetManifestResourceStream("WorkplaceOrchestrator.ico")){var frame=BitmapFrame.Create(icon,BitmapCreateOptions.None,BitmapCacheOption.OnLoad);frame.Freeze();Window.Icon=frame;}
            if(!demo)store=new Store(System.IO.Path.Combine(dataDirectory,"workspaces.db"));
            workspaces=demo?DemoData():store.Load();
            preferences=demo?new Preferences():store.LoadPreferences();
            if(!demo&&!isolated)preferences.StartWithWindows=WindowsIntegration.StartupEnabled();
            theme=new ThemeManager(Window);theme.Apply(preferences.Theme);
            Window.Width=Math.Min(Window.Width,SystemParameters.WorkArea.Width-32);Window.Height=Math.Min(Window.Height,SystemParameters.WorkArea.Height-32);
            Window.MinWidth=Math.Min(Window.MinWidth,Window.Width);Window.MinHeight=Math.Min(Window.MinHeight,Window.Height);
            platform=new WindowsPlatform();engine=new Engine(platform);monitor=new ProcessMonitor();
            engine.Changed+=UpdateControls;
            monitor.Changed+=()=>DispatchRefresh();
            monitor.Exited+=(key,code)=>{if(!disposed)Window.Dispatcher.BeginInvoke(new Action(()=>engine.ReportExit(key,code)));};
            Get<ListBox>("WorkspaceList").SelectionChanged+=(s,e)=>{selected=Get<ListBox>("WorkspaceList").SelectedItem as Workspace;ShowWorkspace();};
            Click("NewWorkspace",(s,e)=>CreateWorkspace());
            Click("WorkspaceMenu",(s,e)=>WorkspaceMenu((Button)s));
            Click("AddApp",async(s,e)=>await AddApplication());
            Click("LaunchWorkspace",async(s,e)=>{if(selected!=null)await RunAction(()=>engine.Launch(selected));});
            Click("StopWorkspace",async(s,e)=>{if(selected!=null)await RunAction(()=>engine.StopWorkspace(selected));});
            Click("Cancel",async(s,e)=>await RunAction(()=>engine.Cancel()));
            Click("Skip",(s,e)=>engine.Skip());Click("Retry",(s,e)=>engine.RetryPaused());
            Click("Diagnostics",async(s,e)=>await Diagnostics());Click("Privacy",(s,e)=>Privacy());
            Click("Preferences",(s,e)=>EditPreferences());
            Get<ListBox>("AppList").AddHandler(Button.ClickEvent,new RoutedEventHandler(async(s,e)=>
            {
                var b=e.OriginalSource as Button;if(b==null)return;var a=b.DataContext as AppEntry;if(a==null)return;
                if((string)b.Tag=="menu")AppMenu(b,a);else if((string)b.Tag=="action")await AppAction(a);
            }));
            timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(5)};timer.Tick+=async(s,e)=>await Refresh();
            Window.Loaded+=(s,e)=>Initialize();
            Window.Closing+=(s,e)=>
            {
                if(closing)return;
                if(tray!=null&&preferences.CloseToTray){e.Cancel=true;Window.Hide();return;}
                if(engine.Busy)
                {
                    e.Cancel=true;
                    if(MessageBox.Show(Window,"Cancel the launch sequence and close Orchestrator? Applications already opened will remain running.","Close Orchestrator",MessageBoxButton.YesNo,MessageBoxImage.Question)==MessageBoxResult.Yes)
                        Window.Dispatcher.BeginInvoke(new Action(async()=>await ExitAsync()));
                }
            };
            Window.Closed+=(s,e)=>{if(tray!=null){Dispose();Application.Current.Shutdown();}};
            BindWorkspaces(workspaces.FirstOrDefault());
        }
        async void Initialize(){if(started||demo)return;started=true;monitor.Configure(workspaces.SelectMany(w=>w.Apps));timer.Start();await Refresh();}
        public void Start(bool background){Initialize();if(!background)OpenWindow();}
        public void OpenWindow(){if(disposed)return;Window.Show();if(Window.WindowState==WindowState.Minimized)Window.WindowState=WindowState.Normal;Window.Activate();}
        public void EnableTray()
        {
            if(tray!=null)return;
            tray=new System.Windows.Forms.NotifyIcon{Text="Workplace Orchestrator"};
            using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("WorkplaceOrchestrator.ico"))using(var icon=new System.Drawing.Icon(stream))tray.Icon=(System.Drawing.Icon)icon.Clone();
            var menu=new System.Windows.Forms.ContextMenuStrip();
            menu.Items.Add("Open Workplace",null,(s,e)=>Window.Dispatcher.BeginInvoke(new Action(OpenWindow)));
            menu.Items.Add("Exit Workplace",null,(s,e)=>Window.Dispatcher.BeginInvoke(new Action(async()=>await ExitAsync())));
            tray.ContextMenuStrip=menu;tray.MouseClick+=(s,e)=>{if(e.Button==System.Windows.Forms.MouseButtons.Left)Window.Dispatcher.BeginInvoke(new Action(OpenWindow));};tray.Visible=true;
        }
        public async Task ExitAsync(){if(closing)return;closing=true;await engine.Cancel();Window.Close();if(tray!=null){Dispose();Application.Current.Shutdown();}}
        public void EndSession(){closing=true;engine.Cancel();Dispose();}
        public void ApplyPreferences(Preferences value)
        {
            bool changed=value.StartWithWindows!=preferences.StartWithWindows;
            try{if(changed&&!isolated&&!demo)WindowsIntegration.SetStartup(value.StartWithWindows);if(store!=null)store.SavePreferences(value);}
            catch{if(changed&&!isolated&&!demo)WindowsIntegration.SetStartup(preferences.StartWithWindows);throw;}
            preferences=value;theme.Apply(value.Theme);
        }
        void EditPreferences()
        {
            var form=new FormDialog(Window,"Preferences","Workplace stays local. Starting with Windows never launches a workspace.");
            var startup=new CheckBox{Content="Start Workplace with Windows",IsChecked=preferences.StartWithWindows,Margin=new Thickness(0,14,0,8)};form.Body.Children.Add(startup);
            var close=form.Choice("When I close the window",new[]{"Close window to tray","Exit Workplace"},preferences.CloseToTray?"Close window to tray":"Exit Workplace");
            var appearance=form.Choice("Appearance",Enum.GetNames(typeof(Appearance)),preferences.Theme.ToString());
            form.Note("Close to tray keeps your session ownership available. Exit leaves your applications running; they are protected when Workplace reopens.");
            form.Accept("Save preferences",()=>{try{ApplyPreferences(new Preferences{StartWithWindows=startup.IsChecked==true,CloseToTray=close.SelectedIndex==0,Theme=(Appearance)Enum.Parse(typeof(Appearance),(string)appearance.SelectedItem)});return null;}catch(Exception ex){return "Could not save preferences: "+ex.Message;}});form.ShowDialog();
        }
        static List<Workspace> DemoData()
        {
            string sys=Environment.GetFolderPath(Environment.SpecialFolder.System);
            var w=new Workspace{Name="Focused work"};
            w.Apps.Add(new AppEntry{Name="Visual Studio Code",Path=@"C:\Applications\Microsoft VS Code\Code.exe",State=AppState.Running,Detail="Opened by this workspace"});
            w.Apps.Add(new AppEntry{Name="Microsoft Edge",Path=@"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",Mode=LaunchMode.Smart,State=AppState.Running,Detail="Already running · protected"});
            w.Apps.Add(new AppEntry{Name="Notepad",Path=System.IO.Path.Combine(sys,"notepad.exe"),State=AppState.Closed,Detail="Ready to launch"});
            return new List<Workspace>{w,new Workspace{Name="Research"},new Workspace{Name="Job search"}};
        }
        void BindWorkspaces(Workspace selection)
        {
            Get<ListBox>("WorkspaceList").ItemsSource=null;Get<ListBox>("WorkspaceList").ItemsSource=workspaces;Get<ListBox>("WorkspaceList").SelectedItem=selection;
            selected=selection;ShowWorkspace();
        }
        void ShowWorkspace()
        {
            if(selected!=null)
            {
                foreach(var a in selected.Apps){LoadIcon(a);if(!demo&&String.IsNullOrEmpty(a.Aumid)&&!File.Exists(a.Path)){a.State=AppState.Failed;a.Detail="Application missing. Use Choose application again in the row menu.";}}
                Text("WorkspaceTitle",selected.Name);Text("WorkspaceSubtitle",selected.Summary+" · Your applications, ready in the right order.");
                Text("ModeTitle",selected.DefaultMode+" launch, at your pace");
                Text("ModeDescription",selected.DefaultMode==LaunchMode.Hybrid?"A little breathing room between apps. More when your machine needs it.":selected.DefaultMode==LaunchMode.Smart?"Adapts to processor, memory and disk pressure as applications open.":selected.DefaultMode==LaunchMode.Timed?"Follows your chosen intervals between applications.":"Opens apps in order, with no settling delay.");
                Text("EmptyDescription","Add the applications you use together. You can adjust their order and timing anytime.");
            }
            else {Text("WorkspaceTitle","A calmer start to work.");Text("WorkspaceSubtitle","Your applications, ready in the right order.");Text("ModeTitle","One workspace. Everything you need.");Text("ModeDescription","Create your first workspace to get started.");Text("EmptyDescription","Create a workspace, then add the applications you use together.");}
            Get<ListBox>("AppList").ItemsSource=null;Get<ListBox>("AppList").ItemsSource=selected==null?null:selected.Apps;
            Get<Border>("EmptyState").Visibility=selected==null||selected.Apps.Count==0?Visibility.Visible:Visibility.Collapsed;
            UpdateControls();
        }
        void LoadIcon(AppEntry app)
        {
            if(app.Icon==null)app.Icon=ApplicationMetadata.Icon(app.Path,app.IconReference);
        }
        void UpdateControls()
        {
            if(disposed)return;
            foreach(var w in workspaces){int running=w.Apps.Count(a=>a.State==AppState.Running);w.RuntimeStatus=engine.ActiveWorkspace==w?(engine.Paused?"Paused":"Launching"):running>0?running+" running":"";}
            Text("Status",engine.Message);
            Get<Button>("LaunchWorkspace").IsEnabled=selected!=null&&selected.Apps.Any(a=>a.Enabled)&&!engine.Busy;
            Get<Button>("StopWorkspace").IsEnabled=selected!=null&&engine.SessionFor(selected)!=null;
            Get<Button>("AddApp").IsEnabled=selected!=null&&!engine.Busy;
            Get<Button>("NewWorkspace").IsEnabled=!engine.Busy;Get<Button>("WorkspaceMenu").IsEnabled=selected!=null&&!engine.Busy;
            Get<Button>("Cancel").Visibility=engine.Busy?Visibility.Visible:Visibility.Collapsed;
            Get<Button>("Skip").Visibility=engine.ActiveApp!=null&&(engine.Paused||engine.ActiveApp.State==AppState.Waiting)?Visibility.Visible:Visibility.Collapsed;
            Get<Button>("Retry").Visibility=engine.Paused?Visibility.Visible:Visibility.Collapsed;
        }
        void DispatchRefresh() {if(!disposed)Window.Dispatcher.BeginInvoke(new Action(async()=>await Refresh()));}
        async Task Refresh()
        {
            if(refreshing||disposed||demo)return;refreshing=true;
            var paths=PackagePaths();
            try {await engine.Refresh(workspaces.ToArray());PersistPackagePaths(paths);if(!disposed)monitor.Observe(engine.AllCurrent);}
            catch(Exception ex){Text("Status","Status update unavailable: "+ex.Message);}
            finally{refreshing=false;}
        }
        async Task RunAction(Func<Task> action)
        {
            var paths=PackagePaths();
            try {await action();PersistPackagePaths(paths);await Refresh();}
            catch(Exception ex){MessageBox.Show(Window,ex.Message,"Action needs attention",MessageBoxButton.OK,MessageBoxImage.Warning);}
        }
        Dictionary<string,string> PackagePaths(){return workspaces.SelectMany(w=>w.Apps).Where(a=>!String.IsNullOrEmpty(a.Aumid)).ToDictionary(a=>a.Id,a=>a.Path);}
        void PersistPackagePaths(Dictionary<string,string> before)
        {
            var changed=workspaces.SelectMany(w=>w.Apps).Where(a=>before.ContainsKey(a.Id)&&!Rules.SamePath(before[a.Id],a.Path)).ToList();
            if(changed.Count==0)return;
            store.Save(workspaces);monitor.Configure(workspaces.SelectMany(w=>w.Apps));
            foreach(var app in changed){app.Icon=null;LoadIcon(app);app.Changed();}
        }
        bool Save()
        {
            try{foreach(var w in workspaces)w.Updated=DateTime.UtcNow;store.Save(workspaces);monitor.Configure(workspaces.SelectMany(w=>w.Apps));BindWorkspaces(selected);DispatchRefresh();return true;}
            catch(Exception ex)
            {
                MessageBox.Show(Window,"Your change was not saved.\n\n"+ex.Message,"Storage problem",MessageBoxButton.OK,MessageBoxImage.Error);
                try{string id=selected==null?null:selected.Id;workspaces.Clear();workspaces.AddRange(store.Load());BindWorkspaces(workspaces.FirstOrDefault(w=>w.Id==id)??workspaces.FirstOrDefault());}catch{}
                return false;
            }
        }
        void CreateWorkspace()
        {
            var form=new FormDialog(Window,"New workspace","A workspace brings together the applications for one kind of work.");
            var name=form.Input("Workspace name","", "e.g. Coding, Research, Job search");
            form.Accept("Create workspace",()=>{if(String.IsNullOrWhiteSpace(name.Text)||name.Text.Trim().Length>100)return "Enter a name of 1–100 characters.";return null;});
            if(form.ShowDialog()!=true)return;
            selected=new Workspace{Name=name.Text.Trim()};workspaces.Add(selected);Save();
        }
        void WorkspaceMenu(Button button)
        {
            if(selected==null||engine.Busy)return;
            var menu=new ContextMenu();
            Menu(menu,"Workspace settings",()=>EditWorkspace());
            Menu(menu,"Duplicate",()=>{var copy=selected.Copy();workspaces.Add(copy);selected=copy;Save();});
            Menu(menu,"Delete workspace",()=>
            {
                if(MessageBox.Show(Window,"Delete “"+selected.Name+"”? Running applications will stay open, and this workspace will no longer be able to stop them.","Delete workspace",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
                workspaces.Remove(selected);selected=workspaces.FirstOrDefault();Save();
            });
            button.ContextMenu=menu;menu.PlacementTarget=button;menu.IsOpen=true;
        }
        static void Menu(ContextMenu menu,string title,Action action) {var item=new MenuItem{Header=title};item.Click+=(s,e)=>action();menu.Items.Add(item);}
        void EditWorkspace()
        {
            var form=new FormDialog(Window,"Workspace settings","Defaults apply to newly added applications. Existing applications keep their own rules.");
            var name=form.Input("Name",selected.Name,null);var mode=form.Choice("Default launch mode",Enum.GetNames(typeof(LaunchMode)),selected.DefaultMode.ToString());var delay=form.Input("Default minimum delay (seconds)",(selected.DefaultDelayMs/1000.0).ToString(),null);
            var failure=form.Choice("When an application fails",new[]{"Continue with the next application","Pause for retry or skip"},selected.PauseOnFailure?"Pause for retry or skip":"Continue with the next application");
            int ms=0;form.Accept("Save settings",()=>{if(String.IsNullOrWhiteSpace(name.Text)||name.Text.Trim().Length>100)return "Enter a name of 1–100 characters.";return ParseSeconds(delay.Text,0,300,out ms)?null:"Delay must be between 0 and 300 seconds.";});
            if(form.ShowDialog()!=true)return;
            selected.Name=name.Text.Trim();selected.DefaultMode=(LaunchMode)Enum.Parse(typeof(LaunchMode),(string)mode.SelectedItem);selected.DefaultDelayMs=ms;selected.PauseOnFailure=failure.SelectedIndex==1;Save();
        }
        async Task AddApplication()
        {
            if(selected==null||engine.Busy)return;
            var workspace=selected;
            var dialog=new AppPicker(Window,catalogue);
            dialog.Loaded+=async(s,e)=>
            {
                if(catalogue!=null)return;
                dialog.SetLoading(true);
                try{catalogue=await Task.Run(()=>Discovery.Scan());dialog.SetApps(catalogue);}catch(Exception ex){dialog.SetError(ex.Message);}
                finally{dialog.SetLoading(false);}
            };
            if(dialog.ShowDialog()!=true||dialog.Selection==null)return;
            var chosen=dialog.Selection;
            if(workspace.Apps.Any(a=>Rules.SamePath(a.Path,chosen.Path)&&a.Aumid==chosen.Aumid)){MessageBox.Show(Window,"This application is already in the workspace.","Already added");return;}
            workspace.Apps.Add(new AppEntry{Name=chosen.Name,Path=chosen.Path,Aumid=chosen.Aumid??"",Source=chosen.Source??"manual",IconReference=chosen.IconReference??"",ShortcutPath=chosen.ShortcutPath??"",Mode=workspace.DefaultMode,DelayMs=workspace.DefaultDelayMs,MaxWaitMs=Math.Max(30000,workspace.DefaultDelayMs)});Save();
            await Task.FromResult(0);
        }
        void AppMenu(Button button,AppEntry app)
        {
            if(engine.Busy){MessageBox.Show(Window,"Finish or cancel the launch sequence before editing.","Sequence in progress");return;}
            var menu=new ContextMenu();
            Menu(menu,"Launch settings & details",()=>EditApp(app));
            Menu(menu,"Choose application again…",async()=>await RepairApplication(app));
            if(app.CanStop)Menu(menu,"Force stop owned application",async()=>{if(MessageBox.Show(Window,"Force close only the processes opened by this workspace? Unsaved changes in those processes may be lost.","Force stop "+app.Name,MessageBoxButton.YesNo,MessageBoxImage.Warning)==MessageBoxResult.Yes)await RunAction(()=>engine.StopApplication(selected,app,true));});
            Menu(menu,app.Enabled?"Disable in workspace launch":"Enable in workspace launch",()=>{app.Enabled=!app.Enabled;Save();});
            int index=selected.Apps.IndexOf(app);
            if(index>0)Menu(menu,"Move up",()=>{selected.Apps.RemoveAt(index);selected.Apps.Insert(index-1,app);Save();});
            if(index<selected.Apps.Count-1)Menu(menu,"Move down",()=>{selected.Apps.RemoveAt(index);selected.Apps.Insert(index+1,app);Save();});
            Menu(menu,"Remove application",()=>{if(MessageBox.Show(Window,"Remove “"+app.Name+"” from this workspace? If it is running, it will stay open.","Remove application",MessageBoxButton.YesNo,MessageBoxImage.Question)==MessageBoxResult.Yes){selected.Apps.Remove(app);Save();}});
            button.ContextMenu=menu;menu.PlacementTarget=button;menu.IsOpen=true;
        }
        void EditApp(AppEntry app)
        {
            var form=new FormDialog(Window,app.Name,"Configure how this application opens. Minimum delay applies after the preceding launch.");
            form.Note(app.Path+(String.IsNullOrEmpty(app.Aumid)?"":"\nStore identity: "+app.Aumid));form.Note("Current state: "+app.StateLabel+"\n"+app.Detail);
            var name=form.Input("Display name",app.Name,null);var mode=form.Choice("Launch mode",Enum.GetNames(typeof(LaunchMode)),app.Mode.ToString());
            var delay=form.Input("Minimum delay (seconds)",(app.DelayMs/1000.0).ToString(),null);
            var advanced=new StackPanel();var expander=new Expander{Header="Advanced settings",Content=advanced,Margin=new Thickness(0,12,0,8)};form.Body.Children.Add(expander);
            var max=FormDialog.AddInput(advanced,"Maximum wait (seconds)",(app.MaxWaitMs/1000.0).ToString());var retry=FormDialog.AddInput(advanced,"Retries (0–5)",app.Retries.ToString());
            int ms=0,wait=0,count=0;form.Accept("Save launch settings",()=>
            {
                if(String.IsNullOrWhiteSpace(name.Text)||name.Text.Trim().Length>200)return "Enter a name of 1–200 characters.";
                if(!ParseSeconds(delay.Text,0,300,out ms))return "Minimum delay must be 0–300 seconds.";
                if(!ParseSeconds(max.Text,1,600,out wait)||wait<ms)return "Maximum wait must be 1–600 seconds and at least the minimum delay.";
                return Int32.TryParse(retry.Text,out count)&&count>=0&&count<=5?null:"Retries must be 0–5.";
            });
            if(form.ShowDialog()!=true)return;app.Name=name.Text.Trim();app.Mode=(LaunchMode)Enum.Parse(typeof(LaunchMode),(string)mode.SelectedItem);app.DelayMs=ms;app.MaxWaitMs=wait;app.Retries=count;Save();
        }
        async Task RepairApplication(AppEntry app)
        {
            if(app.CanStop){MessageBox.Show(Window,"Stop this owned application before changing its identity.","Application is running");return;}
            var picker=new AppPicker(Window,null);
            picker.Loaded+=async(s,e)=>{picker.SetLoading(true);try{catalogue=await Task.Run(()=>Discovery.Scan());picker.SetApps(catalogue);}catch(Exception ex){picker.SetError(ex.Message);}finally{picker.SetLoading(false);}};
            if(picker.ShowDialog()!=true||picker.Selection==null)return;
            var chosen=picker.Selection;
            if(selected.Apps.Any(a=>a!=app&&Rules.SamePath(a.Path,chosen.Path)&&a.Aumid==chosen.Aumid)){MessageBox.Show(Window,"That application is already in this workspace.","Already added");return;}
            app.Path=chosen.Path;app.Aumid=chosen.Aumid??"";app.Source=chosen.Source??"manual";app.IconReference=chosen.IconReference??"";app.ShortcutPath=chosen.ShortcutPath??"";app.Icon=null;app.State=AppState.Closed;app.Detail="Ready to launch";Save();await Refresh();
        }
        static bool ParseSeconds(string text,double min,double max,out int ms) {double n;ms=0;if(!Double.TryParse(text,out n)||Double.IsNaN(n)||n<min||n>max)return false;ms=(int)(n*1000);return true;}
        async Task AppAction(AppEntry app)
        {
            if(engine.Busy){MessageBox.Show(Window,"Finish or cancel the launch sequence before starting or stopping an individual app.","Sequence in progress");return;}
            await Refresh();
            if(app.State==AppState.Running||app.StopFailed){await RunAction(()=>engine.StopApplication(selected,app));return;}
            await RunAction(()=>engine.Launch(selected,app));
        }
        async Task Diagnostics()
        {
            var p=await platform.Sample();
            var form=new FormDialog(Window,"System diagnostics","These measurements are local and are not saved or transmitted.");
            using(var process=Process.GetCurrentProcess())form.Note("Orchestrator memory: "+(process.WorkingSet64/1048576.0).ToString("0.0")+" MB");
            form.Note("System pressure: "+p.Level+"\nCPU: "+(p.CpuAvailable?p.Cpu.ToString("0")+"%":"Unavailable")+"\nAvailable RAM: "+(p.MemoryAvailable?(p.AvailableBytes/1073741824.0).ToString("0.0")+" GB":"Unavailable")+"\nDisk activity: "+(p.DiskAvailable?p.Disk.ToString("0")+"%":"Unavailable; CPU and memory remain active"));
            form.Note("Process start events: "+(monitor.EventsAvailable?"Connected":"Unavailable; 5-second reconciliation active")+"\nProcess exits: event notifications with reconciliation\nIdle resource sampling: off\nProcess matching: full executable path + PID + start time");
            form.Accept("Done",()=>null);form.ShowDialog();
        }
        void Privacy()
        {
            var form=new FormDialog(Window,"Private by design","Everything needed to run your workspaces stays on this device.");
            form.Note("Stored locally: workspace names, application paths, launch order and timing rules. Session ownership lives in memory and resets when Orchestrator closes. After a restart, existing applications are protected.");
            form.Note("No accounts, telemetry, network access, credentials, browser history or document contents. Only configured applications are tracked; no unrelated process history is retained.");
            form.Note("Configuration folder:\n"+dataDirectory+"\n\nTo reset, close Orchestrator and remove this folder. Uninstall preserves your workspaces unless you choose the remove-data option.");
            form.Accept("Done",()=>null);form.ShowDialog();
        }
        public void Dispose() {if(disposed)return;disposed=true;timer.Stop();monitor.Dispose();platform.Dispose();theme.Dispose();if(tray!=null){tray.Visible=false;var icon=tray.Icon;var menu=tray.ContextMenuStrip;tray.Dispose();if(icon!=null)icon.Dispose();if(menu!=null)menu.Dispose();tray=null;}if(store!=null)store.Dispose();}
    }
    public class FormDialog : Window
    {
        public StackPanel Body { get; private set; }
        readonly StackPanel actions;
        readonly TextBlock error;
        public FormDialog(Window owner,string title,string description)
        {
            Owner=owner;Title=title;Width=550;SizeToContent=SizeToContent.Height;MaxHeight=760;ResizeMode=ResizeMode.NoResize;WindowStartupLocation=WindowStartupLocation.CenterOwner;Background=new SolidColorBrush(Color.FromRgb(247,249,252));FontFamily=new FontFamily("Segoe UI");FontSize=13;Foreground=new SolidColorBrush(Color.FromRgb(35,51,76));Resources=owner.Resources;
            SetResourceReference(BackgroundProperty,"BackgroundBrush");SetResourceReference(ForegroundProperty,"TextBrush");
            MaxHeight=Math.Min(MaxHeight,SystemParameters.WorkArea.Height-32);
            var dock=new DockPanel{Margin=new Thickness(26)};Content=dock;
            actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,20,0,0)};DockPanel.SetDock(actions,Dock.Bottom);dock.Children.Add(actions);
            error=new TextBlock{Foreground=Brushes.Firebrick,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0)};DockPanel.SetDock(error,Dock.Bottom);dock.Children.Add(error);
            Body=new StackPanel();dock.Children.Add(new ScrollViewer{Content=Body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,MaxHeight=590});
            Body.Children.Add(new TextBlock{Text=title,FontSize=24,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap});Note(description);
        }
        public void Note(string text){var note=new TextBlock{Text=text,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,10,0,10),FontSize=12};note.SetResourceReference(TextBlock.ForegroundProperty,"MutedBrush");Body.Children.Add(note);}
        public TextBox Input(string label,string text,string hint){var box=AddInput(Body,label,text);if(hint!=null)box.ToolTip=hint;return box;}
        public static TextBox AddInput(StackPanel body,string label,string text){body.Children.Add(new TextBlock{Text=label,Margin=new Thickness(0,13,0,7),FontWeight=FontWeights.SemiBold});var box=new TextBox{Text=text};body.Children.Add(box);return box;}
        public ComboBox Choice(string label,string[] choices,string selected){Body.Children.Add(new TextBlock{Text=label,Margin=new Thickness(0,13,0,7),FontWeight=FontWeights.SemiBold});var combo=new ComboBox{ItemsSource=choices,SelectedItem=selected};Body.Children.Add(combo);return combo;}
        public void Accept(string label,Func<string> validate)
        {
            var cancel=new Button{Content="Cancel",IsCancel=true};actions.Children.Add(cancel);
            var accept=new Button{Content=label,IsDefault=true,Style=(Style)Owner.FindResource("Primary"),Margin=new Thickness(0)};actions.Children.Add(accept);
            accept.Click+=(s,e)=>{string problem=validate();if(problem!=null){error.Text=problem;return;}DialogResult=true;};
        }
    }
    public sealed class AppPicker : FormDialog
    {
        List<DiscoveredApp> apps=new List<DiscoveredApp>();
        readonly ListBox list;
        readonly TextBox search;
        readonly TextBlock status;
        public DiscoveredApp Selection { get; private set; }
        public AppPicker(Window owner,List<DiscoveredApp> cached):base(owner,"Add an application","Find an installed app, or browse to a local executable. The executable identity is shown before you add it.")
        {
            Width=720;search=Input("Search applications","",null);search.TextChanged+=(s,e)=>Filter();
            status=new TextBlock{Margin=new Thickness(0,10,0,8),Foreground=Brushes.SlateGray};Body.Children.Add(status);
            list=new ListBox{Height=245,HorizontalContentAlignment=HorizontalAlignment.Stretch};
            list.ItemTemplate=(DataTemplate)XamlReader.Parse("<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><Grid Margin='6,8'><Grid.ColumnDefinitions><ColumnDefinition Width='44'/><ColumnDefinition Width='*'/></Grid.ColumnDefinitions><Image Source='{Binding Icon}' Width='32' Height='32'/><StackPanel Grid.Column='1'><TextBlock Text='{Binding Name}' FontWeight='SemiBold' FontSize='14'/><TextBlock Text='{Binding Kind}' FontSize='11' Opacity='.7' Margin='0,3,0,0'/><TextBlock Text='{Binding Path}' FontSize='10' Opacity='.55' TextTrimming='CharacterEllipsis' ToolTip='{Binding Path}'/></StackPanel></Grid></DataTemplate>");Body.Children.Add(list);
            var browse=new Button{Content="Browse for .exe…",HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,14,0,0)};Body.Children.Add(browse);
            browse.Click+=(s,e)=>{var picker=new OpenFileDialog{Filter="Application (*.exe)|*.exe",CheckFileExists=true,Title="Choose an application"};if(picker.ShowDialog(this)!=true)return;string error=Rules.ValidatePath(picker.FileName,true);if(error!=null){status.Text=error;return;}var entry=ApplicationMetadata.Manual(picker.FileName);apps.Insert(0,entry);search.Text="";Filter();list.SelectedItem=entry;};
            Accept("Add application",()=>{Selection=list.SelectedItem as DiscoveredApp;if(Selection==null)return "Select an application first.";return Rules.ValidatePath(Selection.Path,true);});
            if(cached!=null)SetApps(cached);
        }
        public void SetApps(List<DiscoveredApp> entries){apps=entries.ToList();Filter();}
        public void SetLoading(bool loading){if(loading)status.Text="Finding desktop and Microsoft Store applications…";else Filter();}
        public void SetError(string message){status.Text="Discovery unavailable: "+message+". Use Browse to add an executable.";}
        void Filter(){if(list==null)return;var found=apps.Where(a=>a.Name.IndexOf(search.Text,StringComparison.CurrentCultureIgnoreCase)>=0||a.Path.IndexOf(search.Text,StringComparison.CurrentCultureIgnoreCase)>=0).ToList();list.ItemsSource=found;status.Text=found.Count+" applications · launch arguments are not used";}
    }
}
