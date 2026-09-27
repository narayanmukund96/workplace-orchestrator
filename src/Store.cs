using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace WorkplaceOrchestrator
{
    // Windows ships SQLite. All user values are parameter-bound; configuration writes are atomic.
    public sealed class Store : IDisposable
    {
        IntPtr db;
        [DllImport("winsqlite3", CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_open16([MarshalAs(UnmanagedType.LPWStr)] string path, out IntPtr db);
        [DllImport("winsqlite3", CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_close(IntPtr db);
        [DllImport("winsqlite3", CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_prepare16_v2(IntPtr db, [MarshalAs(UnmanagedType.LPWStr)] string sql, int n, out IntPtr stmt, IntPtr tail);
        [DllImport("winsqlite3", CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_step(IntPtr stmt);
        [DllImport("winsqlite3", CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_finalize(IntPtr stmt);
        [DllImport("winsqlite3", CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_bind_text16(IntPtr stmt, int index, [MarshalAs(UnmanagedType.LPWStr)] string value, int n, IntPtr destructor);
        [DllImport("winsqlite3", CallingConvention=CallingConvention.Cdecl)] static extern IntPtr sqlite3_column_text16(IntPtr stmt, int index);
        [DllImport("winsqlite3", CallingConvention=CallingConvention.Cdecl)] static extern IntPtr sqlite3_errmsg16(IntPtr db);
        public Store(string path)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path)));
            if (sqlite3_open16(path, out db) != 0) { string error=Error(); Dispose(); throw new IOException(error); }
            try
            {
                Run("PRAGMA busy_timeout=3000");
                var version=Read("PRAGMA user_version");
                int v=Int32.Parse(version[0][0]);
                if (v > 2) throw new InvalidDataException("This configuration was created by a newer version. It has not been changed.");
                Run("PRAGMA foreign_keys=ON");
                Run("PRAGMA journal_mode=WAL");
                if (v == 0)
                {
                    Run("BEGIN IMMEDIATE");
                    try
                    {
                        Run("CREATE TABLE workspaces(id TEXT PRIMARY KEY,name TEXT NOT NULL,created TEXT NOT NULL,updated TEXT NOT NULL,mode INTEGER NOT NULL,delay INTEGER NOT NULL,pause INTEGER NOT NULL,position INTEGER NOT NULL)");
                        Run("CREATE TABLE apps(id TEXT PRIMARY KEY,workspace TEXT NOT NULL REFERENCES workspaces(id) ON DELETE CASCADE,name TEXT NOT NULL,path TEXT NOT NULL,aumid TEXT NOT NULL,position INTEGER NOT NULL,mode INTEGER NOT NULL,delay INTEGER NOT NULL,maxwait INTEGER NOT NULL,retries INTEGER NOT NULL,enabled INTEGER NOT NULL)");
                        Run("PRAGMA user_version=1"); Run("COMMIT");v=1;
                    }
                    catch { Run("ROLLBACK"); throw; }
                }
                if(v==1)
                {
                    // Additive and transactional: existing IDs, application order and launch rules are untouched.
                    Run("BEGIN IMMEDIATE");
                    try
                    {
                        Run("ALTER TABLE apps ADD COLUMN source TEXT NOT NULL DEFAULT 'manual'");
                        Run("ALTER TABLE apps ADD COLUMN icon_reference TEXT NOT NULL DEFAULT ''");
                        Run("ALTER TABLE apps ADD COLUMN shortcut_path TEXT NOT NULL DEFAULT ''");
                        Run("UPDATE apps SET source='packaged' WHERE aumid<>''");
                        Run("CREATE TABLE preferences(key TEXT PRIMARY KEY,value TEXT NOT NULL)");
                        Run("PRAGMA user_version=2");Run("COMMIT");
                    }
                    catch{Run("ROLLBACK");throw;}
                }
            }
            catch { Dispose(); throw; }
        }
        string Error() { return Marshal.PtrToStringUni(sqlite3_errmsg16(db)); }
        IntPtr Prepare(string sql, params string[] args)
        {
            IntPtr s;
            if (sqlite3_prepare16_v2(db,sql,-1,out s,IntPtr.Zero)!=0) throw new IOException(Error());
            for(int i=0;i<args.Length;i++) if(sqlite3_bind_text16(s,i+1,args[i],-1,new IntPtr(-1))!=0) { sqlite3_finalize(s); throw new IOException(Error()); }
            return s;
        }
        void Run(string sql, params string[] args)
        {
            IntPtr s=Prepare(sql,args);
            try { int r=sqlite3_step(s); if(r!=100 && r!=101) throw new IOException(Error()); }
            finally { sqlite3_finalize(s); }
        }
        List<string[]> Read(string sql, int columns=1, params string[] args)
        {
            var rows=new List<string[]>(); IntPtr s=Prepare(sql,args);
            try { int r; while((r=sqlite3_step(s))==100) { var row=new string[columns]; for(int i=0;i<columns;i++) row[i]=Marshal.PtrToStringUni(sqlite3_column_text16(s,i)); rows.Add(row); } if(r!=101) throw new IOException(Error()); }
            finally { sqlite3_finalize(s); }
            return rows;
        }
        public List<Workspace> Load()
        {
            var list=new List<Workspace>();
            foreach(var r in Read("SELECT id,name,created,updated,mode,delay,pause FROM workspaces ORDER BY position",7))
            {
                var w=new Workspace { Id=r[0], Name=r[1], Created=DateTime.Parse(r[2],CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind), Updated=DateTime.Parse(r[3],CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind), DefaultMode=(LaunchMode)Int32.Parse(r[4]), DefaultDelayMs=Int32.Parse(r[5]), PauseOnFailure=r[6]=="1" };
                foreach(var a in Read("SELECT id,name,path,aumid,mode,delay,maxwait,retries,enabled,source,icon_reference,shortcut_path FROM apps WHERE workspace=? ORDER BY position",12,w.Id))
                    w.Apps.Add(new AppEntry { Id=a[0],Name=a[1],Path=a[2],Aumid=a[3],Mode=(LaunchMode)Int32.Parse(a[4]),DelayMs=Int32.Parse(a[5]),MaxWaitMs=Int32.Parse(a[6]),Retries=Int32.Parse(a[7]),Enabled=a[8]=="1",Source=a[9],IconReference=a[10],ShortcutPath=a[11] });
                Rules.Validate(w); list.Add(w);
            }
            return list;
        }
        public void Save(List<Workspace> workspaces)
        {
            foreach(var w in workspaces) Rules.Validate(w);
            Run("BEGIN IMMEDIATE");
            try
            {
                Run("DELETE FROM apps"); Run("DELETE FROM workspaces");
                for(int i=0;i<workspaces.Count;i++)
                {
                    var w=workspaces[i];
                    Run("INSERT INTO workspaces VALUES(?,?,?,?,?,?,?,?)",w.Id,w.Name,w.Created.ToString("o"),w.Updated.ToString("o"),((int)w.DefaultMode).ToString(),w.DefaultDelayMs.ToString(),w.PauseOnFailure?"1":"0",i.ToString());
                    for(int j=0;j<w.Apps.Count;j++)
                    {
                        var a=w.Apps[j];
                        Run("INSERT INTO apps VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?,?)",a.Id,w.Id,a.Name,a.Path,a.Aumid,j.ToString(),((int)a.Mode).ToString(),a.DelayMs.ToString(),a.MaxWaitMs.ToString(),a.Retries.ToString(),a.Enabled?"1":"0",a.Source,a.IconReference,a.ShortcutPath);
                    }
                }
                Run("COMMIT");
            }
            catch { Run("ROLLBACK"); throw; }
        }
        public Preferences LoadPreferences()
        {
            var result=new Preferences();
            foreach(var row in Read("SELECT key,value FROM preferences",2))
            {
                if(row[0]=="theme"){Appearance theme;if(Enum.TryParse(row[1],out theme)&&Enum.IsDefined(typeof(Appearance),theme))result.Theme=theme;}
                if(row[0]=="start_with_windows")result.StartWithWindows=row[1]=="1";
                if(row[0]=="close_to_tray")result.CloseToTray=row[1]!="0";
            }
            return result;
        }
        public void SavePreferences(Preferences preferences)
        {
            if(!Enum.IsDefined(typeof(Appearance),preferences.Theme))throw new InvalidDataException("Invalid appearance preference.");
            Run("BEGIN IMMEDIATE");
            try
            {
                Run("INSERT OR REPLACE INTO preferences VALUES(?,?)","theme",preferences.Theme.ToString());
                Run("INSERT OR REPLACE INTO preferences VALUES(?,?)","start_with_windows",preferences.StartWithWindows?"1":"0");
                Run("INSERT OR REPLACE INTO preferences VALUES(?,?)","close_to_tray",preferences.CloseToTray?"1":"0");Run("COMMIT");
            }catch{Run("ROLLBACK");throw;}
        }
        public void Dispose() { if(db!=IntPtr.Zero) { sqlite3_close(db); db=IntPtr.Zero; } }
    }
}
