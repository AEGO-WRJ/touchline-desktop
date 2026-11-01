using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Documents;
using System.Windows.Media;

namespace Touchline {
 public class PitchView:Canvas {
  class Marker {public Button Button;public PitchPosition Position;public bool Away;}
  readonly List<Marker> markers=new List<Marker>();
  readonly bool full;
  static Brush Color(string hex){return (Brush)new BrushConverter().ConvertFromString(hex);}
  public static TextBlock PlayerLabel(MatchPlayer player,double size,Brush ink,bool bold){
   var label=new TextBlock{FontSize=size,Foreground=ink,FontWeight=bold?FontWeights.Bold:FontWeights.Normal,TextWrapping=TextWrapping.Wrap};
   if(player.SubbedIn)label.Inlines.Add(new Run("↑ "){Foreground=Color("#8FEDBD"),FontWeight=FontWeights.Bold});
   if(player.SubbedOut)label.Inlines.Add(new Run("↓ "){Foreground=Color("#F3CF80"),FontWeight=FontWeights.Bold});
   label.Inlines.Add(new Run(player.Name));return label;
  }
  public PitchView(TeamSquad home,TeamSquad away,bool current,Action<MatchPlayer,bool> select){
   full=away!=null;ClipToBounds=true;Background=Color("#28694D");
   AddTeam(home,false,current,select);if(full)AddTeam(away,true,current,select);
   SizeChanged+=(s,e)=>PositionMarkers();
  }
  void AddTeam(TeamSquad team,bool away,bool current,Action<MatchPlayer,bool> select){
   foreach(var position in PitchLayout.Build(team,current)){
    var p=position.Player;var content=new StackPanel();
    var number=new TextBlock{Text=String.IsNullOrEmpty(p.Number)?"—":p.Number,FontSize=14,FontWeight=FontWeights.Bold,Foreground=Color(away?"#203B35":"#FFFFFF"),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
    content.Children.Add(new Border{Width=34,Height=34,CornerRadius=new CornerRadius(17),Background=Color(away?"#F5F3DF":"#DA614F"),BorderBrush=Color("#F6F6E8"),BorderThickness=new Thickness(1.5),Child=number,HorizontalAlignment=HorizontalAlignment.Center});
    content.Children.Add(new Border{Width=32,Height=15,CornerRadius=new CornerRadius(4),Background=Color(p.Rating.HasValue?(p.Rating>=7?"#A3E8AF":"#F4D18A"):"#D2DCD1"),HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,1,0,2),Child=new TextBlock{Text=p.Rating.HasValue?p.Rating.Value.ToString("0.0",CultureInfo.InvariantCulture):"—",Foreground=Color("#17352A"),FontSize=10,FontWeight=FontWeights.Bold,TextAlignment=TextAlignment.Center}});
    var name=PlayerLabel(p,10,Brushes.White,false);name.Background=Color("#7021372C");name.TextAlignment=TextAlignment.Center;name.TextTrimming=TextTrimming.CharacterEllipsis;name.TextWrapping=TextWrapping.NoWrap;content.Children.Add(name);
    string status=p.SentOff?" · 红牌离场":p.SubbedOut?" · 已换下":p.SubbedIn?" · 替补上场":"";
    var button=new Button{Tag=p,Content=content,Height=68,Padding=new Thickness(0),BorderThickness=new Thickness(0),Background=Brushes.Transparent,Cursor=Cursors.Hand,ToolTip=p.Number+" · "+p.Name+"\n"+p.Position+status+"\n点击查看球员数据",HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Top};
    var template=new ControlTemplate(typeof(Button));template.VisualTree=new FrameworkElementFactory(typeof(ContentPresenter));button.Template=template;
    System.Windows.Automation.AutomationProperties.SetName(button,p.Number+"号 "+p.Name+" 球员数据");
    button.Click+=(s,e)=>select(p,away);button.MouseEnter+=(s,e)=>content.Opacity=.8;button.MouseLeave+=(s,e)=>content.Opacity=1;
    markers.Add(new Marker{Button=button,Position=position,Away=away});Children.Add(button);
   }
  }
  void PositionMarkers(){
   double w=ActualWidth,h=ActualHeight;if(w<80||h<80)return;
   foreach(var marker in markers){
    double x,y;var p=marker.Position;
    if(full){x=25+p.Y*(w/2-45);y=36+(1-p.X)*(h-72);if(marker.Away){x=w-x;y=h-y;}}
    else{x=20+(1-p.X)*(w-40);y=36+(1-p.Y)*(h-72);}
    double width=full?Math.Min(90,w/12):Math.Min(72,(w-40)/5);
    marker.Button.Width=width;SetLeft(marker.Button,Math.Max(0,Math.Min(w-width,x-width/2)));SetTop(marker.Button,Math.Max(0,Math.Min(h-68,y-25)));
   }
   InvalidateVisual();
  }
  protected override void OnRender(DrawingContext dc){
   base.OnRender(dc);double w=ActualWidth,h=ActualHeight;if(w<20||h<20)return;
   dc.DrawRectangle(Color("#28694D"),null,new Rect(0,0,w,h));
   for(int i=0;i<8;i+=2)dc.DrawRectangle(Color("#2E7556"),null,full?new Rect(w*i/8,0,w/8,h):new Rect(0,h*i/8,w,h/8));
   var pen=new Pen(Color("#A6CDB3"),1.5);var dot=Color("#A6CDB3");
   dc.DrawRoundedRectangle(null,pen,new Rect(8,8,w-16,h-16),8,8);
   if(full){
    dc.DrawLine(pen,new Point(w/2,8),new Point(w/2,h-8));dc.DrawEllipse(null,pen,new Point(w/2,h/2),h*.13,h*.13);dc.DrawEllipse(dot,null,new Point(w/2,h/2),2,2);
    foreach(bool right in new[]{false,true}){
     dc.DrawRectangle(null,pen,new Rect(right?w-8-w*.15:8,h*.23,w*.15,h*.54));dc.DrawRectangle(null,pen,new Rect(right?w-8-w*.055:8,h*.38,w*.055,h*.24));dc.DrawEllipse(dot,null,new Point(right?w-w*.11:w*.11,h/2),2,2);
    }
   }else{
    dc.PushClip(new RectangleGeometry(new Rect(8,8,w-16,h-16)));dc.DrawEllipse(null,pen,new Point(w/2,8),w*.18,w*.18);dc.Pop();
    dc.DrawRectangle(null,pen,new Rect(w*.18,h-8-h*.23,w*.64,h*.23));dc.DrawRectangle(null,pen,new Rect(w*.34,h-8-h*.08,w*.32,h*.08));dc.DrawEllipse(dot,null,new Point(w/2,h-8-h*.16),2,2);
   }
  }
 }
}
