using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WorkplaceOrchestrator
{
    public static class ApplicationMetadata
    {
        [DllImport("shell32.dll",CharSet=CharSet.Unicode)]static extern uint ExtractIconEx(string file,int index,IntPtr[] large,IntPtr[] small,uint count);
        [DllImport("user32.dll")]static extern bool DestroyIcon(IntPtr icon);
        static readonly Dictionary<string,ImageSource> memory=new Dictionary<string,ImageSource>();
        static readonly object gate=new object();
        static string cacheDirectory;
        public static void ConfigureCache(string directory){cacheDirectory=Path.Combine(directory,"icons");}
        public static string DisplayName(string executable)
        {
            try
            {
                var info=FileVersionInfo.GetVersionInfo(executable);
                string name=!String.IsNullOrWhiteSpace(info.ProductName)?info.ProductName:info.FileDescription;
                if(!String.IsNullOrWhiteSpace(name))return name.Trim();
            }catch{}
            return Path.GetFileNameWithoutExtension(executable);
        }
        public static DiscoveredApp Manual(string executable)
        {
            return new DiscoveredApp{Name=DisplayName(executable),Path=executable,Aumid="",Source="manual",IconReference=executable};
        }
        public static ImageSource Icon(string executable,string reference)
        {
            string source=String.IsNullOrWhiteSpace(reference)?executable:Environment.ExpandEnvironmentVariables(reference);
            // Do not resolve network metadata from a tampered configuration.
            if(source.StartsWith(@"\\")||source.Contains("://"))source=executable;
            string file=source;int index=0;int comma=source.LastIndexOf(',');
            if(comma>0&&Int32.TryParse(source.Substring(comma+1),out index))file=source.Substring(0,comma).Trim('"');
            try{if(new DriveInfo(Path.GetPathRoot(file)).DriveType==DriveType.Network)file=executable;}catch{file=executable;}
            string key=file+"|"+index+"|"+(File.Exists(file)?File.GetLastWriteTimeUtc(file).Ticks:0);
            lock(gate)
            {
                ImageSource result;if(memory.TryGetValue(key,out result))return result;
                string cached=null;
                if(cacheDirectory!=null)try
                {
                    using(var hash=SHA256.Create())cached=Path.Combine(cacheDirectory,BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(key))).Replace("-","")+".png");
                    if(File.Exists(cached))result=ReadImage(cached);
                }catch{cached=null;}
                if(result==null)
                {
                    try
                    {
                        string extension=Path.GetExtension(file).ToLowerInvariant();
                        if(extension==".png"||extension==".jpg"||extension==".jpeg"||extension==".ico")result=ReadImage(file);
                        else
                        {
                            var large=new IntPtr[1];var small=new IntPtr[1];
                            try{if(ExtractIconEx(file,index,large,small,1)>0&&large[0]!=IntPtr.Zero){var image=Imaging.CreateBitmapSourceFromHIcon(large[0],Int32Rect.Empty,BitmapSizeOptions.FromWidthAndHeight(40,40));image.Freeze();result=image;}}
                            finally{if(large[0]!=IntPtr.Zero)DestroyIcon(large[0]);if(small[0]!=IntPtr.Zero)DestroyIcon(small[0]);}
                        }
                    }catch{}
                    if(result==null&&!Rules.SamePath(file,executable))result=Icon(executable,executable);
                    if(result!=null&&cached!=null)try
                    {
                        Directory.CreateDirectory(cacheDirectory);
                        var image=result as BitmapSource;if(image!=null){var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using(var output=new FileStream(cached,FileMode.Create,FileAccess.Write,FileShare.None))encoder.Save(output);}
                        // Cache size is bounded and reconstructible, not an application-history log.
                        foreach(var old in new DirectoryInfo(cacheDirectory).GetFiles("*.png").OrderByDescending(f=>f.LastWriteTimeUtc).Skip(256))old.Delete();
                    }catch{}
                }
                if(result==null){var drawing=new GeometryDrawing(new SolidColorBrush(Color.FromRgb(102,132,174)),null,Geometry.Parse("M2,4 L30,4 30,28 2,28 Z M5,10 L5,25 27,25 27,10 Z"));var fallback=new DrawingImage(drawing);fallback.Freeze();result=fallback;}
                if(memory.Count>=256)memory.Clear();memory[key]=result;return result;
            }
        }
        static ImageSource ReadImage(string file)
        {
            var image=new BitmapImage();image.BeginInit();image.UriSource=new Uri(Path.GetFullPath(file));image.CacheOption=BitmapCacheOption.OnLoad;image.DecodePixelWidth=48;image.EndInit();image.Freeze();return image;
        }
    }
}
