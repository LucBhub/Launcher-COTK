using System.Windows.Media;
using System.Windows.Media.Imaging;
using COTK.Next.Core;

namespace COTK.Next.UI;

internal static class ClientRankAssets
{
    private static readonly Dictionary<int, ImageSource?> Cache = new();
    public static ImageSource? Load(int tier)
    {
        if(tier<1 || tier>6) return null;
        if(Cache.TryGetValue(tier,out var existing)) return existing;
        ImageSource? image=null;
        var file=Path.Combine(AppPaths.InstallDir,"ranks",$"tier-{tier}.png");
        if(File.Exists(file))
        {
            var bitmap=new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption=BitmapCacheOption.OnLoad;
            bitmap.UriSource=new Uri(file); bitmap.EndInit(); bitmap.Freeze(); image=bitmap;
        }
        Cache[tier]=image;
        return image;
    }
}
