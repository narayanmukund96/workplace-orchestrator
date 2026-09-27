using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;

namespace WorkplaceOrchestrator
{
    public enum LaunchMode { Smart, Timed, Hybrid, Immediate }
    public enum AppState { Closed, Queued, Launching, Running, Waiting, Failed, StoppedUnexpectedly, Stopping }
    public enum Appearance { System, Light, Dark }
    public sealed class Preferences
    {
        public Appearance Theme = Appearance.System;
        public bool StartWithWindows;
        public bool CloseToTray = true;
    }
    public enum PressureLevel { Healthy, Moderate, High }

    public sealed class AppEntry : INotifyPropertyChanged
    {
        public string Id = Guid.NewGuid().ToString();
        public string Name = "";
        public string Path = "";
        public string Aumid = "";
        public string Source = "manual";
        public string IconReference = "";
        public string ShortcutPath = "";
        public LaunchMode Mode = LaunchMode.Hybrid;
        public int DelayMs = 2000;
        public int MaxWaitMs = 30000;
        public int Retries = 1;
        public bool Enabled = true;
        AppState state;
        string detail = "Not running";
        public AppState State { get { return state; } set { if(state==value)return;state = value; Changed(); } }
        public string Detail { get { return detail; } set { if(detail==value)return;detail = value; Changed(); } }
        public string DisplayName { get { return Name; } }
        public object Icon { get; set; }
        public string ExecutablePath { get { return Path; } }
        public string RuleLabel { get { return (Enabled ? "" : "Disabled · ") + Mode + (Mode == LaunchMode.Timed || Mode == LaunchMode.Hybrid ? " · " + (DelayMs / 1000.0).ToString("0.#") + "s minimum" : ""); } }
        public string StateLabel { get { return State == AppState.StoppedUnexpectedly ? "Stopped unexpectedly" : State.ToString(); } }
        bool canStop;
        public bool CanStop { get{return canStop;} set{if(canStop==value)return;canStop=value;Changed();} }
        public bool StopFailed { get; set; }
        public string ActionLabel { get { return State == AppState.Stopping ? "Stopping…" : State == AppState.Running ? (CanStop?"Stop":"Running") : State == AppState.Failed || State == AppState.StoppedUnexpectedly ? "Retry" : "Start"; } }
        public bool ActionEnabled { get { return State!=AppState.Stopping && (State!=AppState.Running || CanStop); } }
        public string StateColor { get { return State == AppState.Running ? "#23977C" : State == AppState.Failed || State == AppState.StoppedUnexpectedly ? "#C34F4F" : State == AppState.Closed ? "#7A8799" : "#B97A23"; } }
        public event PropertyChangedEventHandler PropertyChanged;
        public void Changed() { if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs("")); }
        public AppEntry Copy() { return new AppEntry { Name=Name, Path=Path, Aumid=Aumid, Source=Source, IconReference=IconReference, ShortcutPath=ShortcutPath, Mode=Mode, DelayMs=DelayMs, MaxWaitMs=MaxWaitMs, Retries=Retries, Enabled=Enabled }; }
    }
    public sealed class Workspace : INotifyPropertyChanged
    {
        public string Id = Guid.NewGuid().ToString();
        public string Name = "New workspace";
        public DateTime Created = DateTime.UtcNow;
        public DateTime Updated = DateTime.UtcNow;
        public LaunchMode DefaultMode = LaunchMode.Hybrid;
        public int DefaultDelayMs = 2000;
        public bool PauseOnFailure;
        public List<AppEntry> Apps = new List<AppEntry>();
        string runtimeStatus="";
        public string RuntimeStatus { get { return runtimeStatus; } set { if(runtimeStatus==value)return;runtimeStatus=value;if(PropertyChanged!=null)PropertyChanged(this,new PropertyChangedEventArgs("Summary")); } }
        public event PropertyChangedEventHandler PropertyChanged;
        public string Summary { get { return Apps.Count + (Apps.Count == 1 ? " application" : " applications")+(runtimeStatus.Length>0?" · "+runtimeStatus:""); } }
        public string DisplayName { get { return Name; } }
        public override string ToString() { return Name; }
        public Workspace Copy() { return new Workspace { Name=Name + " copy", DefaultMode=DefaultMode, DefaultDelayMs=DefaultDelayMs, PauseOnFailure=PauseOnFailure, Apps=Apps.Select(a => a.Copy()).ToList() }; }
    }
    public sealed class ProcessIdentity
    {
        public int Pid;
        public long StartedUtcTicks;
        public string Path;
        public int ParentPid;
        public string Key { get { return Pid + ":" + StartedUtcTicks; } }
    }
    public sealed class Session
    {
        public string Id = Guid.NewGuid().ToString();
        public string WorkspaceId;
        public DateTime Started = DateTime.UtcNow;
        public string Status = "Launching";
        public readonly Dictionary<string, List<ProcessIdentity>> Owned = new Dictionary<string, List<ProcessIdentity>>();
        public readonly Dictionary<string, List<ProcessIdentity>> Roots = new Dictionary<string, List<ProcessIdentity>>();
        public readonly HashSet<string> PreExisting = new HashSet<string>();
    }
    public sealed class Pressure
    {
        public double Cpu;
        public double AvailableMemoryFraction = 1;
        public ulong AvailableBytes = UInt64.MaxValue;
        public double Disk;
        public bool CpuAvailable = true, DiskAvailable = true, MemoryAvailable = true;
        public bool Critical { get { return MemoryAvailable && (AvailableMemoryFraction < .04 || AvailableBytes < 200UL * 1024 * 1024); } }
        public PressureLevel Level
        {
            get
            {
                double score = (CpuAvailable ? Cpu / 100 * .45 : .2) + (MemoryAvailable ? (1 - AvailableMemoryFraction) * .35 : .2) + (DiskAvailable ? Disk / 100 * .2 : .1);
                if (Critical || (MemoryAvailable && AvailableMemoryFraction < .10) || (CpuAvailable && Cpu >= 92) || (DiskAvailable && Disk >= 95) || score >= .78) return PressureLevel.High;
                return score >= .55 ? PressureLevel.Moderate : PressureLevel.Healthy;
            }
        }
    }
    public static class Rules
    {
        public static string ValidatePath(string path, bool requireExists)
        {
            if (String.IsNullOrWhiteSpace(path) || path.IndexOfAny(new[] { '\r', '\n', '"', '\0' }) >= 0) return "Choose a local application executable.";
            if (!System.IO.Path.IsPathRooted(path) || path.StartsWith(@"\\") || path.Length < 3 || path[1] != ':') return "Use an absolute path on a local drive.";
            if (!String.Equals(System.IO.Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase)) return "Only application (.exe) files are supported.";
            try
            {
                string full = System.IO.Path.GetFullPath(path);
                if (full.Substring(2).Contains(":")) return "Alternate data streams are not supported.";
                var drive = new DriveInfo(System.IO.Path.GetPathRoot(full));
                if (drive.DriveType == DriveType.Network) return "Network executables are not supported.";
                if (requireExists && !File.Exists(full)) return "The application was moved or uninstalled. Choose its executable again.";
            }
            catch { return "The executable path is invalid."; }
            return null;
        }
        public static bool SamePath(string a, string b)
        {
            try { return String.Equals(System.IO.Path.GetFullPath(a), System.IO.Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }
        public static void Validate(Workspace w)
        {
            Guid id;
            if (!Guid.TryParse(w.Id, out id) || String.IsNullOrWhiteSpace(w.Name) || w.Name.Length > 100) throw new InvalidDataException("Invalid workspace identity or name.");
            if (!Enum.IsDefined(typeof(LaunchMode), w.DefaultMode) || w.DefaultDelayMs < 0 || w.DefaultDelayMs > 300000) throw new InvalidDataException("Invalid workspace launch defaults.");
            if (w.Apps.Count > 200 || w.Apps.Select(a=>a.Id).Distinct().Count() != w.Apps.Count) throw new InvalidDataException("Invalid application list.");
            foreach (var a in w.Apps)
            {
                if (!Guid.TryParse(a.Id, out id) || String.IsNullOrWhiteSpace(a.Name) || a.Name.Length > 200) throw new InvalidDataException("Invalid application identity.");
                string error = ValidatePath(a.Path, false);
                if (error != null) throw new InvalidDataException(a.Name + ": " + error);
                if (!Enum.IsDefined(typeof(LaunchMode), a.Mode) || a.DelayMs < 0 || a.DelayMs > 300000 || a.MaxWaitMs < Math.Max(1000, a.DelayMs) || a.MaxWaitMs > 600000 || a.Retries < 0 || a.Retries > 5) throw new InvalidDataException("Invalid launch rule for " + a.Name);
                if (a.Aumid.Length > 300 || a.Aumid.Any(c=>Char.IsControl(c)) || (a.Aumid.Length > 0 && !a.Aumid.Contains("!"))) throw new InvalidDataException("Invalid packaged application identity.");
                if(!new[]{"manual","registry","start-menu","packaged"}.Contains(a.Source))throw new InvalidDataException("Invalid application source.");
                if(a.IconReference.Length>32768 || a.ShortcutPath.Length>32768)throw new InvalidDataException("Invalid application metadata.");
            }
        }
    }
}
