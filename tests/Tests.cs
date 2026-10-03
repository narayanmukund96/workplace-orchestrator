using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WorkplaceOrchestrator;

sealed class FakePlatform : IPlatform
{
    public long Clock;
    public int Attempts,Samples,Failures,NextPid=100;
    public readonly Dictionary<string,List<ProcessIdentity>> Processes=new Dictionary<string,List<ProcessIdentity>>();
    public readonly List<string> Launched=new List<string>();
    public readonly List<long> LaunchTimes=new List<long>();
    public readonly List<ProcessIdentity> Closed=new List<ProcessIdentity>();
    public Func<Pressure> Pressure=()=>new Pressure{Cpu=10,AvailableMemoryFraction=.6,AvailableBytes=8UL*1024*1024*1024,Disk=5};
    public Action<int> OnDelay;
    public Func<AppEntry,Task<ProcessIdentity>> LaunchOverride;
    public long Now {get{return Clock;}}
    public Task<List<ProcessIdentity>> Find(AppEntry app){List<ProcessIdentity> p;return Task.FromResult(Processes.TryGetValue(app.Path,out p)?p.ToList():new List<ProcessIdentity>());}
    public ProcessIdentity Add(AppEntry app,bool old=false){var p=new ProcessIdentity{Pid=NextPid++,StartedUtcTicks=DateTime.UtcNow.Ticks+(old?-TimeSpan.TicksPerMinute:1),Path=app.Path};List<ProcessIdentity> list;if(!Processes.TryGetValue(app.Path,out list)){list=new List<ProcessIdentity>();Processes[app.Path]=list;}list.Add(p);return p;}
    public Task<ProcessIdentity> Launch(AppEntry app){Attempts++;if(Failures-->0)throw new InvalidOperationException("Simulated launch failure");Launched.Add(app.Name);LaunchTimes.Add(Clock);return LaunchOverride==null?Task.FromResult(Add(app)):LaunchOverride(app);}
    public bool KeepOpen,VisibleWindows,Missing;
    public Task<bool> Close(ProcessIdentity id,bool force){Closed.Add(id);if(!KeepOpen||force)foreach(var list in Processes.Values)list.RemoveAll(p=>p.Key==id.Key);return Task.FromResult(true);}
    public Task<List<ProcessIdentity>> ExpandOwned(List<ProcessIdentity> known){var live=Processes.Values.SelectMany(p=>p).ToList();var owned=live.Where(p=>known.Any(k=>k.Key==p.Key)).ToList();bool changed;do{changed=false;foreach(var p in live)if(!owned.Contains(p)&&owned.Any(parent=>parent.Pid==p.ParentPid&&parent.StartedUtcTicks<=p.StartedUtcTicks)){owned.Add(p);changed=true;}}while(changed);return Task.FromResult(owned);}
    public async Task<bool> TerminateOwned(List<ProcessIdentity> roots,List<ProcessIdentity> owned){foreach(var id in (await ExpandOwned(roots.Concat(owned).ToList())).OrderByDescending(p=>p.StartedUtcTicks))await Close(id,true);return true;}
    public Task<bool> HasVisibleWindows(List<ProcessIdentity> owned){return Task.FromResult(VisibleWindows);}
    public bool Available(AppEntry app){return !Missing;}
    public void Forget(List<ProcessIdentity> roots){}
    public Task<Pressure> Sample(){Samples++;return Task.FromResult(Pressure());}
    public Task Delay(int ms,CancellationToken token){token.ThrowIfCancellationRequested();Clock+=ms;if(OnDelay!=null)OnDelay(ms);return Task.FromResult(0);}
}
static class Tests
{
    static int passed,failed;
    static void Assert(bool condition,string message){if(!condition)throw new Exception(message);}
    static AppEntry App(string name){return new AppEntry{Name=name,Path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),name+".exe"),Retries=0};}
    static Workspace Work(params AppEntry[] apps){return new Workspace{Name="Test workspace",Apps=apps.ToList()};}
    static async Task Test(string name,Func<Task> run){try{await run();Console.WriteLine("PASS "+name);passed++;}catch(Exception ex){Console.WriteLine("FAIL "+name+": "+ex);failed++;}}
    static Task Sync(Action action){action();return Task.FromResult(0);}
    static async Task Suite(string output)
    {
        await Test("Pressure uses CPU, RAM, disk and critical memory",()=>Sync(()=>
        {
            Assert(new Pressure{Cpu=20,Disk=10,AvailableMemoryFraction=.5}.Level==PressureLevel.Healthy,"healthy");
            Assert(new Pressure{Cpu=95}.Level==PressureLevel.High,"cpu");
            Assert(new Pressure{Disk=98}.Level==PressureLevel.High,"disk");
            Assert(new Pressure{AvailableMemoryFraction=.08}.Level==PressureLevel.High,"memory");
            Assert(new Pressure{AvailableBytes=100*1024*1024}.Critical,"critical bytes");
            Assert(new Pressure{Cpu=70,Disk=70,AvailableMemoryFraction=.25}.Level==PressureLevel.Moderate,"weighted moderate");
        }));
        await Test("Executable validation rejects URLs, scripts, UNC and alternate streams",()=>Sync(()=>
        {
            foreach(var p in new[]{"https://example.com/app.exe",@"\\server\app.exe","app.exe",@"C:\a.cmd",@"C:\test.exe:evil.exe","C:\\a.exe\n"})Assert(Rules.ValidatePath(p,false)!=null,p);
            Assert(Rules.ValidatePath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"notepad.exe"),true)==null,"valid application");
            Assert(Rules.ValidatePath(@"C:\missing-unique-67281.exe",true)!=null,"deleted app");
        }));
        await Test("SQLite round-trip, order, Unicode, rename and duplication",()=>Sync(()=>
        {
            string db=Path.Combine(output,"roundtrip.db");var a=App("first");var b=App("second");b.Mode=LaunchMode.Timed;b.DelayMs=4567;b.MaxWaitMs=12000;b.Retries=3;b.Enabled=false;
            var w=Work(b,a);w.Name="Focus 'quoted' — 研究";w.PauseOnFailure=true;var copy=w.Copy();
            Assert(copy.Id!=w.Id&&copy.Apps[0].Id!=b.Id,"fresh UUIDs");
            using(var store=new Store(db))store.Save(new List<Workspace>{w,copy});
            using(var store=new Store(db))
            {
                var loaded=store.Load();Assert(loaded.Count==2&&loaded[0].Name==w.Name,"workspace persistence");Assert(loaded[0].Apps[0].Name=="second"&&loaded[0].Apps[1].Name=="first","order");
                var entry=loaded[0].Apps[0];Assert(entry.Retries==3&&entry.DelayMs==4567&&entry.MaxWaitMs==12000&&!entry.Enabled&&entry.Mode==LaunchMode.Timed,"rules");
                loaded.RemoveAt(1);loaded[0].Name="Renamed";loaded[0].Apps.RemoveAt(0);store.Save(loaded);
                Assert(store.Load()[0].Name=="Renamed"&&store.Load()[0].Apps.Count==1,"rename remove delete");
                loaded[0].Apps[0].Path="https://malicious/app.exe";
                try{store.Save(loaded);throw new Exception("invalid config saved");}catch(InvalidDataException){}
                Assert(store.Load()[0].Apps[0].Path!="https://malicious/app.exe","last valid state preserved");
            }
        }));
        await Test("Database transaction rolls back duplicate application IDs",()=>Sync(()=>
        {
            using(var store=new Store(Path.Combine(output,"transaction.db")))
            {
                var w=Work(App("one"));store.Save(new List<Workspace>{w});var copy=w.Copy();copy.Apps[0].Id=w.Apps[0].Id;
                bool rejected=false;try{store.Save(new List<Workspace>{w,copy});}catch(IOException){rejected=true;}
                Assert(rejected&&store.Load().Count==1,"transaction rollback");
            }
        }));
        await Test("Corrupted databases are rejected without resetting the file",()=>Sync(()=>
        {
            string file=Path.Combine(output,"corrupt.db");byte[] original=System.Text.Encoding.UTF8.GetBytes("invalid database content that must be preserved");File.WriteAllBytes(file,original);
            bool rejected=false;try{using(var store=new Store(file))store.Load();}catch(IOException){rejected=true;}
            Assert(rejected&&File.ReadAllBytes(file).SequenceEqual(original),"corrupt database preserved");
        }));
        await Test("Newer schema is rejected without dropping saved workspaces",()=>Sync(()=>
        {
            string file=Path.Combine(output,"future.db");
            using(var store=new Store(file)){store.Save(new List<Workspace>{Work(App("one"))});typeof(Store).GetMethod("Run",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).Invoke(store,new object[]{"PRAGMA user_version=99",new string[0]});}
            bool rejected=false;try{using(var store=new Store(file))store.Load();}catch(InvalidDataException){rejected=true;}
            Assert(rejected&&new FileInfo(file).Length>0,"future schema preserved");
        }));
        await Test("Tampered stored executable is rejected on load",()=>Sync(()=>
        {
            using(var store=new Store(Path.Combine(output,"tampered.db")))
            {
                store.Save(new List<Workspace>{Work(App("one"))});typeof(Store).GetMethod("Run",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).Invoke(store,new object[]{"UPDATE apps SET path=?",new string[]{"https://example.com/evil.exe"}});
                bool rejected=false;try{store.Load();}catch(InvalidDataException){rejected=true;}Assert(rejected,"tampered path rejected");
            }
        }));
        await Test("V1 database migrates without losing workspace IDs, order, rules or names",()=>Sync(()=>
        {
            string file=Path.Combine(output,"migration.db");var w=Work(App("one"),App("two"));w.Apps[0].DelayMs=4321;w.Apps[0].Aumid="Test_family!App";
            using(var store=new Store(file))
            {
                store.Save(new List<Workspace>{w});var sql=typeof(Store).GetMethod("Run",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
                foreach(var statement in new[]{"CREATE TABLE apps_v1 AS SELECT id,workspace,name,path,aumid,position,mode,delay,maxwait,retries,enabled FROM apps","DROP TABLE apps","ALTER TABLE apps_v1 RENAME TO apps","DROP TABLE preferences","PRAGMA user_version=1"})sql.Invoke(store,new object[]{statement,new string[0]});
            }
            using(var store=new Store(file)){var loaded=store.Load().Single();Assert(loaded.Id==w.Id&&loaded.Apps[0].Id==w.Apps[0].Id&&loaded.Apps[0].DelayMs==4321&&loaded.Apps[1].Name=="two","preserved data");Assert(loaded.Apps[0].Source=="packaged"&&loaded.Apps[1].Source=="manual","identity defaults");var pref=store.LoadPreferences();Assert(pref.Theme==Appearance.System&&!pref.StartWithWindows&&pref.CloseToTray,"safe preferences");pref.Theme=Appearance.Dark;pref.CloseToTray=false;store.SavePreferences(pref);}
            using(var store=new Store(file)){Assert(store.LoadPreferences().Theme==Appearance.Dark&&!store.LoadPreferences().CloseToTray&&store.Load().Count==1,"preferences persisted independently");}
        }));
        await Test("Application identity metadata survives persistence and duplicate",()=>Sync(()=>
        {
            var a=App("one");a.Source="start-menu";a.IconReference=@"C:\Applications\one.ico";a.ShortcutPath=@"C:\Menu\one.lnk";using(var store=new Store(Path.Combine(output,"identity.db"))){store.Save(new List<Workspace>{Work(a)});var copy=store.Load()[0].Copy().Apps[0];Assert(copy.Source==a.Source&&copy.IconReference==a.IconReference&&copy.ShortcutPath==a.ShortcutPath,"richer identity");}
        }));
        await Test("Individual stop protects pre-existing processes",async()=>
        {
            var p=new FakePlatform();var a=App("one");p.Add(a,true);var w=Work(a);var e=new Engine(p);await e.Launch(w);await e.StopApplication(w,a);Assert(p.Closed.Count==0&&p.Processes[a.Path].Count==1,"external session protected");
        });
        await Test("Individual stop reports Stopping then Closed",async()=>
        {
            var p=new FakePlatform();var a=App("one");var w=Work(a);var e=new Engine(p);await e.Launch(w);bool stopping=false;a.PropertyChanged+=(s,args)=>{if(a.State==AppState.Stopping)stopping=true;};await e.StopApplication(w,a);Assert(stopping&&a.State==AppState.Closed&&!a.CanStop,"complete stop state");
        });
        await Test("Stop closes verified descendants with a different executable",async()=>
        {
            var p=new FakePlatform();var a=App("root");var w=Work(a);var e=new Engine(p);await e.Launch(w);var root=p.Processes[a.Path][0];var child=p.Add(App("worker"));child.ParentPid=root.Pid;await e.Refresh(new[]{w});await e.StopApplication(w,a);Assert(p.Closed.Any(id=>id.Key==child.Key)&&a.State==AppState.Closed,"child attributed and stopped");
        });
        await Test("Owned background processes receive bounded termination fallback",async()=>
        {
            var p=new FakePlatform{KeepOpen=true};var a=App("root");var w=Work(a);var e=new Engine(p);await e.Launch(w);await e.StopApplication(w,a);Assert(p.Clock>=8000&&p.Clock<=10500&&a.State==AppState.Closed,"bounded fallback");
        });
        await Test("Visible save prompts survive timeout until explicit force stop",async()=>
        {
            var p=new FakePlatform{KeepOpen=true,VisibleWindows=true};var a=App("root");var w=Work(a);var e=new Engine(p);await e.Launch(w);await e.StopApplication(w,a);Assert(a.State==AppState.Failed&&a.StopFailed&&p.Processes[a.Path].Count==1,"visible window preserved");await e.Refresh(new[]{w});Assert(a.State==AppState.Failed,"failure persists while open");await e.StopApplication(w,a,true);Assert(a.State==AppState.Closed&&p.Processes[a.Path].Count==0,"explicit force closes owned process");
        });
        await Test("Missing executable is reflected without launching or reselecting",async()=>
        {
            var p=new FakePlatform{Missing=true};var a=App("gone");await new Engine(p).Refresh(new[]{Work(a)});Assert(a.State==AppState.Failed&&a.Detail.Contains("missing"),"missing app state");
        });
        await Test("Reused process ID never inherits session ownership",async()=>
        {
            var p=new FakePlatform();var a=App("root");var w=Work(a);var e=new Engine(p);await e.Launch(w);var old=p.Processes[a.Path][0];p.Processes[a.Path].Clear();var replacement=p.Add(a);replacement.Pid=old.Pid;replacement.StartedUtcTicks=old.StartedUtcTicks+100;await e.StopApplication(w,a);Assert(p.Closed.Count==0&&p.Processes[a.Path].Count==1,"PID reuse protected");
        });
        await Test("Pre-existing applications never acquire workspace ownership",async()=>
        {
            var p=new FakePlatform();var a=App("existing");var b=App("new");p.Add(a,true);var w=Work(a,b);var engine=new Engine(p);await engine.Launch(w);await engine.StopWorkspace(w);
            Assert(p.Launched.SequenceEqual(new[]{"new"}),"no duplicate");Assert(engine.SessionFor(w).PreExisting.Contains(a.Id),"preexisting flag");Assert(p.Closed.Count==1&&p.Closed[0].Path==b.Path,"safe stop");
        });
        await Test("Timed mode follows interval despite high CPU",async()=>
        {
            var p=new FakePlatform();p.Pressure=()=>new Pressure{Cpu=98};var a=App("one");var b=App("two");a.Mode=b.Mode=LaunchMode.Timed;b.DelayMs=2400;
            await new Engine(p).Launch(Work(a,b));Assert(p.LaunchTimes[0]==0&&p.LaunchTimes[1]>=2400&&p.LaunchTimes[1]<4000,"timed sequence");
        });
        await Test("Smart mode waits for CPU pressure to clear",async()=>
        {
            var p=new FakePlatform();p.Pressure=()=>new Pressure{Cpu=p.Clock<3000?99:15};var a=App("one");a.Mode=LaunchMode.Smart;
            await new Engine(p).Launch(Work(a));Assert(p.LaunchTimes[0]>=3000,"smart resource wait");
        });
        await Test("Hybrid respects minimum delay and disk pressure",async()=>
        {
            var p=new FakePlatform();p.Pressure=()=>new Pressure{Disk=p.Clock>0&&p.Clock<4000?99:5};var a=App("one");var b=App("two");b.DelayMs=2000;
            await new Engine(p).Launch(Work(a,b));Assert(p.LaunchTimes[1]>=4000,"hybrid dual gate");
        });
        await Test("Maximum wait provides bounded continuity",async()=>
        {
            var p=new FakePlatform();p.Pressure=()=>new Pressure{Cpu=99};var a=App("one");a.MaxWaitMs=3000;var engine=new Engine(p);bool warned=false;engine.Changed+=()=>{if(engine.Message.Contains("Maximum wait"))warned=true;};
            await engine.Launch(Work(a));Assert(p.LaunchTimes.Single()==3000&&warned,"bounded fallback warning");
        });
        await Test("Critical memory prevents launch even in immediate mode",async()=>
        {
            var p=new FakePlatform();p.Pressure=()=>new Pressure{AvailableMemoryFraction=.01};var a=App("one");a.Mode=LaunchMode.Immediate;a.MaxWaitMs=2000;
            await new Engine(p).Launch(Work(a));Assert(p.Launched.Count==0&&a.State==AppState.Failed&&p.Clock==2000,"critical memory skip");
        });
        await Test("Immediate mode skips settling and ordinary pressure",async()=>
        {
            var p=new FakePlatform();p.Pressure=()=>new Pressure{Cpu=99};var a=App("one");var b=App("two");a.Mode=b.Mode=LaunchMode.Immediate;
            await new Engine(p).Launch(Work(a,b));Assert(p.LaunchTimes.All(t=>t==0),"immediate");
        });
        await Test("Launch failure retries and succeeds",async()=>
        {
            var p=new FakePlatform{Failures=1};var a=App("one");a.Retries=1;await new Engine(p).Launch(Work(a));Assert(p.Attempts==2&&a.State==AppState.Running,"retry success");
        });
        await Test("Exhausted retry continues to next application",async()=>
        {
            var p=new FakePlatform{Failures=2};var a=App("one");a.Retries=1;var b=App("two");await new Engine(p).Launch(Work(a,b));Assert(a.State==AppState.Failed&&b.State==AppState.Running&&p.Attempts==3,"continue failure policy");
        });
        await Test("Pause policy can skip a failed app",async()=>
        {
            var p=new FakePlatform{Failures=1};var a=App("one");var b=App("two");var w=Work(a,b);w.PauseOnFailure=true;var engine=new Engine(p);var run=engine.Launch(w);
            Assert(engine.Paused&&!run.IsCompleted,"pause");engine.Skip();await run;Assert(b.State==AppState.Running,"resume after skip");
        });
        await Test("Pause policy retry resumes the same application",async()=>
        {
            var p=new FakePlatform{Failures=1};var a=App("one");var w=Work(a);w.PauseOnFailure=true;var engine=new Engine(p);var run=engine.Launch(w);engine.RetryPaused();await run;Assert(a.State==AppState.Running&&p.Attempts==2,"pause retry");
        });
        await Test("Cancellation unblocks a paused launch",async()=>
        {
            var p=new FakePlatform{Failures=1};var a=App("one");var b=App("two");var w=Work(a,b);w.PauseOnFailure=true;var engine=new Engine(p);var run=engine.Launch(w);await engine.Cancel();await run;Assert(!engine.Busy&&b.State==AppState.Closed,"cancel queue");
        });
        await Test("External launch during settling is not duplicated or owned",async()=>
        {
            var p=new FakePlatform();var a=App("one");var b=App("two");bool inserted=false;p.OnDelay=ms=>{if(!inserted){p.Add(b,true);inserted=true;}};var w=Work(a,b);var engine=new Engine(p);await engine.Launch(w);await engine.StopWorkspace(w);
            Assert(p.Launched.Count==1&&p.Closed.All(id=>id.Path!=b.Path),"external launch safe");
        });
        await Test("Single-instance activation with old identity is never owned",async()=>
        {
            var p=new FakePlatform();var a=App("one");p.LaunchOverride=app=>Task.FromResult(p.Add(app,true));var w=Work(a);var engine=new Engine(p);await engine.Launch(w);await engine.StopWorkspace(w);Assert(p.Closed.Count==0,"old PID protected");
        });
        await Test("Cancellation during launch retains ownership for safe stop",async()=>
        {
            var p=new FakePlatform();var a=App("one");var pending=new TaskCompletionSource<ProcessIdentity>();p.LaunchOverride=app=>pending.Task;var w=Work(a);var engine=new Engine(p);var launch=engine.Launch(w);var stop=engine.StopWorkspace(w);pending.SetResult(p.Add(a));await launch;await stop;Assert(p.Closed.Count==1,"in-flight ownership captured");
        });
        await Test("Disabled applications skipped; explicit single start works",async()=>
        {
            var p=new FakePlatform();var a=App("one");a.Enabled=false;var w=Work(a);var engine=new Engine(p);await engine.Launch(w);Assert(p.Attempts==0,"disabled");await engine.Launch(w,a);Assert(p.Attempts==1,"single start");
        });
        await Test("Repeat launch preserves earlier session ownership",async()=>
        {
            var p=new FakePlatform();var a=App("one");var w=Work(a);var engine=new Engine(p);await engine.Launch(w);await engine.Launch(w);await engine.StopWorkspace(w);Assert(p.Attempts==1&&p.Closed.Count==1,"owned process retained");
        });
        await Test("Shared applications are protected in a second workspace",async()=>
        {
            var p=new FakePlatform();var a=App("one");var w1=Work(a);var w2=Work(a.Copy());var engine=new Engine(p);await engine.Launch(w1);await engine.Launch(w2);await engine.StopWorkspace(w2);Assert(p.Closed.Count==0,"second workspace not owner");await engine.StopWorkspace(w1);Assert(p.Closed.Count==1,"first workspace owner");
        });
        await Test("External closure and abnormal exit update live state",async()=>
        {
            var p=new FakePlatform();var a=App("one");var w=Work(a);var engine=new Engine(p);var id=p.Add(a);await engine.Refresh(new[]{w});Assert(a.State==AppState.Running,"external launch");p.Processes.Clear();await engine.Refresh(new[]{w});Assert(a.State==AppState.Closed,"external close");id=p.Add(a);await engine.Refresh(new[]{w});engine.ReportExit(id.Key,1);p.Processes.Clear();await engine.Refresh(new[]{w});Assert(a.State==AppState.StoppedUnexpectedly,"abnormal exit");
        });
        await Test("A second workspace cannot start a concurrent sequence",async()=>
        {
            var p=new FakePlatform{Failures=1};var w=Work(App("one"));w.PauseOnFailure=true;var engine=new Engine(p);var run=engine.Launch(w);bool rejected=false;try{await engine.Launch(Work(App("two")));}catch(InvalidOperationException){rejected=true;}await engine.Cancel();await run;Assert(rejected,"serialized launches");
        });
    }
    static async Task Native(string output,string helper)
    {
        await Test("Windows resource signals return bounded measurements",async()=>
        {
            using(var p=new WindowsPlatform()){await Task.Delay(300);var s=await p.Sample();Assert(s.Cpu>=0&&s.Cpu<=100&&s.AvailableBytes>0,"resource sample");Console.WriteLine("  CPU="+s.Cpu.ToString("0.0")+"; memory available="+(s.AvailableBytes/1048576)+" MB; disk available="+s.DiskAvailable);}
        });
        await Test("Real process identity, PID reuse protection, close and exit event",async()=>
        {
            using(var platform=new WindowsPlatform())using(var monitor=new ProcessMonitor())
            {
                var app=new AppEntry{Name="Controlled test app",Path=helper};ProcessIdentity launched=null;
                try
                {
                    var exit=new TaskCompletionSource<int>();monitor.Exited+=(key,code)=>exit.TrySetResult(code);monitor.Configure(new[]{app});
                    launched=await platform.Launch(app);Assert(launched!=null,"launched identity");
                    await Task.Delay(800);var found=await platform.Find(app);Assert(found.Any(p=>p.Key==launched.Key),"exact path discovery");monitor.Observe(found);
                    var impostor=new ProcessIdentity{Pid=launched.Pid,Path=launched.Path,StartedUtcTicks=launched.StartedUtcTicks+1};Assert(!await platform.Close(impostor,true),"start-time mismatch protected");
                    Assert(await platform.Close(launched,false),"graceful close request");var winner=await Task.WhenAny(exit.Task,Task.Delay(5000));Assert(winner==exit.Task&&exit.Task.Result==0,"normal process exit event");
                    Assert((await platform.Find(app)).Count==0,"closed reflected");
                    Console.WriteLine("  WMI start events available="+monitor.EventsAvailable);
                }
                finally{if(launched!=null)platform.Close(launched,true).GetAwaiter().GetResult();}
            }
        });
        await Test("Installed application discovery resolves valid executables",()=>Sync(()=>
        {
            var watch=Stopwatch.StartNew();var apps=Discovery.Scan();Assert(apps.Count>0,"discover applications");Assert(apps.All(a=>Rules.ValidatePath(a.Path,true)==null),"only valid apps");
            var packaged=apps.Where(a=>!String.IsNullOrEmpty(a.Aumid)).ToList();foreach(var a in packaged)Assert(PackageIdentity.Matches(a.Aumid,a.Path),"registered identity: "+a.Name);
            if(packaged.Count>0)Assert(!PackageIdentity.Matches(packaged[0].Aumid,helper),"Store identity/executable tampering rejected");
            Assert(apps.All(a=>!a.Path.Contains("OrchestratorControlledTest")),"no test executables in catalogue");
            var manual=ApplicationMetadata.Manual(helper);Assert(!String.IsNullOrWhiteSpace(manual.Name)&&manual.Icon!=null,"manual metadata/icon fallback");
            foreach(var a in apps.Take(10).Concat(packaged.Take(5)))Assert(a.Icon!=null,"resolved application icon");
            Console.WriteLine("  Discovered="+apps.Count+"; Store="+packaged.Count+"; registered identities verified="+packaged.Count+"; elapsed="+watch.ElapsedMilliseconds+"ms");
        }));
        await Test("Packaged application refresh replaces and persists a stale versioned path",async()=>
        {
            var packaged=Discovery.Scan().Where(a=>!String.IsNullOrEmpty(a.Aumid)).ToList();
            var selected=packaged.FirstOrDefault(a=>a.Name.IndexOf("Claude",StringComparison.OrdinalIgnoreCase)>=0)??packaged.FirstOrDefault();
            Assert(selected!=null,"a packaged application is required");
            string packageRoot=Path.GetDirectoryName(selected.Path);
            while(packageRoot!=null&&!File.Exists(Path.Combine(packageRoot,"AppxManifest.xml")))packageRoot=Path.GetDirectoryName(packageRoot);
            Assert(packageRoot!=null,"package root");
            string relative=selected.Path.Substring(packageRoot.Length).TrimStart(Path.DirectorySeparatorChar);
            string stale=Path.Combine(Path.GetDirectoryName(packageRoot),Path.GetFileName(packageRoot)+".stale",relative);
            Assert(!File.Exists(stale),"simulated path is stale");
            var app=new AppEntry{Name=selected.Name,Path=stale,Aumid=selected.Aumid,Source="packaged"};
            using(var platform=new WindowsPlatform())
            {
                await platform.Find(app);
                Assert(Rules.SamePath(app.Path,selected.Path)&&PackageIdentity.Matches(app.Aumid,app.Path),"refresh resolved current package executable");
            }
            string database=Path.Combine(output,"package-path-refresh.db");var workspace=Work(app);
            using(var store=new Store(database))store.Save(new List<Workspace>{workspace});
            using(var store=new Store(database))Assert(Rules.SamePath(store.Load().Single().Apps.Single().Path,selected.Path),"refreshed path persisted");
            Console.WriteLine("  Packaged app="+selected.Name+"; stale path refreshed to="+selected.Path);
        });
        await Test("Native multiprocess app stops root and orphaned background child",async()=>
        {
            using(var platform=new WindowsPlatform())
            {
                var a=new AppEntry{Name="Controlled process tree",Path=helper,Mode=LaunchMode.Immediate};var w=Work(a);var e=new Engine(platform);
                Environment.SetEnvironmentVariable("WORKPLACE_TEST_TREE","1");
                try
                {
                    await e.Launch(w);await Task.Delay(1000);await e.Refresh(new[]{w});Assert(e.Current(a).Count>=2,"root and child tracked");
                    var before=e.Current(a).ToList();await e.StopApplication(w,a);Assert(a.State==AppState.Closed,"app group closed");Assert(before.All(p=>ProcessGroups.Live(p)==null),"root and child exited");
                }
                finally{Environment.SetEnvironmentVariable("WORKPLACE_TEST_TREE",null);var s=e.SessionFor(w);if(s!=null){List<ProcessIdentity> roots;if(s.Roots.TryGetValue(a.Id,out roots))platform.TerminateOwned(roots,e.Current(a)).GetAwaiter().GetResult();}}
            }
        });
    }
    public static int Main(string[] args)
    {
        string output=args[0];Directory.CreateDirectory(output);
        if(args.Length>1)Native(output,args[1]).GetAwaiter().GetResult();else Suite(output).GetAwaiter().GetResult();
        Console.WriteLine("RESULT "+passed+" passed; "+failed+" failed");return failed==0?0:1;
    }
}
