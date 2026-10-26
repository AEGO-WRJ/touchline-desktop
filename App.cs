using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Web.Script.Serialization;
using Forms=System.Windows.Forms;

namespace Touchline {
 public class Preferences {
  public double Left=Double.NaN,Top=Double.NaN,Width=420,Height=740,Opacity=1;
  public bool Pinned=true,Sound=false;
  public FavoriteSelection Favorites=new FavoriteSelection();
 }
 public class CardWindow:Window {
  readonly string dataDir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TouchlineCard");
  readonly ScoreFeed feed;
  readonly FavoriteStore favoriteStore;
  Preferences preferences=new Preferences();
  LeagueResult[] results=new LeagueResult[0];
  DateTime day=MatchData.BeijingToday;
  DateTime rangeStart=MatchData.BeijingToday,rangeEnd=MatchData.BeijingToday.AddDays(6);
  bool liveOnly,loading,refreshAgain,forceNext,exiting,jumpToToday;
  readonly TeamDirectory teamDirectory=new TeamDirectory();
  readonly Dictionary<string,BitmapImage> logoCache=new Dictionary<string,BitmapImage>();
  string filter="all",expanded;
  ScoreTracker tracker=new ScoreTracker();
  DateTimeOffset due=DateTimeOffset.UtcNow,noticeUntil=DateTimeOffset.MinValue;
  TextBlock summary,footer,notice;
  Border noticeBox;
  Border favoriteBadge;
  string badgeTeamId;
  StackPanel list;
  ScrollViewer scroll;
  Button pinButton,liveButton,refreshButton,favoritesButton;
  CheckBox favoritesToggle;
  readonly Dictionary<string,Button> tabs=new Dictionary<string,Button>();
  readonly DispatcherTimer timer=new DispatcherTimer();
  Forms.NotifyIcon tray;
  static readonly Brush Ink=B("#EAF2F3"),Muted=B("#91A2A9"),Green=B("#63E7AE"),Amber=B("#F1C779");
  public CardWindow() {
   Directory.CreateDirectory(dataDir);feed=new ScoreFeed(Path.Combine(dataDir,"cache"));
   try { var file=Path.Combine(dataDir,"settings.json");if(File.Exists(file)) preferences=new JavaScriptSerializer().Deserialize<Preferences>(File.ReadAllText(file))??new Preferences(); } catch(Exception) {}
   favoriteStore=new FavoriteStore(dataDir);preferences.Favorites=favoriteStore.Load();
   Title="看球 · 桌面比分";FontFamily=new FontFamily("Microsoft YaHei UI, Segoe UI");FontSize=12;Foreground=Ink;
   Width=Clamp(preferences.Width,380,560);Height=Clamp(preferences.Height,440,1100);MinWidth=380;MinHeight=440;MaxWidth=560;
   WindowStyle=WindowStyle.None;AllowsTransparency=true;Background=Brushes.Transparent;ResizeMode=ResizeMode.CanResizeWithGrip;
   Topmost=preferences.Pinned;Opacity=Clamp(preferences.Opacity,.7,1);
   var area=SystemParameters.WorkArea;
   Left=Double.IsNaN(preferences.Left)?area.Right-Width-24:Math.Max(area.Left,Math.Min(preferences.Left,area.Right-Width));
   Top=Double.IsNaN(preferences.Top)?area.Top+45:Math.Max(area.Top,Math.Min(preferences.Top,area.Bottom-Math.Min(Height,area.Height)));
   Height=Math.Min(Height,area.Height-20);
   Resources=(ResourceDictionary)XamlReader.Parse(Styles);
   Build(); SetupTray();
   if(!String.IsNullOrEmpty(favoriteStore.Warning)){notice.Text=favoriteStore.Warning;notice.Foreground=Amber;noticeBox.Visibility=Visibility.Visible;noticeUntil=DateTimeOffset.UtcNow.AddMinutes(2);}
   timer.Interval=TimeSpan.FromSeconds(1);timer.Tick+=async (s,e)=>{
    if(day!=MatchData.BeijingToday) {day=MatchData.BeijingToday;if(rangeEnd<day.AddDays(6))rangeEnd=day.AddDays(6);due=DateTimeOffset.UtcNow;Render();}
    if(noticeBox.Visibility==Visibility.Visible && DateTimeOffset.UtcNow>noticeUntil) noticeBox.Visibility=Visibility.Collapsed;
    if(DateTimeOffset.UtcNow>=due && !loading) await Refresh();
   };timer.Start();
   Loaded+=async (s,e)=>await Refresh();
   StateChanged+=(s,e)=>{if(WindowState==WindowState.Minimized){Hide();WindowState=WindowState.Normal;}};
   Closing+=(s,e)=>{exiting=true;Save();timer.Stop();if(tray!=null){tray.Visible=false;tray.Dispose();}};
  }
  static double Clamp(double x,double min,double max) { return Double.IsNaN(x)||Double.IsInfinity(x)?max:Math.Max(min,Math.Min(x,max)); }
  static Brush B(string hex) { return (Brush)new BrushConverter().ConvertFromString(hex); }
  static TextBlock T(string text,double size=12,Brush color=null,bool bold=false) {
   return new TextBlock{Text=text,FontSize=size,Foreground=color??Ink,FontWeight=bold?FontWeights.SemiBold:FontWeights.Normal,VerticalAlignment=VerticalAlignment.Center,TextWrapping=TextWrapping.Wrap};
  }
  Button Btn(string text,Action action,string tip=null) {
   var b=new Button{Content=text,ToolTip=tip??text,Padding=new Thickness(10,7,10,7),Margin=new Thickness(0,0,4,0),Cursor=Cursors.Hand};
   b.Click+=(s,e)=>action();return b;
  }
  static Grid Cols(params double[] widths) {
   var g=new Grid();foreach(double w in widths)g.ColumnDefinitions.Add(new ColumnDefinition{Width=w<0?new GridLength(-w,GridUnitType.Star):new GridLength(w)});return g;
  }
  static void At(Grid g,UIElement item,int column) { Grid.SetColumn(item,column);g.Children.Add(item); }
  void Build() {
   var shell=new Border{Background=B("#111C22"),BorderBrush=B("#31424A"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(18),Margin=new Thickness(8),Padding=new Thickness(18,17,18,12)};
   shell.Effect=new System.Windows.Media.Effects.DropShadowEffect{Color=Colors.Black,BlurRadius=14,Opacity=.35,ShadowDepth=3};Content=shell;
   var root=new Grid();shell.Child=root;
   foreach(var size in new[]{GridLength.Auto,GridLength.Auto,GridLength.Auto,GridLength.Auto,GridLength.Auto,GridLength.Auto,new GridLength(1,GridUnitType.Star),GridLength.Auto})root.RowDefinitions.Add(new RowDefinition{Height=size});
   var header=Cols(-1,0);header.ColumnDefinitions[1].Width=GridLength.Auto;
   var brand=new StackPanel{Cursor=Cursors.SizeAll};brand.Children.Add(T("TOUCHLINE  /  比分随行",9,Green,true));
   var titleRow=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,3,0,0)};favoriteBadge=new Border{Margin=new Thickness(0,0,9,0),VerticalAlignment=VerticalAlignment.Center,Visibility=Visibility.Collapsed};titleRow.Children.Add(favoriteBadge);titleRow.Children.Add(T("看球",25,Ink,true));brand.Children.Add(titleRow);
   brand.MouseLeftButtonDown+=(s,e)=>{if(e.ButtonState==MouseButtonState.Pressed) {try{DragMove();}catch(InvalidOperationException){} }};At(header,brand,0);
   var controls=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Top};
   pinButton=Btn(Topmost?"已置顶":"置顶",()=>{Topmost=!Topmost;pinButton.Content=Topmost?"已置顶":"置顶";pinButton.Foreground=Topmost?Green:Muted;Save();},"切换窗口置顶");pinButton.FontSize=10;pinButton.Padding=new Thickness(8,6,8,6);pinButton.Foreground=Topmost?Green:Muted;
   controls.Children.Add(pinButton);controls.Children.Add(Btn("−",()=>Hide(),"收起到系统托盘，双击托盘图标恢复"));controls.Children.Add(Btn("×",()=>Close(),"退出看球"));At(header,controls,1);root.Children.Add(header);
   var dateBar=Cols(-1,0);dateBar.ColumnDefinitions[1].Width=GridLength.Auto;dateBar.Margin=new Thickness(0,17,0,14);Grid.SetRow(dateBar,1);root.Children.Add(dateBar);
   var today=Btn("今天",JumpToday,"回到今天的赛程位置");today.HorizontalAlignment=HorizontalAlignment.Left;today.Foreground=Green;At(dateBar,today,0);
   favoritesButton=Btn("关注球队",ShowFavorites,"添加或取消关注球队，支持多选");At(dateBar,favoritesButton,1);
   var filters=new UniformGrid{Columns=7,Margin=new Thickness(0,0,0,13)};Grid.SetRow(filters,2);root.Children.Add(filters);
   AddTab(filters,"all","全部");foreach(var league in League.All) AddTab(filters,league.Id,league.Name);
   var line=Cols(-1,0);line.ColumnDefinitions[1].Width=GridLength.Auto;Grid.SetRow(line,3);line.Margin=new Thickness(0,0,0,12);root.Children.Add(line);
   summary=T("正在读取今日赛程…",11,Muted);At(line,summary,0);liveButton=Btn("只看进行中",()=>{liveOnly=!liveOnly;liveButton.Foreground=liveOnly?Green:Muted;Render();});liveButton.FontSize=10;liveButton.Padding=new Thickness(7,4,7,4);At(line,liveButton,1);
   notice=T("",11,Green);noticeBox=new Border{Child=notice,Background=B("#183B30"),CornerRadius=new CornerRadius(8),Padding=new Thickness(10),Margin=new Thickness(0,0,0,10),Visibility=Visibility.Collapsed};Grid.SetRow(noticeBox,4);root.Children.Add(noticeBox);
   var options=Cols(-1,0);options.ColumnDefinitions[1].Width=GridLength.Auto;options.Margin=new Thickness(1,0,0,12);Grid.SetRow(options,5);root.Children.Add(options);
   favoritesToggle=new CheckBox{Content="只看关注球队",IsChecked=preferences.Favorites.OnlyFavorites,Foreground=Ink,FontSize=11,VerticalAlignment=VerticalAlignment.Center};
   favoritesToggle.Click+=(s,e)=>{preferences.Favorites.OnlyFavorites=favoritesToggle.IsChecked==true;scroll.ScrollToTop();PersistFavorites();Render();};At(options,favoritesToggle,0);At(options,T("北京时间 UTC+8",10,Muted),1);
   list=new StackPanel();scroll=new ScrollViewer{Content=list,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Padding=new Thickness(0,0,3,0)};Grid.SetRow(scroll,6);root.Children.Add(scroll);
   var bottom=new StackPanel{Margin=new Thickness(0,12,0,0)};Grid.SetRow(bottom,7);root.Children.Add(bottom);
   var footRow=Cols(-1,0,0);footRow.ColumnDefinitions[1].Width=GridLength.Auto;footRow.ColumnDefinitions[2].Width=GridLength.Auto;bottom.Children.Add(footRow);
   footer=T("正在连接 ESPN…",10,Muted);At(footRow,footer,0);refreshButton=Btn("↻",async ()=>await Refresh(true),"立即刷新全部已加载赛程");refreshButton.Padding=new Thickness(8,3,8,3);At(footRow,refreshButton,1);
   var settings=Btn("设置",ShowSettings);settings.FontSize=10;settings.Padding=new Thickness(7,4,7,4);At(footRow,settings,2);
   var source=T("数据：ESPN · 可能延迟 · 点击比赛查看数据",9,B("#758A94"));source.Margin=new Thickness(0,6,0,0);bottom.Children.Add(source);
   Render();
  }
  void AddTab(Panel p,string id,string text) {
   var b=Btn(text,()=>{filter=id;Render();});b.FontSize=11;b.Padding=new Thickness(0,7,0,7);b.Margin=new Thickness(0,0,3,0);tabs[id]=b;p.Children.Add(b);
  }
  void JumpToday() {
   day=MatchData.BeijingToday;jumpToToday=true;
   if(rangeStart>day||rangeEnd<day){rangeStart=day;rangeEnd=day.AddDays(6);RefreshSoon();}else Render();
  }
  void ExtendRange(bool earlier) {if(earlier)rangeStart=rangeStart.AddDays(-7);else rangeEnd=rangeEnd.AddDays(7);RefreshSoon();}
  async void RefreshSoon(bool force=false) { await Refresh(force); }
  async System.Threading.Tasks.Task Refresh(bool force=false) {
   if(loading){refreshAgain=true;forceNext|=force;return;}loading=true;refreshButton.IsEnabled=false;footer.Text="正在同步赛程与比分…";
   var requestedStart=rangeStart;var requestedEnd=rangeEnd;
   try {
    var incoming=await feed.LoadRange(requestedStart,requestedEnd,force);if(exiting)return;
    if(rangeStart==requestedStart&&rangeEnd==requestedEnd) {
     results=incoming;
     var fresh=incoming.SelectMany(x=>x.Matches).Where(x=>!x.DataStale).ToList();teamDirectory.Learn(fresh);
     if(requestedStart<=MatchData.BeijingToday&&requestedEnd>=MatchData.BeijingToday) {
      var changes=tracker.Observe(fresh);
      if(changes.Count>0) {
       string text=String.Join("\n",changes.Select(TranslateNotice));notice.Text=text;noticeBox.Visibility=Visibility.Visible;noticeUntil=DateTimeOffset.UtcNow.AddSeconds(15);
       if(preferences.Sound) System.Media.SystemSounds.Asterisk.Play();
       if(!IsVisible && tray!=null) tray.ShowBalloonTip(6000,"比分更新",text,Forms.ToolTipIcon.Info);
      }
     }
     Render();
    }
   } catch(Exception) { footer.Text="同步失败，稍后自动重试";footer.Foreground=Amber; }
   finally {
    loading=false;if(!exiting){refreshButton.IsEnabled=true;due=DateTimeOffset.UtcNow.AddSeconds(results.Any(x=>x.Stale||x.Matches.Any(m=>m.State=="in"))?30:120);if(refreshAgain){refreshAgain=false;bool nextForce=forceNext;forceNext=false;RefreshSoon(nextForce);}}
   }
  }
  string TranslateNotice(string text) { foreach(var m in results.SelectMany(x=>x.Matches)){text=text.Replace(m.Home,Names.Team(m.Home)).Replace(m.Away,Names.Team(m.Away));}return text; }
  void Render() {
   UpdateFavoriteBadge();
   favoritesButton.Content="关注球队 · "+preferences.Favorites.TeamIds.Count;
   foreach(var kv in tabs){kv.Value.Background=B(kv.Key==filter?"#27503F":"#1C2B33");kv.Value.Foreground=kv.Key==filter?Green:Muted;}
   var selected=results.Where(x=>filter=="all"||x.League.Id==filter).ToList();
   var shown=ScheduleView.Select(selected.SelectMany(x=>x.Matches),preferences.Favorites,filter,liveOnly);
   int playing=shown.Count(x=>x.State=="in");summary.Text=shown.Count+" 场比赛   ·   "+playing+" 场进行中";
   if(results.Length==0) summary.Text="正在读取赛程…";
   var offset=scroll.VerticalOffset;
   var anchor=list.Children.OfType<FrameworkElement>().FirstOrDefault(x=>x.Tag is string&&x.TranslatePoint(new Point(0,0),list).Y>=offset);
   string anchorId=anchor==null?null:anchor.Tag as string;double anchorDelta=anchor==null?0:anchor.TranslatePoint(new Point(0,0),list).Y-offset;
   list.Children.Clear();FrameworkElement todayAnchor=null;
   var earlier=Btn("↑  加载更早赛程",()=>ExtendRange(true));earlier.HorizontalAlignment=HorizontalAlignment.Stretch;earlier.Margin=new Thickness(0,0,0,12);earlier.FontSize=10;list.Children.Add(earlier);
   var days=shown.Select(x=>x.Kickoff.ToOffset(TimeSpan.FromHours(8)).Date).Distinct().ToList();
   if(rangeStart<=day&&rangeEnd>=day&&!days.Contains(day))days.Add(day);
   foreach(var date in days.OrderBy(x=>x)) {
    var label=T((date==day?"今天  ·  ":"")+date.ToString("MM月dd日 ddd",CultureInfo.GetCultureInfo("zh-CN")),11,date==day?Green:Muted,true);label.Margin=new Thickness(1,8,0,10);label.Tag="date:"+date.ToString("yyyyMMdd");list.Children.Add(label);if(date==day)todayAnchor=label;
    var dayMatches=shown.Where(x=>x.Kickoff.ToOffset(TimeSpan.FromHours(8)).Date==date).ToList();
    foreach(var m in dayMatches){var card=MatchCard(m,selected.First(x=>x.League.Id==m.League));card.Tag="match:"+m.Id;list.Children.Add(card);}
    if(dayMatches.Count==0&&shown.Count>0) {var no=T(selected.Any(x=>x.Stale)?"今天的赛程暂未完整同步":preferences.Favorites.OnlyFavorites?"今天暂无符合筛选的关注球队比赛":liveOnly?"今天暂无进行中的比赛":"今天暂无符合筛选的比赛",11,Muted);no.Margin=new Thickness(6,5,6,20);list.Children.Add(no);}
   }
   if(shown.Count==0) {
    bool noFavorites=preferences.Favorites.OnlyFavorites&&preferences.Favorites.TeamIds.Count==0;
    string heading=noFavorites?"还没有关注球队":results.Length==0?"正在寻找比赛…":selected.Any(x=>x.Stale)?"赛程暂未同步":liveOnly?"现在没有进行中的比赛":"暂无符合筛选的赛程";
    string body=noFavorites?"点击上方“关注球队”，添加你喜欢的球队。":results.Length==0?"按时间连续查看五大联赛与欧冠。":selected.Any(x=>x.Stale)?"网络或数据源暂时不可用。将自动重试，也可点击右下角刷新。":liveOnly?"关闭“只看进行中”，查看完整赛程。":"可继续加载更多日期，或调整筛选条件。";
    var empty=new StackPanel{Margin=new Thickness(12,24,12,25)};var mark=T(noFavorites?"☆":"⚽",34,Green);mark.HorizontalAlignment=HorizontalAlignment.Center;empty.Children.Add(mark);
    var h=T(heading,16,Ink,true);h.TextAlignment=TextAlignment.Center;h.Margin=new Thickness(0,18,0,10);empty.Children.Add(h);var text=T(body,12,Muted);text.TextAlignment=TextAlignment.Center;empty.Children.Add(text);list.Children.Add(empty);
   }
   var range=T("已加载 "+rangeStart.ToString("MM/dd")+" — "+rangeEnd.ToString("MM/dd"),10,Muted);range.TextAlignment=TextAlignment.Center;range.Margin=new Thickness(0,12,0,8);list.Children.Add(range);
   var more=Btn("加载更多赛程  ↓",()=>ExtendRange(false));more.HorizontalAlignment=HorizontalAlignment.Stretch;more.Margin=new Thickness(0,0,0,8);list.Children.Add(more);
   int failed=results.Count(x=>x.Stale);
   if(results.Length>0) {
    footer.Foreground=failed>0?Amber:Muted;
    footer.Text=failed>0?failed+" 项赛事连接异常 · 30秒重试":"赛中30秒刷新 · 赛程连续排列";
    footer.ToolTip=failed>0?String.Join("\n",results.Where(x=>x.Stale).Select(x=>x.League.Name+"：部分日期可能过期")):"赛中每30秒刷新，其他时间每2分钟检查。未来赛程缓存15分钟；点击刷新可立即重新读取。";
   }
   bool jump=jumpToToday;jumpToToday=false;var todayTarget=todayAnchor;
   Dispatcher.BeginInvoke(new Action(()=>{
    list.UpdateLayout();
    if(jump){scroll.ScrollToVerticalOffset(todayTarget==null?0:todayTarget.TranslatePoint(new Point(0,0),list).Y);return;}
    var restored=anchorId==null?null:list.Children.OfType<FrameworkElement>().FirstOrDefault(x=>Object.Equals(x.Tag,anchorId));
    scroll.ScrollToVerticalOffset(restored==null?offset:restored.TranslatePoint(new Point(0,0),list).Y-anchorDelta);
   }),DispatcherPriority.Loaded);
  }
  Border MatchCard(Match m,LeagueResult result) {
   bool open=m.Id==expanded,live=m.State=="in";
   var outer=new Border{Background=B(open?"#20343B":"#1B2A32"),CornerRadius=new CornerRadius(11),BorderBrush=B(live?"#367C60":open?"#496771":"#293B44"),BorderThickness=new Thickness(1),Margin=new Thickness(0,0,0,9)};
   var stack=new StackPanel();outer.Child=stack;
   var body=new StackPanel{Margin=new Thickness(13,10,13,11)};
   var topline=Cols(-1,-1);var leagueLabel=T(result.League.Name+"  ·  "+(m.Note??m.Kickoff.ToOffset(TimeSpan.FromHours(8)).ToString("HH:mm")),10,B(result.League.Color),true);At(topline,leagueLabel,0);
   var status=T((m.DataStale?"缓存 · ":live?"● ":"")+m.Status,10,m.DataStale?Amber:live?Green:Muted);status.HorizontalAlignment=HorizontalAlignment.Right;At(topline,status,1);body.Children.Add(topline);
   var score=Cols(-1,76,-1);score.Margin=new Thickness(0,13,0,4);At(score,Team(m.Home,m.HomeLogo,m.HomeId),0);At(score,Team(m.Away,m.AwayLogo,m.AwayId),2);
   var center=new StackPanel{VerticalAlignment=VerticalAlignment.Center};var scoreText=T(m.State=="pre"?"VS":(m.HomeScore==""?"—":m.HomeScore)+" : "+(m.AwayScore==""?"—":m.AwayScore),m.State=="pre"?16:24,live?Green:Ink,true);scoreText.TextAlignment=TextAlignment.Center;center.Children.Add(scoreText);var hint=T(open?"收起  ⌃":"数据  ⌄",9,Muted);hint.TextAlignment=TextAlignment.Center;hint.Margin=new Thickness(0,5,0,0);center.Children.Add(hint);At(score,center,1);body.Children.Add(score);
   var click=new Button{Content=body,Padding=new Thickness(0),Background=Brushes.Transparent,BorderThickness=new Thickness(0),HorizontalContentAlignment=HorizontalAlignment.Stretch,Cursor=Cursors.Hand,ToolTip="展开比赛数据"};click.Style=(Style)Resources["CardButton"];click.Click+=(s,e)=>{expanded=open?null:m.Id;Render();};stack.Children.Add(click);
   if(open) {
    var detail=new StackPanel{Margin=new Thickness(13,0,13,13)};detail.Children.Add(new Border{Height=1,Background=B("#37505A"),Margin=new Thickness(0,0,0,12)});
    var followRow=Cols(-1,-1);followRow.Margin=new Thickness(0,0,0,12);At(followRow,FollowButton(m.HomeId,"主队"),0);At(followRow,FollowButton(m.AwayId,"客队"),1);detail.Children.Add(followRow);
    if(m.DataUpdated.HasValue){var updated=T((m.DataStale?"连接异常 · 缓存可能过期\n":"")+"数据读取："+m.DataUpdated.Value.ToOffset(TimeSpan.FromHours(8)).ToString("MM-dd HH:mm:ss"),9,m.DataStale?Amber:Muted);updated.Margin=new Thickness(0,0,0,10);detail.Children.Add(updated);}
    if(m.HomeStats.Count==0 && m.AwayStats.Count==0) {var no=T(m.State=="pre"?"开赛后将在这里显示比赛统计":"数据源暂未提供本场统计",11,Muted);no.Margin=new Thickness(0,0,0,10);detail.Children.Add(no);}
    else {Stat(detail,m,"possessionPct","控球率",true);Stat(detail,m,"totalShots","射门",false);Stat(detail,m,"shotsOnTarget","射正",false);Stat(detail,m,"wonCorners","角球",false);Stat(detail,m,"foulsCommitted","犯规",false);}
    var eventTitle=T("关键事件",10,Muted,true);eventTitle.Margin=new Thickness(0,12,0,7);detail.Children.Add(eventTitle);
    if(m.Events.Count==0) detail.Children.Add(T(m.State=="pre"?"等待开球":"暂无事件记录",11,Muted));
    foreach(var ev in m.Events) {
     var row=Cols(45,42,-1);row.Margin=new Thickness(0,4,0,4);At(row,T(ev.Clock,10,Muted),0);At(row,T(ev.Kind,10,ev.Kind=="红牌"?B("#FF8793"):ev.Kind=="黄牌"?Amber:Green),1);
     var player=T(ev.Player+"\n"+Names.Team(ev.TeamId==m.HomeId?m.Home:ev.TeamId==m.AwayId?m.Away:""),10,Ink);At(row,player,2);detail.Children.Add(row);
    }
    if(!String.IsNullOrEmpty(m.Venue)){var venue=T("球场 · "+m.Venue,9,Muted);venue.Margin=new Thickness(0,12,0,0);detail.Children.Add(venue);}
    stack.Children.Add(detail);
   }
   return outer;
  }
  Button FollowButton(string id,string label) {
   bool followed=preferences.Favorites.Contains(id);var button=Btn(followed?"★ 已关注"+label:"☆ 关注"+label,()=>{preferences.Favorites.Set(id,!followed);PersistFavorites();Render();});button.Foreground=followed?Green:Muted;button.FontSize=10;return button;
  }
  void UpdateFavoriteBadge() {
   string id=preferences.Favorites.TeamIds.FirstOrDefault();
   if(String.IsNullOrEmpty(id)){favoriteBadge.Visibility=Visibility.Collapsed;badgeTeamId=null;return;}
   if(id==badgeTeamId)return;
   var team=teamDirectory.Primary(preferences.Favorites);if(team==null){favoriteBadge.Visibility=Visibility.Collapsed;return;}
   favoriteBadge.Child=TeamBadge(team.Name,team.Logo,31);favoriteBadge.ToolTip="首位关注 · "+Names.Team(team.Name);favoriteBadge.Visibility=Visibility.Visible;badgeTeamId=id;
  }
  FrameworkElement TeamBadge(string name,string logo,double size) {
   var badge=new Grid{Width=size,Height=size};badge.Children.Add(new Border{Background=B("#31444C"),CornerRadius=new CornerRadius(size/2),Child=new TextBlock{Text=String.IsNullOrEmpty(name)?"★":name.Substring(0,1),Foreground=Ink,TextAlignment=TextAlignment.Center,VerticalAlignment=VerticalAlignment.Center,FontWeight=FontWeights.Bold}});
   Uri uri;
   if(Uri.TryCreate(logo,UriKind.Absolute,out uri) && uri.Scheme=="https" && uri.Host=="a.espncdn.com") {
    var img=new Image{Width=size,Height=size,Stretch=Stretch.Uniform};img.ImageFailed+=(o,e)=>{img.Visibility=Visibility.Collapsed;};
    try{BitmapImage bitmap;if(!logoCache.TryGetValue(logo,out bitmap)){bitmap=new BitmapImage(uri);logoCache[logo]=bitmap;}img.Source=bitmap;badge.Children.Add(img);}catch(Exception){}
   }
   return badge;
  }
  FrameworkElement Team(string name,string logo,string id) {
   var s=new StackPanel{VerticalAlignment=VerticalAlignment.Center};
   var badge=TeamBadge(name,logo,30);badge.Margin=new Thickness(0,0,0,7);
   s.Children.Add(badge);bool followed=preferences.Favorites.Contains(id);var n=T((followed?"★ ":"")+Names.Team(name),12,followed?Green:Ink,true);n.TextAlignment=TextAlignment.Center;n.ToolTip=name;s.Children.Add(n);return s;
  }
  void Stat(Panel parent,Match m,string key,string label,bool percent) {
   string h,a;m.HomeStats.TryGetValue(key,out h);m.AwayStats.TryGetValue(key,out a);
   var section=new StackPanel{Margin=new Thickness(0,0,0,9)};var row=Cols(-1,-1,-1);At(row,T(h==null?"—":h+(percent?"%":""),11),0);var title=T(label,10,Muted);title.TextAlignment=TextAlignment.Center;At(row,title,1);var right=T(a==null?"—":a+(percent?"%":""),11);right.TextAlignment=TextAlignment.Right;At(row,right,2);section.Children.Add(row);
   double hv,av;if(Double.TryParse(h,NumberStyles.Any,CultureInfo.InvariantCulture,out hv)&&Double.TryParse(a,NumberStyles.Any,CultureInfo.InvariantCulture,out av)&&hv+av>0) {
    var bar=Cols(-Math.Max(.01,hv),-Math.Max(.01,av));bar.Height=3;bar.Margin=new Thickness(0,5,0,0);At(bar,new Border{Background=Green,CornerRadius=new CornerRadius(2),Margin=new Thickness(0,0,1,0)},0);At(bar,new Border{Background=B("#719CBC"),CornerRadius=new CornerRadius(2)},1);section.Children.Add(bar);
   }
   parent.Children.Add(section);
  }
  void SetupTray() {
   tray=new Forms.NotifyIcon{Text="看球 · 桌面比分",Icon=System.Drawing.SystemIcons.Application,Visible=true};
   tray.DoubleClick+=(s,e)=>Dispatcher.Invoke(new Action(()=>{Show();Activate();}));
   var menu=new Forms.ContextMenuStrip();menu.Items.Add("显示看球",null,(s,e)=>Dispatcher.Invoke(new Action(()=>{Show();Activate();})));menu.Items.Add("退出",null,(s,e)=>Dispatcher.Invoke(new Action(Close)));tray.ContextMenuStrip=menu;
  }
  void ShowFavorites() {
   var panel=new Grid{Margin=new Thickness(20)};
   foreach(var height in new[]{GridLength.Auto,GridLength.Auto,GridLength.Auto,new GridLength(1,GridUnitType.Star),GridLength.Auto})panel.RowDefinitions.Add(new RowDefinition{Height=height});
   var dialog=new Window{Title="关注球队",Owner=this,Width=400,Height=560,MinHeight=420,MinWidth=360,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=B("#182830"),Foreground=Ink,FontFamily=FontFamily,Content=panel,Topmost=Topmost};
   var heading=T("关注你的球队",19,Ink,true);heading.Margin=new Thickness(0,0,0,14);panel.Children.Add(heading);
   var search=new TextBox{FontSize=13,Padding=new Thickness(10,8,10,8),Background=B("#263B45"),Foreground=Ink,BorderBrush=B("#46606B"),CaretBrush=Ink,ToolTip="输入球队中文名或英文名"};Grid.SetRow(search,1);panel.Children.Add(search);
   var filterRow=Cols(-1,0);filterRow.ColumnDefinitions[1].Width=GridLength.Auto;filterRow.Margin=new Thickness(0,12,0,12);Grid.SetRow(filterRow,2);panel.Children.Add(filterRow);
   var count=T("",11,Green);At(filterRow,count,0);var selectedOnly=new CheckBox{Content="只显示已关注",Foreground=Muted,VerticalAlignment=VerticalAlignment.Center};At(filterRow,selectedOnly,1);
   var teams=new StackPanel();var scroller=new ScrollViewer{Content=teams,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};Grid.SetRow(scroller,3);panel.Children.Add(scroller);
   var hint=T("勾选添加关注，用 ↑ ↓ 排序。首位队徽显示在卡片顶部。配置即时保存到本机，关闭或重启后保留。",10,Muted);hint.Margin=new Thickness(0,14,0,0);Grid.SetRow(hint,4);panel.Children.Add(hint);
   Action draw=null;
   draw=()=>{
    teams.Children.Clear();count.Text="已关注 "+preferences.Favorites.TeamIds.Count+" 支球队";
    var all=teamDirectory.All;
    foreach(var missing in preferences.Favorites.TeamIds.Where(id=>!all.Any(x=>x.Id==id)))all.Add(new TeamEntry{Id=missing,Name="球队 "+missing,League="已关注"});
    string term=search.Text.Trim();
    var found=all.Where(t=>(term.Length==0||Names.Team(t.Name).IndexOf(term,StringComparison.OrdinalIgnoreCase)>=0||t.Name.IndexOf(term,StringComparison.OrdinalIgnoreCase)>=0)&&(selectedOnly.IsChecked!=true||preferences.Favorites.Contains(t.Id))).OrderBy(t=>preferences.Favorites.Contains(t.Id)?preferences.Favorites.TeamIds.IndexOf(t.Id):Int32.MaxValue).ThenBy(t=>Names.Team(t.Name),StringComparer.CurrentCulture).ToList();
    foreach(var team in found) {
     var label=new StackPanel();label.Children.Add(T(Names.Team(team.Name),13,Ink,true));label.Children.Add(T(team.League+" · "+team.Name,10,Muted));
     var cb=new CheckBox{Content=label,IsChecked=preferences.Favorites.Contains(team.Id),Foreground=Ink,Margin=new Thickness(3,8,7,8),VerticalContentAlignment=VerticalAlignment.Center,HorizontalContentAlignment=HorizontalAlignment.Stretch,Padding=new Thickness(7,0,0,0)};
     cb.Click+=(s,e)=>{preferences.Favorites.Set(team.Id,cb.IsChecked==true);PersistFavorites();Render();draw();};
     var teamRow=Cols(-1,0);teamRow.ColumnDefinitions[1].Width=GridLength.Auto;At(teamRow,cb,0);
     if(preferences.Favorites.Contains(team.Id)) {
      var arrows=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};
      var up=Btn("↑",()=>{preferences.Favorites.Move(team.Id,-1);PersistFavorites();Render();draw();},"上移关注顺序");up.IsEnabled=preferences.Favorites.TeamIds.IndexOf(team.Id)>0;up.Padding=new Thickness(7,6,7,6);arrows.Children.Add(up);
      var down=Btn("↓",()=>{preferences.Favorites.Move(team.Id,1);PersistFavorites();Render();draw();},"下移关注顺序");down.IsEnabled=preferences.Favorites.TeamIds.IndexOf(team.Id)<preferences.Favorites.TeamIds.Count-1;down.Padding=new Thickness(7,6,7,6);arrows.Children.Add(down);At(teamRow,arrows,1);
     }
     teams.Children.Add(teamRow);
    }
    if(found.Count==0){var empty=T("没有找到球队，试试其他名称。",12,Muted);empty.Margin=new Thickness(5,20,5,20);teams.Children.Add(empty);}
   };
   search.TextChanged+=(s,e)=>draw();selectedOnly.Click+=(s,e)=>draw();dialog.Resources=Resources;draw();dialog.ShowDialog();
  }
  void ShowSettings() {
   var panel=new StackPanel{Margin=new Thickness(22)};
   var dialog=new Window{Title="看球设置",Owner=this,Width=360,Height=430,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=B("#182830"),Foreground=Ink,FontFamily=FontFamily,Content=panel,Topmost=Topmost};
   panel.Children.Add(T("让比分留在你身边",17,Ink,true));
   var sound=new CheckBox{Content="比分变化时播放提示音",IsChecked=preferences.Sound,Foreground=Ink,Margin=new Thickness(0,22,0,20)};sound.Checked+=(s,e)=>{preferences.Sound=true;Save();};sound.Unchecked+=(s,e)=>{preferences.Sound=false;Save();};panel.Children.Add(sound);
   panel.Children.Add(T("卡片不透明度",11,Muted));var slider=new Slider{Minimum=.7,Maximum=1,Value=Opacity,Margin=new Thickness(0,10,0,18),TickFrequency=.05};slider.ValueChanged+=(s,e)=>{Opacity=slider.Value;};panel.Children.Add(slider);
   panel.Children.Add(T("拖动左上角标题移动卡片；拖动右下角调整大小。收起后，双击托盘图标可恢复。",11,Muted));
   panel.Children.Add(T("赛中每30秒刷新，其余每2分钟刷新。数据可能延迟，缺失项显示为“—”。",10,Muted));
   var saved=T("关注配置保存在本机：\n"+favoriteStore.FilePath,10,Muted);saved.Margin=new Thickness(0,15,0,8);panel.Children.Add(saved);
   panel.Children.Add(Btn("打开配置目录",()=>System.Diagnostics.Process.Start("explorer.exe","\""+dataDir+"\"")));
   dialog.Resources=Resources;
   dialog.Closed+=(s,e)=>Save();dialog.ShowDialog();
  }
  void PersistFavorites() {
   try {
    favoriteStore.Save(preferences.Favorites);Save();
    if(!String.IsNullOrEmpty(favoriteStore.Warning))MessageBox.Show(this,favoriteStore.Warning,"本地关注配置",MessageBoxButton.OK,MessageBoxImage.Warning);
   }catch(Exception ex) {
    MessageBox.Show(this,"关注配置未能保存到本机。请检查目录权限后重新操作。\n\n"+favoriteStore.FilePath+"\n\n"+ex.Message,"关注保存失败",MessageBoxButton.OK,MessageBoxImage.Warning);
   }
  }
  void Save() {
   preferences.Left=Left;preferences.Top=Top;preferences.Width=Width;preferences.Height=Height;preferences.Pinned=Topmost;preferences.Opacity=Opacity;
   try{File.WriteAllText(Path.Combine(dataDir,"settings.json"),new JavaScriptSerializer().Serialize(preferences));}catch(Exception){}
  }
  const string Styles=@"<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
  <Style TargetType='Button'><Setter Property='Foreground' Value='#CBD8DE'/><Setter Property='Background' Value='#22323A'/><Setter Property='BorderThickness' Value='0'/><Setter Property='FontFamily' Value='Microsoft YaHei UI'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'><Border x:Name='surface' Background='{TemplateBinding Background}' CornerRadius='6' Padding='{TemplateBinding Padding}'><ContentPresenter HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}' VerticalAlignment='Center'/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='surface' Property='Opacity' Value='.75'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter TargetName='surface' Property='Opacity' Value='.4'/></Trigger><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='surface' Property='BorderBrush' Value='#63E7AE'/><Setter TargetName='surface' Property='BorderThickness' Value='1'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
  <Style x:Key='CardButton' TargetType='Button'><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'><Border x:Name='surface' Background='Transparent' CornerRadius='10'><ContentPresenter HorizontalAlignment='Stretch'/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='surface' Property='Background' Value='#102FE0AC'/></Trigger><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='surface' Property='BorderBrush' Value='#63E7AE'/><Setter TargetName='surface' Property='BorderThickness' Value='1'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
  <Style TargetType='ScrollBar'><Setter Property='Width' Value='5'/><Setter Property='Background' Value='Transparent'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ScrollBar'><Track x:Name='PART_Track' IsDirectionReversed='True' Orientation='{TemplateBinding Orientation}' Minimum='{TemplateBinding Minimum}' Maximum='{TemplateBinding Maximum}' Value='{Binding Value, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}' ViewportSize='{TemplateBinding ViewportSize}'><Track.DecreaseRepeatButton><RepeatButton Command='ScrollBar.PageUpCommand' Opacity='0'/></Track.DecreaseRepeatButton><Track.Thumb><Thumb><Thumb.Template><ControlTemplate TargetType='Thumb'><Border Background='#46606B' CornerRadius='3'/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb><Track.IncreaseRepeatButton><RepeatButton Command='ScrollBar.PageDownCommand' Opacity='0'/></Track.IncreaseRepeatButton></Track></ControlTemplate></Setter.Value></Setter></Style>
 </ResourceDictionary>";
 }
 public static class Program {
  [STAThread] public static void Main() {
   bool created;using(var mutex=new Mutex(true,"Local\\TouchlineCard.Desktop",out created)) {
    if(!created){MessageBox.Show("看球已经在运行。请在系统托盘中双击图标恢复卡片。","看球");return;}
    try{var app=new Application();app.Run(new CardWindow());}catch(Exception ex){MessageBox.Show("无法启动看球卡片："+ex.Message,"看球");}
   }
  }
 }
}
