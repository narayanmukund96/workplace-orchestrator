using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace WorkplaceOrchestrator
{
    // Called on the UI synchronization context; platform operations run away from the dispatcher.
    public sealed class Engine
    {
        readonly IPlatform platform;
        readonly Dictionary<string,Session> sessions=new Dictionary<string,Session>();
        readonly Dictionary<string,List<ProcessIdentity>> current=new Dictionary<string,List<ProcessIdentity>>();
        readonly HashSet<string> unexpected=new HashSet<string>();
        readonly HashSet<string> requestedStops=new HashSet<string>();
        CancellationTokenSource cancellation;
        Task activeTask;
        TaskCompletionSource<bool> pause;
        bool skip;
        bool waitLimitReached;
        int stopping;
        public Workspace ActiveWorkspace { get; private set; }
        public AppEntry ActiveApp { get; private set; }
        public bool Busy { get { return ActiveWorkspace!=null||stopping>0; } }
        public bool Paused { get { return pause!=null; } }
        public string Message { get; private set; }
        public Pressure LastPressure { get; private set; }
        public event Action Changed;
        public Engine(IPlatform platform) { this.platform=platform;Message="Ready when you are"; }
        void Notify(string message) { Message=message;if(Changed!=null)Changed(); }
        public Session SessionFor(Workspace workspace) { Session s;return sessions.TryGetValue(workspace.Id,out s)?s:null; }
        public List<ProcessIdentity> Current(AppEntry app) { List<ProcessIdentity> p;return current.TryGetValue(app.Id,out p)?p:new List<ProcessIdentity>(); }
        public IEnumerable<ProcessIdentity> AllCurrent { get { return current.Values.SelectMany(v=>v).GroupBy(p=>p.Key).Select(g=>g.First()).ToArray(); } }
        public void ReportExit(string key,int exitCode) { if(exitCode!=0 && !requestedStops.Contains(key))unexpected.Add(key); }
        public async Task Refresh(IEnumerable<Workspace> workspaces,bool prune=true)
        {
            var apps=workspaces.SelectMany(w=>w.Apps).ToArray();
            var identities=new Dictionary<string,List<ProcessIdentity>>(StringComparer.OrdinalIgnoreCase);
            foreach(var app in apps)
            {
                var workspace=workspaces.First(w=>w.Apps.Contains(app));
                var owned=await OwnedNow(workspace,app);
                List<ProcessIdentity> found;
                if(!identities.TryGetValue(app.Path,out found)) { found=await platform.Find(app);identities[app.Path]=found; }
                found=found.Concat(owned).GroupBy(p=>p.Key).Select(g=>g.First()).ToList();
                var old=Current(app); current[app.Id]=found;
                app.CanStop=owned.Count>0;
                if(app==ActiveApp || app.State==AppState.Queued || app.State==AppState.Stopping)continue;
                if(found.Count>0)
                {
                    if(app.StopFailed&&owned.Count>0){app.State=AppState.Failed;app.Changed();continue;}
                    app.State=AppState.Running;app.Detail=app.CanStop?"Opened by this workspace · "+owned.Count+" process(es)":"Opened outside this workspace · protected";
                }
                else if(old.Count>0)
                {
                    bool crashed=old.Any(p=>unexpected.Contains(p.Key));
                    app.State=crashed?AppState.StoppedUnexpectedly:AppState.Closed;
                    app.Detail=crashed?"The application exited with an error. Retry when ready.":"Closed outside the workspace or stopped normally";
                    app.StopFailed=false;
                }
                else if(!platform.Available(app)){app.State=AppState.Failed;app.Detail="Application missing. Use Choose application again in the row menu.";}
                else if(app.StopFailed){app.StopFailed=false;app.State=AppState.Closed;app.Detail="Closed";}
            }
            var retained=new HashSet<string>(apps.Select(a=>a.Id));
            if(prune)foreach(var key in current.Keys.Where(k=>!retained.Contains(k)).ToArray())current.Remove(key);
            var live=new HashSet<string>(AllCurrent.Select(p=>p.Key));
            unexpected.RemoveWhere(k=>!live.Contains(k));requestedStops.RemoveWhere(k=>!live.Contains(k));
            if(Changed!=null)Changed();
        }
        async Task<List<ProcessIdentity>> OwnedNow(Workspace workspace,AppEntry app)
        {
            var s=SessionFor(workspace);List<ProcessIdentity> roots,owned;
            if(s==null||!s.Roots.TryGetValue(app.Id,out roots))return new List<ProcessIdentity>();
            if(!s.Owned.TryGetValue(app.Id,out owned))owned=new List<ProcessIdentity>();
            var live=await platform.ExpandOwned(roots.Concat(owned).ToList());s.Owned[app.Id]=live;
            if(live.Count==0&&!Busy){platform.Forget(roots);s.Roots.Remove(app.Id);}
            return live;
        }
        public Task Launch(Workspace workspace,AppEntry single=null)
        {
            if(Busy)throw new InvalidOperationException("Wait for the current sequence to finish, or cancel it first.");
            Rules.Validate(workspace);
            ActiveWorkspace=workspace;cancellation=new CancellationTokenSource();
            activeTask=Run(workspace,single,cancellation.Token);
            return activeTask;
        }
        async Task Run(Workspace workspace,AppEntry single,CancellationToken token)
        {
            var selected=single==null?workspace.Apps.Where(a=>a.Enabled).ToList():new List<AppEntry>{single};
            Session session;
            if(!sessions.TryGetValue(workspace.Id,out session)) {session=new Session{WorkspaceId=workspace.Id};sessions.Add(workspace.Id,session);}
            session.Status="Launching";
            int pressureWarnings=0;
            try
            {
                // Snapshot before any launch, including apps farther down the queue.
                foreach(var app in selected)
                {
                    var existing=await platform.Find(app); token.ThrowIfCancellationRequested();current[app.Id]=existing;
                    if(existing.Count>0)
                    {
                        List<ProcessIdentity> owned;
                        bool isOwned=session.Owned.TryGetValue(app.Id,out owned) && existing.Any(p=>owned.Any(o=>o.Key==p.Key));
                        if(!isOwned)session.PreExisting.Add(app.Id);
                        app.State=AppState.Running;app.Detail=isOwned?"Running · opened by this workspace":"Already running · left open on workspace stop";
                    }
                    else { app.State=AppState.Queued;app.Detail="In the launch queue"; }
                }
                long? previousLaunch=null;
                foreach(var app in selected)
                {
                    token.ThrowIfCancellationRequested();
                    if(app.State==AppState.Running)continue;
                    ActiveApp=app;skip=false;
                    bool again;
                    do
                    {
                        again=false;
                        waitLimitReached=false;
                        bool ready=await WaitUntilReady(app,previousLaunch,token);
                        if(waitLimitReached)pressureWarnings++;
                        token.ThrowIfCancellationRequested();
                        if(skip){app.State=AppState.Closed;app.Detail="Skipped";break;}
                        if(!ready)
                        {
                            app.State=AppState.Failed;app.Detail="Memory is critically low. Skipped to protect responsiveness.";
                        }
                        else
                        {
                            // Catch external launches that happened while queued, including another workspace's apps.
                            var existing=await platform.Find(app);token.ThrowIfCancellationRequested();
                            if(existing.Count>0) {current[app.Id]=existing;session.PreExisting.Add(app.Id);app.State=AppState.Running;app.Detail="Already running · not owned by this session";break;}
                            for(int attempt=0;attempt<=app.Retries;attempt++)
                            {
                                try
                                {
                                    token.ThrowIfCancellationRequested();
                                    // Retry must recheck state: a slow bootstrapper may have succeeded after a timeout.
                                    existing=await platform.Find(app);token.ThrowIfCancellationRequested();
                                    if(existing.Count>0) {current[app.Id]=existing;app.State=AppState.Running;app.Detail="Running · ownership not assumed";break;}
                                    app.State=AppState.Launching;app.Detail=attempt==0?"Opening application":"Retry "+attempt+" of "+app.Retries;
                                    Notify("Opening "+app.Name);
                                    long before=DateTime.UtcNow.Ticks;
                                    var identity=await platform.Launch(app);
                                    previousLaunch=platform.Now;
                                    // Register ownership before observing cancellation so a stop during launch remains safe.
                                    if(identity!=null && identity.StartedUtcTicks>=before && Rules.SamePath(identity.Path,app.Path))
                                    {
                                        List<ProcessIdentity> owned;
                                        if(!session.Owned.TryGetValue(app.Id,out owned)){owned=new List<ProcessIdentity>();session.Owned.Add(app.Id,owned);}
                                        if(!owned.Any(p=>p.Key==identity.Key))owned.Add(identity);
                                        List<ProcessIdentity> roots;if(!session.Roots.TryGetValue(app.Id,out roots)){roots=new List<ProcessIdentity>();session.Roots.Add(app.Id,roots);}roots.Add(identity);
                                    }
                                    token.ThrowIfCancellationRequested();
                                    long deadline=platform.Now+Math.Min(app.MaxWaitMs,10000);
                                    do
                                    {
                                        existing=(await platform.Find(app)).Concat(await OwnedNow(workspace,app)).GroupBy(p=>p.Key).Select(g=>g.First()).ToList();if(existing.Count>0)break;
                                        await platform.Delay(250,token);
                                    }while(platform.Now<deadline);
                                    if(existing.Count==0)throw new InvalidOperationException("No matching process appeared. The app may have exited or handed off to another executable.");
                                    current[app.Id]=existing;app.State=AppState.Running;
                                    app.CanStop=session.Owned.ContainsKey(app.Id)&&session.Owned[app.Id].Count>0;app.StopFailed=false;
                                    app.Detail=identity==null?"Running · ownership uncertain; workspace stop leaves it open":"Running";
                                    break;
                                }
                                catch(OperationCanceledException){throw;}
                                catch(Exception ex)
                                {
                                    app.State=AppState.Failed;app.Detail=ex.Message;
                                }
                                if(attempt<app.Retries)await platform.Delay(1000,token);
                            }
                        }
                        if(app.State==AppState.Failed && workspace.PauseOnFailure)
                        {
                            pause=new TaskCompletionSource<bool>();Notify("Paused at "+app.Name+". Retry or skip to continue.");
                            try {using(token.Register(()=>pause.TrySetCanceled())) { again=await pause.Task; }}
                            finally {pause=null;}
                        }
                    }while(again);
                    ActiveApp=null;
                }
                int failures=selected.Count(a=>a.State==AppState.Failed);
                session.Status=failures>0?"Needs attention":"Running";
                Notify((failures>0?"Sequence finished · "+failures+" application(s) need attention":workspace.Name+" is ready")+(pressureWarnings>0?" · Maximum pressure wait reached "+pressureWarnings+" time(s); continued as configured.":""));
            }
            catch(OperationCanceledException) {session.Status="Cancelled";Notify("Launch sequence cancelled. Running apps remain open.");}
            catch(Exception ex) {session.Status="Failed";Notify("Sequence stopped: "+ex.Message);}
            finally
            {
                foreach(var app in selected.Where(a=>a.State==AppState.Queued||a.State==AppState.Waiting||a.State==AppState.Launching)){app.State=AppState.Closed;app.Detail="Launch cancelled";}
                ActiveWorkspace=null;ActiveApp=null;pause=null;cancellation.Dispose();cancellation=null;if(Changed!=null)Changed();
            }
        }
        async Task<bool> WaitUntilReady(AppEntry app,long? previous,CancellationToken token)
        {
            long start=previous??platform.Now;int backoff=350;
            while(true)
            {
                token.ThrowIfCancellationRequested();if(skip)return false;
                LastPressure=await platform.Sample();token.ThrowIfCancellationRequested();
                long elapsed=platform.Now-start;
                int minimum=previous.HasValue?(app.Mode==LaunchMode.Timed||app.Mode==LaunchMode.Hybrid?app.DelayMs:app.Mode==LaunchMode.Smart?750:0):0;
                bool pressureReady=app.Mode==LaunchMode.Immediate||app.Mode==LaunchMode.Timed||LastPressure.Level!=PressureLevel.High;
                if(elapsed>=minimum && pressureReady && !LastPressure.Critical)return true;
                if(elapsed>=app.MaxWaitMs)
                {
                    if(LastPressure.Critical)return false;
                    waitLimitReached=true;
                    Notify("Maximum wait reached for "+app.Name+"; continuing with high pressure.");
                    app.Detail="Maximum wait reached; continuing";
                    return true;
                }
                app.State=AppState.Waiting;
                app.Detail=LastPressure.Critical?"Waiting for available memory":elapsed<minimum?"Settling · "+Math.Ceiling((minimum-elapsed)/1000.0)+"s remaining":"Waiting for system pressure to ease";
                Notify("Preparing "+app.Name);
                await platform.Delay((int)Math.Min(backoff,app.MaxWaitMs-elapsed),token);backoff=Math.Min(1500,backoff+250);
            }
        }
        public void Skip() {if(pause!=null)pause.TrySetResult(false);else skip=true;}
        public void RetryPaused() {if(pause!=null)pause.TrySetResult(true);}
        public async Task Cancel() {if(cancellation!=null)cancellation.Cancel();if(activeTask!=null)await activeTask;}
        public async Task StopWorkspace(Workspace workspace)
        {
            if(ActiveWorkspace==workspace)await Cancel();
            var session=SessionFor(workspace);if(session==null){Notify("This workspace has no applications owned by the current session.");return;}
            session.Status="Stopping";int failures=0;stopping++;
            try
            {
            foreach(var app in workspace.Apps)
            {
                if(!session.Roots.ContainsKey(app.Id))continue;
                await StopApplication(workspace,app);if(app.StopFailed)failures++;
            }
            }finally{stopping--;}
            await Refresh(new[]{workspace},false);session.Status=failures>0?"Needs attention":"Stopped";
            Notify(failures>0?failures+" application(s) are still open. Save changes or use Force stop in the app menu.":"Workspace stopped. Applications opened beforehand remain protected.");
        }
        public bool Owns(Workspace workspace,ProcessIdentity process)
        {
            var s=SessionFor(workspace);return s!=null&&s.Owned.Values.Any(ids=>ids.Any(id=>id.Key==process.Key&&Rules.SamePath(id.Path,process.Path)));
        }
        public async Task StopApplication(Workspace workspace,AppEntry app,bool force=false)
        {
            if(app.State==AppState.Stopping)return;
            if(ActiveWorkspace==workspace)await Cancel();
            var owned=await OwnedNow(workspace,app);
            if(owned.Count==0){app.CanStop=false;app.Detail="Opened outside this workspace · protected. Close it in Windows.";app.Changed();Notify("Workplace cannot stop this pre-existing or unverified instance.");return;}
            var session=SessionFor(workspace);var roots=session.Roots[app.Id].ToList();
            stopping++;app.State=AppState.Stopping;app.StopFailed=false;Notify("Closing "+app.Name);
            try
            {
                foreach(var id in owned){requestedStops.Add(id.Key);await platform.Close(id,false);}
                long deadline=platform.Now+(force?0:8000);
                while(owned.Count>0&&platform.Now<deadline){app.Detail="Waiting for the application to close…";await platform.Delay(400,CancellationToken.None);owned=await OwnedNow(workspace,app);}
                if(owned.Count>0&&(force||!await platform.HasVisibleWindows(owned)))
                {
                    app.Detail="Closing remaining owned background processes…";foreach(var id in owned)requestedStops.Add(id.Key);
                    await platform.TerminateOwned(roots,owned);deadline=platform.Now+2500;
                    do{owned=await OwnedNow(workspace,app);if(owned.Count==0)break;await platform.Delay(250,CancellationToken.None);}while(platform.Now<deadline);
                }
                app.StopFailed=owned.Count>0;app.State=app.StopFailed?AppState.Failed:AppState.Closed;app.CanStop=app.StopFailed;
                app.Detail=app.StopFailed?"Still open. Save changes, then retry or choose Force stop.":"Closed";
                if(!app.StopFailed){platform.Forget(roots);session.Roots.Remove(app.Id);session.Owned.Remove(app.Id);}
            }
            catch(Exception ex){app.StopFailed=true;app.State=AppState.Failed;app.Detail="Could not stop safely: "+ex.Message;}
            finally{stopping--;}
            await Refresh(new[]{workspace},false);Notify(app.StopFailed?app.Name+" is still open. Save changes or use Force stop.":app.Name+" stopped");
        }
    }
}
