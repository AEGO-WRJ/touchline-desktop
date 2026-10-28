using System;
namespace Touchline {
 public class Preferences {
  public double Left=Double.NaN,Top=Double.NaN,Width=420,Height=740,Opacity=1,CompactOpacity=.45;
  public bool Pinned=true,Sound=false,Compact=false;
  public bool StartInTray=true,AutoCheckUpdates=true,AutoDownloadUpdates=true;
  public bool ShowCups=true;
  public string LastUpdateCheck;
  public FavoriteSelection Favorites=new FavoriteSelection();
  public void SetCompact(bool compact,double width,double height) {
   if(Compact==compact)return;
   if(!Compact){Width=width;Height=height;}
   Compact=compact;
  }
  public void CaptureLayout(double left,double top,double width,double height,double opacity) {
   Left=left;Top=top;
   if(Compact)CompactOpacity=LimitOpacity(opacity);
   else {Width=width;Height=height;Opacity=LimitOpacity(opacity);}
  }
  public double ActiveOpacity() { return LimitOpacity(Compact?CompactOpacity:Opacity); }
  public static double LimitOpacity(double opacity) { return Double.IsNaN(opacity)||Double.IsInfinity(opacity)?1:Math.Max(.1,Math.Min(1,opacity)); }
 }
}
