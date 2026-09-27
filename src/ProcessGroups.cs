using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace WorkplaceOrchestrator
{
    // Job objects establish Windows-owned lineage for new desktop processes. No KILL_ON_JOB_CLOSE flag:
    // quitting Workplace must leave managed applications running. Shell/UAC/broker launches fall back
    // to creation-time-verified ancestry and are never adopted just because their name matches.
    public sealed class ProcessGroups : IDisposable
    {
        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct StartupInfo { public int cb; public string reserved,desktop,title; public int x,y,cx,cy,xChars,yChars,fill,flags; public short show,reserved2;public IntPtr reservedPtr,input,output,error; }
        [StructLayout(LayoutKind.Sequential)] struct ProcessInfo { public IntPtr process,thread;public int pid,tid; }
        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct ProcessEntry { public uint size,usage,pid;public UIntPtr heap;public uint module,threads,parent;public int priority;public uint flags;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=260)]public string exe; }
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool CreateProcess(string name,StringBuilder command,IntPtr pa,IntPtr ta,bool inherit,uint flags,IntPtr env,string cwd,ref StartupInfo startup,out ProcessInfo process);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr CreateJobObject(IntPtr attrs,string name);
        [DllImport("kernel32.dll",SetLastError=true)]static extern bool AssignProcessToJobObject(IntPtr job,IntPtr process);
        [DllImport("kernel32.dll",SetLastError=true)]static extern bool QueryInformationJobObject(IntPtr job,int kind,IntPtr data,int length,IntPtr returned);
        [DllImport("kernel32.dll",SetLastError=true)]static extern bool TerminateJobObject(IntPtr job,uint code);
        [DllImport("kernel32.dll")]static extern uint ResumeThread(IntPtr thread);
        [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll",SetLastError=true)]static extern bool TerminateProcess(IntPtr handle,uint code);
        [DllImport("kernel32.dll",SetLastError=true)]static extern IntPtr CreateToolhelp32Snapshot(uint flags,uint pid);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern bool Process32First(IntPtr snapshot,ref ProcessEntry entry);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern bool Process32Next(IntPtr snapshot,ref ProcessEntry entry);
        [DllImport("user32.dll")]static extern bool EnumWindows(EnumCallback callback,IntPtr parameter);
        delegate bool EnumCallback(IntPtr window,IntPtr parameter);
        [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
        [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")]static extern bool PostMessage(IntPtr window,uint message,IntPtr wParam,IntPtr lParam);
        readonly Dictionary<string,IntPtr> jobs=new Dictionary<string,IntPtr>();
        readonly object gate=new object();

        public ProcessIdentity Launch(string path)
        {
            var startup=new StartupInfo{cb=Marshal.SizeOf(typeof(StartupInfo))};ProcessInfo info;
            if(!CreateProcess(path,new StringBuilder("\""+path+"\""),IntPtr.Zero,IntPtr.Zero,false,4,IntPtr.Zero,System.IO.Path.GetDirectoryName(path),ref startup,out info))throw new Win32Exception(Marshal.GetLastWin32Error());
            IntPtr job=IntPtr.Zero;
            try
            {
                ProcessIdentity identity;using(var process=Process.GetProcessById(info.pid))identity=WindowsPlatform.Identity(process);
                job=CreateJobObject(IntPtr.Zero,null);
                if(job!=IntPtr.Zero&&AssignProcessToJobObject(job,info.process)&&identity!=null){lock(gate)jobs.Add(identity.Key,job);job=IntPtr.Zero;}
                if(ResumeThread(info.thread)==UInt32.MaxValue){TerminateProcess(info.process,1);throw new Win32Exception(Marshal.GetLastWin32Error());}
                return identity;
            }
            catch{TerminateProcess(info.process,1);throw;}
            finally{if(job!=IntPtr.Zero)CloseHandle(job);CloseHandle(info.thread);CloseHandle(info.process);}
        }
        public static ProcessIdentity Live(ProcessIdentity expected)
        {
            try{using(var process=Process.GetProcessById(expected.Pid)){var now=WindowsPlatform.Identity(process);return now!=null&&now.Key==expected.Key&&Rules.SamePath(now.Path,expected.Path)?now:null;}}catch{return null;}
        }
        static List<int> JobPids(IntPtr job)
        {
            for(int capacity=32;capacity<=16384;capacity*=2)
            {
                int bytes=8+capacity*IntPtr.Size;IntPtr buffer=Marshal.AllocHGlobal(bytes);
                try
                {
                    if(!QueryInformationJobObject(job,3,buffer,bytes,IntPtr.Zero)){if(Marshal.GetLastWin32Error()==234)continue;return new List<int>();}
                    int count=Marshal.ReadInt32(buffer,4);var result=new List<int>();for(int i=0;i<Math.Min(capacity,count);i++)result.Add((int)Marshal.ReadIntPtr(buffer,8+i*IntPtr.Size));return result;
                }finally{Marshal.FreeHGlobal(buffer);}
            }
            return new List<int>();
        }
        public List<ProcessIdentity> Expand(IEnumerable<ProcessIdentity> seeds)
        {
            var known=seeds.GroupBy(p=>p.Key).Select(g=>g.First()).ToArray();var live=new Dictionary<int,ProcessIdentity>();
            foreach(var seed in known)
            {
                var identity=Live(seed);if(identity!=null)live[identity.Pid]=identity;
                lock(gate)
                {
                    IntPtr job;if(!jobs.TryGetValue(seed.Key,out job))continue;
                    foreach(int pid in JobPids(job))try{using(var process=Process.GetProcessById(pid)){identity=WindowsPlatform.Identity(process);if(identity!=null&&identity.StartedUtcTicks>=seed.StartedUtcTicks)live[pid]=identity;}}catch{}
                }
            }
            if(live.Count==0)return live.Values.ToList();
            // A transient Toolhelp snapshot provides parent links only; paths/timestamps are read solely
            // for candidate descendants. An exited/reused parent cannot establish a new ancestry link.
            var parents=new Dictionary<int,int>();IntPtr snapshot=CreateToolhelp32Snapshot(2,0);
            if(snapshot!=new IntPtr(-1))try
            {
                var entry=new ProcessEntry{size=(uint)Marshal.SizeOf(typeof(ProcessEntry))};
                if(Process32First(snapshot,ref entry))do{parents[(int)entry.pid]=(int)entry.parent;}while(Process32Next(snapshot,ref entry));
            }finally{CloseHandle(snapshot);}
            bool added;
            do
            {
                added=false;
                foreach(var pair in parents)
                {
                    ProcessIdentity parent;if(live.ContainsKey(pair.Key)||!live.TryGetValue(pair.Value,out parent))continue;
                    if(Live(parent)==null)continue;
                    try{using(var process=Process.GetProcessById(pair.Key)){var child=WindowsPlatform.Identity(process);if(child!=null&&child.StartedUtcTicks>=parent.StartedUtcTicks){child.ParentPid=parent.Pid;live[child.Pid]=child;added=true;}}}catch{}
                }
            }while(added);
            foreach(var pair in parents){ProcessIdentity id;if(live.TryGetValue(pair.Key,out id))id.ParentPid=pair.Value;}
            return live.Values.ToList();
        }
        public static bool HasVisibleWindows(IEnumerable<ProcessIdentity> processes)
        {
            var live=processes.Where(p=>Live(p)!=null).Select(p=>p.Pid).ToArray();bool visible=false;
            EnumWindows((window,parameter)=>{uint pid;GetWindowThreadProcessId(window,out pid);if(live.Contains((int)pid)&&IsWindowVisible(window)){visible=true;return false;}return true;},IntPtr.Zero);return visible;
        }
        public static bool RequestClose(ProcessIdentity identity)
        {
            if(Live(identity)==null)return true;bool requested=false;
            EnumWindows((window,parameter)=>{uint pid;GetWindowThreadProcessId(window,out pid);if(pid==identity.Pid&&IsWindowVisible(window)&&Live(identity)!=null)requested=PostMessage(window,0x10,IntPtr.Zero,IntPtr.Zero)||requested;return true;},IntPtr.Zero);return requested;
        }
        public bool TerminateJobs(IEnumerable<ProcessIdentity> roots)
        {
            bool ok=true;lock(gate)foreach(var root in roots){IntPtr job;if(jobs.TryGetValue(root.Key,out job))ok=TerminateJobObject(job,1)&&ok;}return ok;
        }
        public void Forget(IEnumerable<ProcessIdentity> roots)
        {
            lock(gate)foreach(var root in roots){IntPtr job;if(jobs.TryGetValue(root.Key,out job)){CloseHandle(job);jobs.Remove(root.Key);}}
        }
        public void Dispose(){lock(gate){foreach(var job in jobs.Values)CloseHandle(job);jobs.Clear();}}
    }
}
