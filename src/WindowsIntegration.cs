using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using Microsoft.Win32;

namespace WorkplaceOrchestrator
{
    public static class WindowsIntegration
    {
        public const string AppId="Workplace.Orchestrator";
        const string RunKey=@"Software\Microsoft\Windows\CurrentVersion\Run";
        [DllImport("shell32.dll",CharSet=CharSet.Unicode)]static extern int SetCurrentProcessExplicitAppUserModelID(string id);
        [DllImport("shell32.dll")]static extern void SHChangeNotify(uint eventId,uint flags,IntPtr item1,IntPtr item2);
        [ComImport,Guid("00021401-0000-0000-C000-000000000046")]class ShellLink{}
        [ComImport,Guid("000214F9-0000-0000-C000-000000000046"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]interface IShellLink
        {
            void GetPath([Out,MarshalAs(UnmanagedType.LPWStr)]StringBuilder path,int max,IntPtr data,uint flags);
            void GetIDList(out IntPtr list);void SetIDList(IntPtr list);void GetDescription([Out,MarshalAs(UnmanagedType.LPWStr)]StringBuilder name,int max);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)]string name);void GetWorkingDirectory([Out,MarshalAs(UnmanagedType.LPWStr)]StringBuilder path,int max);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)]string path);void GetArguments([Out,MarshalAs(UnmanagedType.LPWStr)]StringBuilder args,int max);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)]string args);void GetHotkey(out short key);void SetHotkey(short key);void GetShowCmd(out int show);void SetShowCmd(int show);
            void GetIconLocation([Out,MarshalAs(UnmanagedType.LPWStr)]StringBuilder path,int max,out int index);void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)]string path,int index);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)]string path,uint reserved);void Resolve(IntPtr window,uint flags);void SetPath([MarshalAs(UnmanagedType.LPWStr)]string path);
        }
        [StructLayout(LayoutKind.Sequential,Pack=4)]struct PropertyKey {public Guid Format;public uint Id;}
        [StructLayout(LayoutKind.Explicit)]struct PropVariant
        {
            [FieldOffset(0)]public ushort Type;
            [FieldOffset(8)]public IntPtr Text;
            [FieldOffset(8)]public ulong ForceNativeSize;
        }
        [ComImport,Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]interface IPropertyStore
        {
            void GetCount(out uint count);
            void GetAt(uint index,[Out,MarshalAs(UnmanagedType.Struct)]out PropertyKey key);
            void GetValue([In,MarshalAs(UnmanagedType.Struct)]ref PropertyKey key,[Out,MarshalAs(UnmanagedType.Struct)]out PropVariant value);
            void SetValue([In,MarshalAs(UnmanagedType.Struct)]ref PropertyKey key,[In,MarshalAs(UnmanagedType.Struct)]ref PropVariant value);
            void Commit();
        }
        public static void SetIdentity(){Marshal.ThrowExceptionForHR(SetCurrentProcessExplicitAppUserModelID(AppId));}
        public static void RegisterShortcut()
        {
            string exe=Assembly.GetExecutingAssembly().Location;
            string path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),"Workplace Orchestrator.lnk");
            if(File.Exists(path))
            {
                File.Delete(path);
                NotifyPath(4,path);
            }
            object instance=new ShellLink();
            try
            {
                var link=(IShellLink)instance;link.SetPath(exe);link.SetWorkingDirectory(Path.GetDirectoryName(exe));link.SetDescription("Workplace Orchestrator — local application workspaces");link.SetIconLocation(exe,0);link.SetShowCmd(1);
                var key=new PropertyKey{Format=new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),Id=5};var value=new PropVariant{Type=31,Text=Marshal.StringToCoTaskMemUni(AppId)};
                try{var props=(IPropertyStore)instance;props.SetValue(ref key,ref value);props.Commit();}finally{Marshal.FreeCoTaskMem(value.Text);}
                ((IPersistFile)instance).Save(path,true);
                NotifyPath(2,path);
                NotifyPath(0x1000,Path.GetDirectoryName(path));
                SHChangeNotify(0x08000000,0,IntPtr.Zero,IntPtr.Zero);
            }finally{Marshal.ReleaseComObject(instance);}
        }
        static void NotifyPath(uint eventId,string path)
        {
            IntPtr notification=Marshal.StringToCoTaskMemUni(path);
            try{SHChangeNotify(eventId,0x1005,notification,IntPtr.Zero);}finally{Marshal.FreeCoTaskMem(notification);}
        }
        public static string StartupCommand(string executable){return "\""+Path.GetFullPath(executable)+"\" --background";}
        public static void SetStartup(bool enabled)
        {
            using(var key=Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if(enabled)key.SetValue("WorkplaceOrchestrator",StartupCommand(Assembly.GetExecutingAssembly().Location),RegistryValueKind.String);
                else key.DeleteValue("WorkplaceOrchestrator",false);
            }
        }
        public static bool StartupEnabled()
        {
            using(var key=Registry.CurrentUser.OpenSubKey(RunKey))return key!=null&&String.Equals(key.GetValue("WorkplaceOrchestrator") as string,StartupCommand(Assembly.GetExecutingAssembly().Location),StringComparison.OrdinalIgnoreCase);
        }
    }
}
