using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace WorkplaceOrchestrator
{
    public sealed class ThemeManager : IDisposable
    {
        readonly Window window;
        Appearance preference;
        bool disposed;
        public bool IsDark {get;private set;}
        [DllImport("dwmapi.dll")]static extern int DwmSetWindowAttribute(IntPtr window,int attribute,ref int value,int size);
        public ThemeManager(Window window){this.window=window;SystemEvents.UserPreferenceChanged+=SystemChanged;window.SourceInitialized+=(s,e)=>Caption();}
        public static bool SystemDark()
        {
            try{using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))return key!=null&&Convert.ToInt32(key.GetValue("AppsUseLightTheme",1))==0;}catch{return false;}
        }
        void SystemChanged(object sender,UserPreferenceChangedEventArgs e){if(!disposed&&preference==Appearance.System)window.Dispatcher.BeginInvoke(new Action(()=>{if(!disposed)Apply(preference);}));}
        void Brush(string name,string light,string dark){var brush=new SolidColorBrush((Color)ColorConverter.ConvertFromString(IsDark?dark:light));brush.Freeze();window.Resources[name]=brush;}
        public void Apply(Appearance appearance)
        {
            if(!Enum.IsDefined(typeof(Appearance),appearance))throw new ArgumentException("Unknown appearance.");
            preference=appearance;IsDark=appearance==Appearance.Dark||(appearance==Appearance.System&&SystemDark());
            Brush("BackgroundBrush","#F4F6FA","#171D26");Brush("SurfaceBrush","#FFFFFF","#222B38");Brush("TextBrush","#23334C","#EDF2F9");
            Brush("MutedBrush","#526278","#B6C4D8");Brush("BorderBrush","#D6DFEC","#43526A");Brush("SubtleBrush","#EAF0F8","#2C394D");
            Brush("AccentBrush","#285EBB","#689DF5");Brush("SelectionBrush","#DAE7FD","#354F75");
            Brush("SuccessBrush","#167057","#66D7B0");Brush("ErrorBrush","#AC3238","#FF999C");Brush("WaitBrush","#86550F","#F1C56B");
            // Standard WPF popups use system keys; scoped overrides avoid bright menus in dark mode.
            window.Resources[SystemColors.WindowBrushKey]=window.Resources["SurfaceBrush"];
            window.Resources[SystemColors.WindowTextBrushKey]=window.Resources["TextBrush"];
            window.Resources[SystemColors.ControlBrushKey]=window.Resources["SurfaceBrush"];
            window.Resources[SystemColors.ControlTextBrushKey]=window.Resources["TextBrush"];
            window.Resources[SystemColors.HighlightBrushKey]=window.Resources["SelectionBrush"];
            window.Resources[SystemColors.HighlightTextBrushKey]=window.Resources["TextBrush"];
            window.Resources[SystemColors.MenuBrushKey]=window.Resources["SurfaceBrush"];
            window.Resources[SystemColors.MenuTextBrushKey]=window.Resources["TextBrush"];
            Caption();
        }
        void Caption(){var handle=new WindowInteropHelper(window).Handle;if(handle==IntPtr.Zero)return;int dark=IsDark?1:0;try{DwmSetWindowAttribute(handle,20,ref dark,4);}catch(DllNotFoundException){}catch(EntryPointNotFoundException){}}
        public void Dispose(){if(disposed)return;disposed=true;SystemEvents.UserPreferenceChanged-=SystemChanged;}
    }
}
