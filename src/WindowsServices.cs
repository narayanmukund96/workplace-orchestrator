using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Xml;
using Microsoft.Win32;

namespace WorkplaceOrchestrator
{
    public interface IPlatform
    {
        Task<List<ProcessIdentity>> Find(AppEntry app);
        Task<ProcessIdentity> Launch(AppEntry app);
        Task<bool> Close(ProcessIdentity identity, bool force);
        Task<List<ProcessIdentity>> ExpandOwned(List<ProcessIdentity> known);
        Task<bool> TerminateOwned(List<ProcessIdentity> roots,List<ProcessIdentity> owned);
        Task<bool> HasVisibleWindows(List<ProcessIdentity> owned);
        bool Available(AppEntry app);
        void Forget(List<ProcessIdentity> roots);
        Task<Pressure> Sample();
        Task Delay(int milliseconds, CancellationToken token);
        long Now { get; }
    }
    public sealed class WindowsPlatform : IPlatform, IDisposable
    {
        [DllImport("kernel32.dll",SetLastError=true)] static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool QueryFullProcessImageName(IntPtr h,uint flags,StringBuilder name,ref int size);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll")] static extern bool GetSystemTimes(out long idle,out long kernel,out long user);
        [DllImport("kernel32.dll",CharSet=CharSet.Auto)] static extern bool GlobalMemoryStatusEx([In,Out] MemoryStatus status);
        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Auto)] sealed class MemoryStatus
        {
            public uint Length=(uint)Marshal.SizeOf(typeof(MemoryStatus)); public uint Load;
            public ulong TotalPhysical,AvailablePhysical,TotalPage,AvailablePage,TotalVirtual,AvailableVirtual,Extended;
        }
        [ComImport,Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")] class ActivationManager { }
        [ComImport,Guid("2e941141-7f97-4756-ba1d-9decde894a3d"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IActivationManager
        {
            [PreserveSig] int ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] string id,[MarshalAs(UnmanagedType.LPWStr)] string args,uint options,out uint pid);
            [PreserveSig] int ActivateForFile(IntPtr id,IntPtr items,IntPtr verb,out uint pid);
            [PreserveSig] int ActivateForProtocol(IntPtr id,IntPtr items,out uint pid);
        }
        readonly Stopwatch clock=Stopwatch.StartNew();
        readonly object sampleLock=new object();
        readonly ProcessGroups groups=new ProcessGroups();
        long lastIdle,lastKernel,lastUser;
        long lastSample=-10000;
        PerformanceCounter disk;
        readonly Queue<double> cpuWindow=new Queue<double>();
        readonly Queue<double> diskWindow=new Queue<double>();
        public long Now { get { return clock.ElapsedMilliseconds; } }
        public WindowsPlatform()
        {
            GetSystemTimes(out lastIdle,out lastKernel,out lastUser);
            try { disk=new PerformanceCounter("PhysicalDisk","% Idle Time","_Total",true); disk.NextValue(); } catch { disk=null; }
        }
        public static ProcessIdentity Identity(Process process)
        {
            IntPtr h=IntPtr.Zero;
            try
            {
                h=OpenProcess(0x1000,false,process.Id); if(h==IntPtr.Zero) return null;
                var name=new StringBuilder(32768); int length=name.Capacity;
                if(!QueryFullProcessImageName(h,0,name,ref length)) return null;
                return new ProcessIdentity { Pid=process.Id,StartedUtcTicks=process.StartTime.ToUniversalTime().Ticks,Path=name.ToString() };
            }
            catch { return null; }
            finally { if(h!=IntPtr.Zero) CloseHandle(h); }
        }
        public Task<List<ProcessIdentity>> Find(AppEntry app)
        {
            return Task.Run(()=>
            {
                var result=new List<ProcessIdentity>();
                foreach(var p in Process.GetProcessesByName(System.IO.Path.GetFileNameWithoutExtension(app.Path)))
                {
                    using(p) { var id=Identity(p); if(id!=null && Rules.SamePath(id.Path,app.Path)) result.Add(id); }
                }
                return result;
            });
        }
        public Task<ProcessIdentity> Launch(AppEntry app)
        {
            return Task.Run(()=>
            {
                string validation=Rules.ValidatePath(app.Path,true);
                if(validation!=null) throw new InvalidOperationException(validation);
                Process p;
                if(!String.IsNullOrEmpty(app.Aumid))
                {
                    if(!PackageIdentity.Matches(app.Aumid,app.Path))throw new InvalidOperationException("The Store application identity no longer matches this executable. Remove the entry and add the application again.");
                    object manager=new ActivationManager();
                    try { uint pid; int hr=((IActivationManager)manager).ActivateApplication(app.Aumid,null,0,out pid); Marshal.ThrowExceptionForHR(hr); p=Process.GetProcessById((int)pid); }
                    finally { Marshal.ReleaseComObject(manager); }
                }
                else
                {
                    try{return groups.Launch(app.Path);}
                    catch(System.ComponentModel.Win32Exception ex){if(ex.NativeErrorCode!=740)throw;}
                    // Elevation-required binaries use the regular Windows consent flow.
                    p=Process.Start(new ProcessStartInfo(app.Path) { UseShellExecute=true, WorkingDirectory=System.IO.Path.GetDirectoryName(app.Path) });
                }
                if(p==null) return null;
                using(p) { var id=Identity(p); return id!=null && Rules.SamePath(id.Path,app.Path) ? id : null; }
            });
        }
        public Task<bool> Close(ProcessIdentity id,bool force)
        {
            return Task.Run(()=>
            {
                try
                {
                    using(var p=Process.GetProcessById(id.Pid))
                    {
                        var current=Identity(p);
                        if(current==null || current.Key!=id.Key || !Rules.SamePath(current.Path,id.Path)) return false;
                        if(force) { p.Kill(); return true; }
                        return ProcessGroups.RequestClose(id);
                    }
                }
                catch(ArgumentException) { return true; }
                catch(InvalidOperationException) { return false; }
                catch(System.ComponentModel.Win32Exception) { return false; }
            });
        }
        public Task<List<ProcessIdentity>> ExpandOwned(List<ProcessIdentity> known){return Task.Run(()=>groups.Expand(known));}
        public Task<bool> HasVisibleWindows(List<ProcessIdentity> owned){return Task.Run(()=>ProcessGroups.HasVisibleWindows(owned));}
        public bool Available(AppEntry app){return File.Exists(app.Path);}
        public void Forget(List<ProcessIdentity> roots){groups.Forget(roots);}
        public async Task<bool> TerminateOwned(List<ProcessIdentity> roots,List<ProcessIdentity> owned)
        {
            var live=await ExpandOwned(roots.Concat(owned).ToList());bool ok=groups.TerminateJobs(roots);
            // Job termination is atomic; conservative fallback kills only validated identities, children first.
            foreach(var id in live.OrderByDescending(p=>p.StartedUtcTicks))ok=await Close(id,true)&&ok;
            return ok;
        }
        public Task<Pressure> Sample()
        {
            return Task.Run(()=>
            {
                lock(sampleLock)
                {
                    // After an idle gap, measure a fresh bounded window instead of averaging since startup.
                    if(Now-lastSample>3000)
                    {
                        GetSystemTimes(out lastIdle,out lastKernel,out lastUser);cpuWindow.Clear();diskWindow.Clear();
                        if(disk!=null)try{disk.NextValue();}catch{}
                        Thread.Sleep(200);
                    }
                    lastSample=Now;
                    var value=new Pressure(); long idle,kernel,user;
                    if(GetSystemTimes(out idle,out kernel,out user))
                    {
                        long total=kernel-lastKernel+user-lastUser;
                        double cpu=total>0 ? 100.0*(total-(idle-lastIdle))/total : 0;
                        lastIdle=idle;lastKernel=kernel;lastUser=user;
                        cpuWindow.Enqueue(Math.Max(0,Math.Min(100,cpu)));if(cpuWindow.Count>3)cpuWindow.Dequeue(); value.Cpu=cpuWindow.Average();
                    }
                    else value.CpuAvailable=false;
                    var memory=new MemoryStatus();
                    value.MemoryAvailable=GlobalMemoryStatusEx(memory);
                    if(value.MemoryAvailable) { value.AvailableBytes=memory.AvailablePhysical;value.AvailableMemoryFraction=memory.TotalPhysical>0 ? (double)memory.AvailablePhysical/memory.TotalPhysical : 0; }
                    if(disk!=null)
                    {
                        try { diskWindow.Enqueue(Math.Max(0,Math.Min(100,100-disk.NextValue())));if(diskWindow.Count>3)diskWindow.Dequeue();value.Disk=diskWindow.Average(); }
                        catch { value.DiskAvailable=false; }
                    }
                    else value.DiskAvailable=false;
                    return value;
                }
            });
        }
        public Task Delay(int ms,CancellationToken token) { return Task.Delay(ms,token); }
        public void Dispose() { groups.Dispose();if(disk!=null) disk.Dispose(); }
    }
    public static class PackageIdentity
    {
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,ExactSpelling=true)] static extern int GetPackagesByPackageFamily(string family,ref uint count,IntPtr names,ref uint bufferLength,IntPtr buffer);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,ExactSpelling=true)] static extern int GetPackagePathByFullName(string fullName,ref uint length,StringBuilder path);
        public static bool Matches(string aumid,string executable)
        {
            IntPtr names=IntPtr.Zero,buffer=IntPtr.Zero;
            try
            {
                string[] parts=aumid.Split('!');if(parts.Length!=2)return false;
                uint count=0,length=0;
                int rc=GetPackagesByPackageFamily(parts[0],ref count,IntPtr.Zero,ref length,IntPtr.Zero);
                if((rc!=0&&rc!=122)||count==0||count>1024||length>1048576)return false;
                names=Marshal.AllocHGlobal(checked((int)count*IntPtr.Size));buffer=Marshal.AllocHGlobal(checked((int)length*2));
                if(GetPackagesByPackageFamily(parts[0],ref count,names,ref length,buffer)!=0)return false;
                for(int i=0;i<count;i++)
                {
                    string fullName=Marshal.PtrToStringUni(Marshal.ReadIntPtr(names,i*IntPtr.Size));uint pathLength=0;
                    if(GetPackagePathByFullName(fullName,ref pathLength,null)!=122||pathLength>32768)continue;
                    var path=new StringBuilder((int)pathLength);if(GetPackagePathByFullName(fullName,ref pathLength,path)!=0)continue;
                    string manifest=System.IO.Path.Combine(path.ToString(),"AppxManifest.xml");if(!File.Exists(manifest))continue;
                    var document=new XmlDocument{XmlResolver=null};
                    using(var reader=XmlReader.Create(manifest,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=4194304}))document.Load(reader);
                    foreach(XmlNode node in document.GetElementsByTagName("Application"))
                    {
                        var id=node.Attributes["Id"];var exe=node.Attributes["Executable"];
                        if(id!=null&&exe!=null&&id.Value==parts[1]&&Rules.SamePath(System.IO.Path.Combine(path.ToString(),exe.Value),executable))return true;
                    }
                }
            }
            catch {return false;}
            finally{if(names!=IntPtr.Zero)Marshal.FreeHGlobal(names);if(buffer!=IntPtr.Zero)Marshal.FreeHGlobal(buffer);}
            return false;
        }
    }
    public sealed class ProcessMonitor : IDisposable
    {
        ManagementEventWatcher watcher;
        readonly Dictionary<string,Process> observed=new Dictionary<string,Process>();
        public event Action Changed;
        public event Action<string,int> Exited;
        public bool EventsAvailable { get; private set; }
        string filter="";
        public void Configure(IEnumerable<AppEntry> apps)
        {
            string next=String.Join(" OR ",apps.Select(a=>System.IO.Path.GetFileName(a.Path)).Distinct(StringComparer.OrdinalIgnoreCase).Select(n=>"ProcessName='"+n.Replace("\\","\\\\").Replace("'","\\'")+"'"));
            if(next==filter) return; filter=next;
            if(watcher!=null){try{watcher.Stop();}catch{}watcher.Dispose();watcher=null;}
            EventsAvailable=false;
            if(next.Length==0) return;
            try { watcher=new ManagementEventWatcher("SELECT * FROM Win32_ProcessStartTrace WHERE "+next); watcher.EventArrived+=(s,e)=>{if(Changed!=null)Changed();};watcher.Start();EventsAvailable=true; }
            catch { if(watcher!=null)watcher.Dispose();watcher=null; }
        }
        // Exit notifications retain only identities for configured apps, never unrelated process history.
        public void Observe(IEnumerable<ProcessIdentity> identities)
        {
            var live=new HashSet<string>();
            foreach(var id in identities)
            {
                live.Add(id.Key); if(observed.ContainsKey(id.Key))continue;
                try
                {
                    var p=Process.GetProcessById(id.Pid);var actual=WindowsPlatform.Identity(p);
                    if(actual==null || actual.Key!=id.Key){p.Dispose();continue;}
                    string key=id.Key;
                    p.Exited+=(s,e)=>{int code=0;try{code=p.ExitCode;}catch{}if(Exited!=null)Exited(key,code);if(Changed!=null)Changed();};
                    p.EnableRaisingEvents=true;observed.Add(key,p);
                }
                catch { }
            }
            foreach(var key in observed.Keys.Where(k=>!live.Contains(k)).ToArray()){observed[key].Dispose();observed.Remove(key);}
        }
        public void Dispose() { if(watcher!=null){try{watcher.Stop();}catch{}watcher.Dispose();}foreach(var p in observed.Values)p.Dispose();observed.Clear(); }
    }
    public sealed class DiscoveredApp
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public string Aumid { get; set; }
        public string Source { get; set; }
        public string IconReference { get; set; }
        public string ShortcutPath { get; set; }
        public object Icon { get { return ApplicationMetadata.Icon(Path,IconReference); } }
        public string Kind { get { return String.IsNullOrEmpty(Aumid)?"Desktop application":"Microsoft Store application"; } }
    }
    public static class Discovery
    {
        public static List<DiscoveredApp> Scan()
        {
            var result=new List<DiscoveredApp>();
            Action<string,string,string> add=(name,path,id)=>
            {
                if(String.IsNullOrWhiteSpace(path))return;
                path=Environment.ExpandEnvironmentVariables(path.Trim('"'));
                if(Rules.ValidatePath(path,true)!=null)return;
                if(System.IO.Path.GetFileName(path).IndexOf("OrchestratorControlledTest",StringComparison.OrdinalIgnoreCase)>=0 || System.IO.Path.GetFileName(path).Equals("UiSmoke.exe",StringComparison.OrdinalIgnoreCase))return;
                if(!result.Any(a=>Rules.SamePath(a.Path,path)&&a.Aumid==(id??"")))result.Add(new DiscoveredApp{Name=String.IsNullOrWhiteSpace(name)?ApplicationMetadata.DisplayName(path):name,Path=path,Aumid=id??"",Source=String.IsNullOrEmpty(id)?"registry":"packaged",IconReference=path,ShortcutPath=""});
            };
            foreach(var hive in new[]{RegistryHive.CurrentUser,RegistryHive.LocalMachine})
                foreach(var view in new[]{RegistryView.Registry64,RegistryView.Registry32})
                    try
                    {
                        using(var root=RegistryKey.OpenBaseKey(hive,view))using(var apps=root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths"))
                        {
                            if(apps==null)continue;
                            foreach(string key in apps.GetSubKeyNames())using(var app=apps.OpenSubKey(key)) { string path=app==null?null:app.GetValue(null) as string;string name=key;try{name=FileVersionInfo.GetVersionInfo((path??"").Trim('"')).ProductName;}catch{}add(name,path,""); }
                        }
                    } catch { }
            object shell=null;
            try
            {
                shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
                foreach(var folder in new[]{Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu)})
                    foreach(var file in SafeLinks(folder))
                    {
                        object shortcut=null;
                        try
                        {
                            shortcut=shell.GetType().InvokeMember("CreateShortcut",System.Reflection.BindingFlags.InvokeMethod,null,shell,new object[]{file});
                            string path=(string)shortcut.GetType().InvokeMember("TargetPath",System.Reflection.BindingFlags.GetProperty,null,shortcut,null);
                            string arguments=(string)shortcut.GetType().InvokeMember("Arguments",System.Reflection.BindingFlags.GetProperty,null,shortcut,null);
                            if(String.IsNullOrWhiteSpace(arguments))
                            {
                                string name=System.IO.Path.GetFileNameWithoutExtension(file);add(name,path,"");
                                var app=result.FirstOrDefault(a=>Rules.SamePath(a.Path,path)&&a.Aumid=="");
                                if(app!=null){app.Name=name;app.Source="start-menu";app.ShortcutPath=file;string icon=(string)shortcut.GetType().InvokeMember("IconLocation",System.Reflection.BindingFlags.GetProperty,null,shortcut,null);if(!String.IsNullOrWhiteSpace(icon)&&icon!=",0")app.IconReference=icon;}
                            }
                        }
                        catch{} finally{if(shortcut!=null)Marshal.ReleaseComObject(shortcut);}
                    }
            }
            catch{} finally{if(shell!=null)Marshal.ReleaseComObject(shell);}
            // Resolve Store identity to its actual executable. No shell commands or user arguments are accepted.
            try
            {
                const string command="$ErrorActionPreference='SilentlyContinue'; [Console]::OutputEncoding=[Text.Encoding]::UTF8; $names=@{}; Get-StartApps | ForEach-Object {$names[$_.AppID]=$_.Name}; $rows=@(Get-AppxPackage | ForEach-Object {$p=$_; $m=Get-AppxPackageManifest -Package $p; foreach($a in $m.Package.Applications.Application){$id=$p.PackageFamilyName+'!'+$a.Id; if($names.ContainsKey($id) -and $a.Executable){$logo=[string]$a.VisualElements.Square44x44Logo; if(!$logo){$logo=[string]$m.Package.Properties.Logo}; $icon=''; if($logo){$icon=Join-Path $p.InstallLocation $logo; if(!(Test-Path -LiteralPath $icon)){$base=[IO.Path]::GetFileNameWithoutExtension($icon);$dir=[IO.Path]::GetDirectoryName($icon);$found=Get-ChildItem -LiteralPath $dir -Filter ($base+'*.png') | Sort-Object @{Expression={if($_.Name -like '*targetsize-44*altform-unplated*'){0}elseif($_.Name -like '*scale-200*'){1}else{2}}},Name | Select-Object -First 1; if($found){$icon=$found.FullName}else{$icon=''}}};[pscustomobject]@{Name=$names[$id];Path=(Join-Path $p.InstallLocation $a.Executable);Aumid=$id;IconReference=$icon}}}}); ConvertTo-Json -InputObject $rows -Compress";
                var psi=new ProcessStartInfo(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),@"WindowsPowerShell\v1.0\powershell.exe"),"-NoProfile -NonInteractive -EncodedCommand "+Convert.ToBase64String(Encoding.Unicode.GetBytes(command))) {UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8};
                using(var p=Process.Start(psi))
                {
                    var output=p.StandardOutput.ReadToEndAsync();var errors=p.StandardError.ReadToEndAsync();
                    if(!p.WaitForExit(20000)){p.Kill();}else
                    {
                        var rows=new JavaScriptSerializer().Deserialize<List<DiscoveredApp>>(output.GetAwaiter().GetResult().Trim('\uFEFF'));
                        if(rows!=null)foreach(var a in rows){add(a.Name,a.Path,a.Aumid);var entry=result.FirstOrDefault(r=>r.Aumid==a.Aumid);if(entry!=null&&!String.IsNullOrEmpty(a.IconReference))entry.IconReference=a.IconReference;}
                    }
                }
            }catch{}
            return result.OrderBy(a=>a.Name,StringComparer.CurrentCultureIgnoreCase).ToList();
        }
        static IEnumerable<string> SafeLinks(string folder)
        {
            try{return Directory.GetFiles(folder,"*.lnk",SearchOption.AllDirectories);}catch{return new string[0];}
        }
    }
}
