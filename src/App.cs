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
 public class CardWindow:Window {
  readonly string dataDir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TouchlineCard");
  readonly ScoreFeed feed;
  readonly FavoriteStore favoriteStore;
  readonly BadgeStore badges;
  readonly PreferenceStore preferenceStore;
  readonly AppUpdater updater;
  readonly StartupSetting startup=new StartupSetting(Forms.Application.ExecutablePath);
  Preferences preferences=new Preferences();
  LeagueResult[] results=new LeagueResult[0];
  DateTime day=MatchData.BeijingToday;
  DateTime rangeStart=MatchData.BeijingToday,rangeEnd=MatchData.BeijingToday.AddDays(6);
  bool liveOnly,loading,refreshAgain,forceNext,exiting,jumpToToday,suspended,refreshVisible,updatingDate;
  CancellationTokenSource refreshCancel;
  DatePicker datePicker;
  UniformGrid cupTabs;
  CheckBox cupsToggle;
  readonly TeamDirectory teamDirectory=new TeamDirectory();
  string filter="all",expanded;
  ScoreTracker tracker=new ScoreTracker();
  DateTimeOffset due=DateTimeOffset.UtcNow,noticeUntil=DateTimeOffset.MinValue;
  TextBlock summary,footer,notice;
  Border noticeBox;
  Border favoriteBadge;
  Border fullCard,compactCard,compactBadge;
  TextBlock compactStatus;
  Popup opacityPopup;
  Grid viewHost;
  StackPanel list;
  ScrollViewer scroll;
  Button pinButton,liveButton,refreshButton,favoritesButton;
  CheckBox favoritesToggle;
  readonly Dictionary<string,Button> tabs=new Dictionary<string,Button>();
  readonly DispatcherTimer timer=new DispatcherTimer();
  Forms.NotifyIcon tray;
  Forms.ToolStripMenuItem traySummary,trayVisibility,trayPin,trayStartup,trayUpdates;
  Window updateDialog;
  System.Drawing.Icon teamTrayIcon;
  string trayBadgeKey;
  int trayBadgeRequest;
  System.Drawing.Icon applicationIcon;
  static readonly Brush Ink=B("#EAF2F3"),Muted=B("#91A2A9"),Green=B("#63E7AE"),Amber=B("#F1C779");
  public CardWindow() {
   Directory.CreateDirectory(dataDir);feed=new ScoreFeed(Path.Combine(dataDir,"cache"));badges=new BadgeStore(Path.Combine(dataDir,"badges"));
   using(var stream=System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("Touchline.Icon")) {
    using(var source=new System.Drawing.Icon(stream))applicationIcon=(System.Drawing.Icon)source.Clone();
    Icon=System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(applicationIcon.Handle,Int32Rect.Empty,BitmapSizeOptions.FromWidthAndHeight(32,32));
   }
   preferenceStore=new PreferenceStore(dataDir);preferences=preferenceStore.Load();
   updater=new AppUpdater(dataDir,preferences,Save);
   favoriteStore=new FavoriteStore(dataDir);preferences.Favorites=favoriteStore.Load();
   Title="看球 · 桌面比分";FontFamily=new FontFamily("Microsoft YaHei UI, Segoe UI");FontSize=12;Foreground=Ink;
   Width=Clamp(preferences.Width,380,560);Height=Clamp(preferences.Height,440,1100);MinWidth=380;MinHeight=440;MaxWidth=560;
   WindowStyle=WindowStyle.None;AllowsTransparency=true;Background=Brushes.Transparent;ResizeMode=ResizeMode.CanResizeWithGrip;
   Topmost=preferences.Pinned;Opacity=preferences.ActiveOpacity();
   var area=SystemParameters.WorkArea;
   double initialWidth=preferences.Compact?216:Width,initialHeight=preferences.Compact?36:Height;
   Left=Double.IsNaN(preferences.Left)?area.Right-initialWidth-24:Math.Max(area.Left,Math.Min(preferences.Left,area.Right-initialWidth));
   Top=Double.IsNaN(preferences.Top)?area.Top+45:Math.Max(area.Top,Math.Min(preferences.Top,area.Bottom-Math.Min(initialHeight,area.Height)));
   Height=Math.Min(Height,area.Height-20);
   Resources=(ResourceDictionary)XamlReader.Parse(Styles);
   Build();BuildCompact();ApplyPresentation();SetupTray();
   updater.Changed+=UpdateTray;
   try{startup.RefreshPath();}catch(Exception){notice.Text="开机自启设置未能更新，请在设置中重新开启。";notice.Foreground=Amber;noticeBox.Visibility=Visibility.Visible;noticeUntil=DateTimeOffset.UtcNow.AddMinutes(2);}
   if(!String.IsNullOrEmpty(preferenceStore.Warning)){notice.Text=preferenceStore.Warning;notice.Foreground=Amber;noticeBox.Visibility=Visibility.Visible;noticeUntil=DateTimeOffset.UtcNow.AddMinutes(2);}
   if(!String.IsNullOrEmpty(favoriteStore.Warning)){notice.Text=favoriteStore.Warning;notice.Foreground=Amber;noticeBox.Visibility=Visibility.Visible;noticeUntil=DateTimeOffset.UtcNow.AddMinutes(2);}
   timer.Interval=TimeSpan.FromSeconds(1);timer.Tick+=async (s,e)=>{
    if(noticeBox.Visibility==Visibility.Visible && DateTimeOffset.UtcNow>noticeUntil) noticeBox.Visibility=Visibility.Collapsed;
    if(CanRefresh){AdvanceDay();if(DateTimeOffset.UtcNow>=due&&!loading)await Refresh();}
    if(!exiting)await updater.Tick();
   };timer.Start();
   IsVisibleChanged+=(s,e)=>SyncRefresh();
   StateChanged+=(s,e)=>{if(WindowState==WindowState.Minimized){Hide();WindowState=WindowState.Normal;}};
   Closing+=(s,e)=>{if(!exiting){e.Cancel=true;Hide();return;}Save();PauseRequests();timer.Stop();};
   Closed+=(s,e)=>{Microsoft.Win32.SystemEvents.PowerModeChanged-=PowerChanged;if(tray!=null){tray.Visible=false;tray.Dispose();}if(teamTrayIcon!=null)teamTrayIcon.Dispose();if(applicationIcon!=null)applicationIcon.Dispose();Application.Current.Shutdown();};
   Microsoft.Win32.SystemEvents.PowerModeChanged+=PowerChanged;
   Application.Current.SessionEnding+=(s,e)=>{exiting=true;Save();PauseRequests();};
   PreviewKeyDown+=(s,e)=>{if(Keyboard.Modifiers!=ModifierKeys.Control)return;if(e.Key==Key.R){OpenAndRefresh();e.Handled=true;}else if(e.Key==Key.D1){ShowCard();e.Handled=true;}else if(e.Key==Key.W){Hide();e.Handled=true;}else if(e.Key==Key.Q){Quit();e.Handled=true;}else if(e.Key==Key.OemComma){ShowSettings();e.Handled=true;}};
  }
  public void Start(){if(!preferences.StartInTray)ShowCard(false);else{footer.Text="已暂停刷新 · 展开完整卡片后同步";UpdateTray();}}
  bool CanRefresh {get{return IsVisible&&!preferences.Compact&&!suspended&&!exiting&&WindowState!=WindowState.Minimized;}}
  void AdvanceDay(){var today=MatchData.BeijingToday;if(day==today)return;if(rangeStart==day){rangeStart=today;rangeEnd=today.AddDays(6);}day=today;due=DateTimeOffset.MinValue;Render();}
  void PauseRequests(){var active=refreshCancel;refreshCancel=null;if(active!=null)active.Cancel();loading=false;refreshAgain=false;forceNext=false;if(refreshButton!=null)refreshButton.IsEnabled=true;}
  void SyncRefresh(bool force=false){
   bool active=CanRefresh;
   if(!active){refreshVisible=false;PauseRequests();if(footer!=null)footer.Text="已暂停刷新 · 展开完整卡片后同步";}
   else if(!refreshVisible||force){refreshVisible=true;AdvanceDay();if(force)PauseRequests();due=DateTimeOffset.MinValue;RefreshSoon(force);}
   UpdateCompact();UpdateTray();
  }
  void PowerChanged(object sender,Microsoft.Win32.PowerModeChangedEventArgs args){Dispatcher.BeginInvoke(new Action(()=>{if(args.Mode==Microsoft.Win32.PowerModes.Suspend){suspended=true;SyncRefresh();}else if(args.Mode==Microsoft.Win32.PowerModes.Resume){suspended=false;SyncRefresh(true);}}));}
  void ShowCard(bool full=true){if(full)SetPresentation(false);Show();Activate();SyncRefresh();}
  void OpenAndRefresh(){ShowCard();SyncRefresh(true);}
  void Quit(){
   if(updater.Ready&&preferences.AutoCheckUpdates&&preferences.AutoDownloadUpdates)try{updater.BeginInstall(false);}catch(Exception){}
   exiting=true;Close();
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
   shell.Effect=new System.Windows.Media.Effects.DropShadowEffect{Color=Colors.Black,BlurRadius=14,Opacity=.35,ShadowDepth=3};fullCard=shell;viewHost=new Grid();viewHost.Children.Add(shell);Content=viewHost;
   var root=new Grid();shell.Child=root;
   foreach(var size in new[]{GridLength.Auto,GridLength.Auto,GridLength.Auto,GridLength.Auto,GridLength.Auto,GridLength.Auto,new GridLength(1,GridUnitType.Star),GridLength.Auto})root.RowDefinitions.Add(new RowDefinition{Height=size});
   var header=Cols(-1,0);header.ColumnDefinitions[1].Width=GridLength.Auto;
   var brand=new StackPanel{Cursor=Cursors.SizeAll};brand.Children.Add(T("TOUCHLINE  /  比分随行",9,Green,true));
   var titleRow=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,3,0,0)};favoriteBadge=new Border{Margin=new Thickness(0,0,9,0),VerticalAlignment=VerticalAlignment.Center,Visibility=Visibility.Collapsed};titleRow.Children.Add(favoriteBadge);titleRow.Children.Add(T("看球",25,Ink,true));brand.Children.Add(titleRow);
   brand.MouseLeftButtonDown+=(s,e)=>{if(e.ButtonState==MouseButtonState.Pressed) {try{DragMove();}catch(InvalidOperationException){} }};At(header,brand,0);
   var controls=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Top};
   pinButton=Btn(Topmost?"已置顶":"置顶",()=>{Topmost=!Topmost;pinButton.Content=Topmost?"已置顶":"置顶";pinButton.Foreground=Topmost?Green:Muted;Save();},"切换窗口置顶");pinButton.FontSize=10;pinButton.Padding=new Thickness(8,6,8,6);pinButton.Foreground=Topmost?Green:Muted;
   controls.Children.Add(pinButton);var compactButton=Btn("收纳",()=>SetPresentation(true),"缩成一小条，保留桌面空间");compactButton.FontSize=10;compactButton.Padding=new Thickness(7,6,7,6);controls.Children.Add(compactButton);controls.Children.Add(Btn("−",()=>Hide(),"隐藏到系统托盘"));controls.Children.Add(Btn("×",()=>Hide(),"关闭卡片，继续在系统托盘运行"));At(header,controls,1);root.Children.Add(header);
   var dateBar=Cols(-1,0);dateBar.ColumnDefinitions[1].Width=GridLength.Auto;dateBar.Margin=new Thickness(0,17,0,14);Grid.SetRow(dateBar,1);root.Children.Add(dateBar);
   var dates=new StackPanel{Orientation=Orientation.Horizontal};var today=Btn("今天",JumpToday,"回到今天的赛程位置");today.Foreground=Green;today.Padding=new Thickness(7,5,7,5);dates.Children.Add(today);
   var previous=Btn("‹",()=>SelectDate(rangeStart.AddDays(-7)),"上一周");previous.Padding=new Thickness(5);dates.Children.Add(previous);var next=Btn("›",()=>SelectDate(rangeStart.AddDays(7)),"下一周");next.Padding=new Thickness(5);dates.Children.Add(next);
   datePicker=new DatePicker{SelectedDate=rangeStart,Width=108,FontSize=10,VerticalAlignment=VerticalAlignment.Center,ToolTip="选择赛程起始日期（北京时间）"};datePicker.SelectedDateChanged+=(s,e)=>{if(!updatingDate&&datePicker.SelectedDate.HasValue)SelectDate(datePicker.SelectedDate.Value);};dates.Children.Add(datePicker);At(dateBar,dates,0);
   favoritesButton=Btn("关注球队",ShowFavorites,"添加或取消关注球队，支持多选");At(dateBar,favoritesButton,1);
   var filterPanel=new StackPanel{Margin=new Thickness(0,0,0,13)};Grid.SetRow(filterPanel,2);root.Children.Add(filterPanel);var filters=new UniformGrid{Columns=7};filterPanel.Children.Add(filters);
   AddTab(filters,"all","全部");foreach(var league in League.Regular) AddTab(filters,league.Id,league.Name);
   cupTabs=new UniformGrid{Columns=4,Margin=new Thickness(0,5,0,0)};filterPanel.Children.Add(cupTabs);foreach(var league in League.Cups)AddTab(cupTabs,league.Id,league.Name);
   var line=Cols(-1,0);line.ColumnDefinitions[1].Width=GridLength.Auto;Grid.SetRow(line,3);line.Margin=new Thickness(0,0,0,12);root.Children.Add(line);
   summary=T("正在读取今日赛程…",11,Muted);At(line,summary,0);liveButton=Btn("只看进行中",()=>{liveOnly=!liveOnly;liveButton.Foreground=liveOnly?Green:Muted;Render();});liveButton.FontSize=10;liveButton.Padding=new Thickness(7,4,7,4);At(line,liveButton,1);
   notice=T("",11,Green);noticeBox=new Border{Child=notice,Background=B("#183B30"),CornerRadius=new CornerRadius(8),Padding=new Thickness(10),Margin=new Thickness(0,0,0,10),Visibility=Visibility.Collapsed};Grid.SetRow(noticeBox,4);root.Children.Add(noticeBox);
   var options=Cols(-1,0);options.ColumnDefinitions[1].Width=GridLength.Auto;options.Margin=new Thickness(1,0,0,12);Grid.SetRow(options,5);root.Children.Add(options);
   favoritesToggle=new CheckBox{Content="只看关注球队",IsChecked=preferences.Favorites.OnlyFavorites,Foreground=Ink,FontSize=11,VerticalAlignment=VerticalAlignment.Center};
   favoritesToggle.Click+=(s,e)=>{preferences.Favorites.OnlyFavorites=favoritesToggle.IsChecked==true;scroll.ScrollToTop();PersistFavorites();Render();};At(options,favoritesToggle,0);
   cupsToggle=new CheckBox{Content="显示杯赛",IsChecked=preferences.ShowCups,Foreground=Ink,FontSize=11,VerticalAlignment=VerticalAlignment.Center};cupsToggle.Click+=(s,e)=>SetCups(cupsToggle.IsChecked==true);At(options,cupsToggle,1);
   list=new StackPanel();scroll=new ScrollViewer{Content=list,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Padding=new Thickness(0,0,3,0)};Grid.SetRow(scroll,6);root.Children.Add(scroll);
   var bottom=new StackPanel{Margin=new Thickness(0,12,0,0)};Grid.SetRow(bottom,7);root.Children.Add(bottom);
   var footRow=Cols(-1,0,0);footRow.ColumnDefinitions[1].Width=GridLength.Auto;footRow.ColumnDefinitions[2].Width=GridLength.Auto;bottom.Children.Add(footRow);
   footer=T("正在连接 ESPN…",10,Muted);At(footRow,footer,0);refreshButton=Btn("↻",async ()=>await Refresh(true),"立即刷新全部已加载赛程");refreshButton.Padding=new Thickness(8,3,8,3);At(footRow,refreshButton,1);
   var settings=Btn("设置",ShowSettings);settings.FontSize=10;settings.Padding=new Thickness(7,4,7,4);At(footRow,settings,2);
   var source=T("数据：ESPN · 可能延迟 · 点击比赛查看数据",9,B("#758A94"));source.Margin=new Thickness(0,6,0,0);bottom.Children.Add(source);
   Render();
  }
  void BuildCompact() {
   compactCard=new Border{Background=B("#14252C"),BorderBrush=B("#49606A"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(10),Margin=new Thickness(2),Padding=new Thickness(8,0,5,0),Visibility=Visibility.Collapsed};viewHost.Children.Add(compactCard);
   var row=Cols(-1,30,38);compactCard.Child=row;
   var dragArea=Cols(24,-1);dragArea.Background=Brushes.Transparent;dragArea.Cursor=Cursors.SizeAll;dragArea.ToolTip="拖动移动；双击展开；右键显示菜单";
   compactBadge=new Border{VerticalAlignment=VerticalAlignment.Center};At(dragArea,compactBadge,0);compactStatus=T("看球",10,Ink,true);compactStatus.TextWrapping=TextWrapping.NoWrap;compactStatus.TextTrimming=TextTrimming.CharacterEllipsis;At(dragArea,compactStatus,1);
   dragArea.MouseLeftButtonDown+=(s,e)=>{if(e.ClickCount==2){SetPresentation(false);return;}if(e.ButtonState==MouseButtonState.Pressed){try{DragMove();Save();}catch(InvalidOperationException){}}};At(row,dragArea,0);
   Button opacityButton=null;opacityButton=Btn("◐",()=>ShowOpacityPopup(opacityButton),"调整收纳条透明度");opacityButton.Padding=new Thickness(3,4,3,4);opacityButton.Margin=new Thickness(0,0,2,0);opacityButton.Background=Brushes.Transparent;At(row,opacityButton,1);
   var expand=Btn("展开",()=>SetPresentation(false),"恢复完整比赛卡片");expand.FontSize=10;expand.Padding=new Thickness(3,4,3,4);expand.Margin=new Thickness(0);expand.Background=Brushes.Transparent;At(row,expand,2);
   var menu=new ContextMenu();var open=new MenuItem{Header="展开完整卡片"};open.Click+=(s,e)=>SetPresentation(false);menu.Items.Add(open);
   var opacity=new MenuItem{Header="调整透明度"};opacity.Click+=(s,e)=>ShowOpacityPopup(opacityButton);menu.Items.Add(opacity);
   var hide=new MenuItem{Header="隐藏到系统托盘"};hide.Click+=(s,e)=>Hide();menu.Items.Add(hide);var quit=new MenuItem{Header="退出看球"};quit.Click+=(s,e)=>Quit();menu.Items.Add(quit);compactCard.ContextMenu=menu;
   UpdateCompact();
  }
  void ApplyPresentation() {
   MinWidth=0;MinHeight=0;
   fullCard.Visibility=preferences.Compact?Visibility.Collapsed:Visibility.Visible;
   compactCard.Visibility=preferences.Compact?Visibility.Visible:Visibility.Collapsed;
   if(preferences.Compact){Width=216;Height=36;ResizeMode=ResizeMode.NoResize;ShowInTaskbar=false;}
   else {Width=Clamp(preferences.Width,380,560);Height=Math.Min(Clamp(preferences.Height,440,1100),SystemParameters.WorkArea.Height-20);MinWidth=380;MinHeight=Math.Min(440,Height);ResizeMode=ResizeMode.CanResizeWithGrip;ShowInTaskbar=true;}
   Opacity=preferences.ActiveOpacity();KeepOnScreen();SyncRefresh();
  }
  void KeepOnScreen() {
   var area=SystemParameters.WorkArea;Left=Math.Max(area.Left,Math.Min(Left,area.Right-Width));Top=Math.Max(area.Top,Math.Min(Top,area.Bottom-Height));
  }
  void SetPresentation(bool compact) {
   if(opacityPopup!=null)opacityPopup.IsOpen=false;
   if(preferences.Compact==compact){Show();Activate();return;}
   double right=Left+Width;preferences.CaptureLayout(Left,Top,Width,Height,Opacity);preferences.SetCompact(compact,Width,Height);
   ApplyPresentation();Left=right-Width;KeepOnScreen();Save();
  }
  void UpdateCompact() {
   if(compactStatus==null)return;
   var first=teamDirectory.Primary(preferences.Favorites);compactBadge.Child=first==null?new Image{Source=Icon,Width=19,Height=19}:TeamBadge(first.Name,first.Logo,19,first.Id);
   compactStatus.Text="看球 · 已暂停";compactStatus.Foreground=Muted;
   compactStatus.ToolTip=first==null?"看球 · 双击展开":Names.Team(first.Name)+" · 双击展开";
  }
  FrameworkElement OpacityControls(bool? compactMode=null) {
   bool compact=compactMode??preferences.Compact;double opacity=compact==preferences.Compact?Opacity:compact?preferences.CompactOpacity:preferences.Opacity;
   var panel=new StackPanel();var label=T("",11,Ink);panel.Children.Add(label);
   var slider=new Slider{Minimum=0,Maximum=90,Value=Math.Round((1-opacity)*100),TickFrequency=1,IsSnapToTickEnabled=true,Margin=new Thickness(0,10,0,8),ToolTip="0% 为不透明，90% 最透明"};
   Action update=()=>label.Text=(compact?"收纳条透明度 ":"卡片透明度 ")+Math.Round(slider.Value)+"%";update();
   slider.ValueChanged+=(s,e)=>{double value=Preferences.LimitOpacity(1-slider.Value/100);if(compact==preferences.Compact)Opacity=value;else if(compact)preferences.CompactOpacity=value;else preferences.Opacity=value;update();Save();};panel.Children.Add(slider);return panel;
  }
  void ShowOpacityPopup(FrameworkElement anchor) {
   if(opacityPopup!=null)opacityPopup.IsOpen=false;
   opacityPopup=new Popup{PlacementTarget=anchor,Placement=PlacementMode.Bottom,StaysOpen=false,AllowsTransparency=true,Child=new Border{Background=B("#1B2D35"),BorderBrush=B("#4D6774"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(9),Padding=new Thickness(14),Width=235,Child=OpacityControls()}};
   opacityPopup.Closed+=(s,e)=>Save();opacityPopup.IsOpen=true;
  }
  void AddTab(Panel p,string id,string text) {
   var b=Btn(text,()=>{filter=id;Render();});b.FontSize=11;b.Padding=new Thickness(0,7,0,7);b.Margin=new Thickness(0,0,3,0);tabs[id]=b;p.Children.Add(b);
  }
  void JumpToday() {
   day=MatchData.BeijingToday;jumpToToday=true;
   if(rangeStart>day||rangeEnd<day){rangeStart=day;rangeEnd=day.AddDays(6);RefreshSoon();}else Render();
  }
  void SelectDate(DateTime date){PauseRequests();rangeStart=date.Date;rangeEnd=rangeStart.AddDays(6);results=new LeagueResult[0];expanded=null;scroll.ScrollToTop();Render();RefreshSoon();}
  void SetCups(bool show){if(preferences.ShowCups==show)return;preferences.ShowCups=show;PauseRequests();if(!show&&League.IsCup(filter))filter="all";results=results.Where(x=>show||!League.IsCup(x.League.Id)).ToArray();Save();Render();RefreshSoon();}
  void ExtendRange(bool earlier) {if(earlier)rangeStart=rangeStart.AddDays(-7);else rangeEnd=rangeEnd.AddDays(7);RefreshSoon();}
  async void RefreshSoon(bool force=false) { await Refresh(force); }
  async System.Threading.Tasks.Task Refresh(bool force=false) {
   if(!CanRefresh)return;
   if(loading){refreshAgain=true;forceNext|=force;return;}loading=true;refreshButton.IsEnabled=false;footer.Text="正在同步赛程与比分…";
   var request=new CancellationTokenSource();refreshCancel=request;if(force)badges.ClearFailures();
   var requestedStart=rangeStart;var requestedEnd=rangeEnd;
   try {
    var incoming=await feed.LoadRange(requestedStart,requestedEnd,force,preferences.ShowCups,request.Token);request.Token.ThrowIfCancellationRequested();if(!CanRefresh)return;
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
   } catch(OperationCanceledException) {} catch(Exception) { footer.Text="同步失败，稍后自动重试";footer.Foreground=Amber; }
   finally {
    if(Object.ReferenceEquals(refreshCancel,request)){refreshCancel=null;loading=false;if(!exiting){refreshButton.IsEnabled=true;due=DateTimeOffset.UtcNow.AddSeconds(results.Any(x=>x.Stale||x.Matches.Any(m=>m.State=="in"))?30:120);if(refreshAgain){refreshAgain=false;bool nextForce=forceNext;forceNext=false;RefreshSoon(nextForce);}}}request.Dispose();
   }
  }
  string TranslateNotice(string text) { foreach(var m in results.SelectMany(x=>x.Matches)){text=text.Replace(m.Home,Names.Team(m.Home)).Replace(m.Away,Names.Team(m.Away));}return text; }
  void Render() {
   cupTabs.Visibility=preferences.ShowCups?Visibility.Visible:Visibility.Collapsed;cupsToggle.IsChecked=preferences.ShowCups;
   updatingDate=true;datePicker.SelectedDate=rangeStart;updatingDate=false;
   UpdateCompact();
   UpdateFavoriteBadge();
   favoritesButton.Content="关注球队 · "+preferences.Favorites.TeamIds.Count;
   foreach(var kv in tabs){kv.Value.Background=B(kv.Key==filter?"#27503F":"#1C2B33");kv.Value.Foreground=kv.Key==filter?Green:Muted;}
   var selected=results.Where(x=>(preferences.ShowCups||!League.IsCup(x.League.Id))&&(filter=="all"||x.League.Id==filter)).ToList();
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
   if(!CanRefresh)footer.Text="已暂停刷新 · 展开完整卡片后同步";UpdateTray();
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
   if(String.IsNullOrEmpty(id)){favoriteBadge.Visibility=Visibility.Collapsed;return;}
   var team=teamDirectory.Primary(preferences.Favorites);if(team==null){favoriteBadge.Visibility=Visibility.Collapsed;return;}
   favoriteBadge.Child=TeamBadge(team.Name,team.Logo,31,team.Id);favoriteBadge.ToolTip="首位关注 · "+Names.Team(team.Name);favoriteBadge.Visibility=Visibility.Visible;
  }
  FrameworkElement TeamBadge(string name,string logo,double size,string id) {
   var badge=new Grid{Width=size,Height=size};badge.Children.Add(new Border{Background=B("#31444C"),CornerRadius=new CornerRadius(size/2),Child=new TextBlock{Text=String.IsNullOrEmpty(name)?"★":name.Substring(0,1),Foreground=Ink,TextAlignment=TextAlignment.Center,VerticalAlignment=VerticalAlignment.Center,FontWeight=FontWeights.Bold}});
   FillBadge(badge,teamDirectory.Logos(id,logo),size);
   return badge;
  }
  async void FillBadge(Grid badge,IEnumerable<string> urls,double size) {
   var image=await badges.Load(urls).ConfigureAwait(false);if(image==null)return;
   // Startup can request badges before Application.Run installs a UI context.
   try {
    await badge.Dispatcher.InvokeAsync(()=>{
     if(exiting)return;
     badge.Children.Clear();badge.Children.Add(new Image{Source=image,Width=size,Height=size,Stretch=Stretch.Uniform});
    }).Task.ConfigureAwait(false);
   }catch(System.Threading.Tasks.TaskCanceledException){} // Dispatcher is shutting down.
  }
  FrameworkElement Team(string name,string logo,string id) {
   var s=new StackPanel{VerticalAlignment=VerticalAlignment.Center};
   var badge=TeamBadge(name,logo,30,id);badge.Margin=new Thickness(0,0,0,7);
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
   tray=new Forms.NotifyIcon{Text="看球 · 桌面比分",Icon=applicationIcon,Visible=true};
   tray.DoubleClick+=(s,e)=>ShowCard();var menu=new Forms.ContextMenuStrip();tray.ContextMenuStrip=menu;
   traySummary=new Forms.ToolStripMenuItem{Enabled=false};menu.Items.Add(traySummary);menu.Items.Add(new Forms.ToolStripSeparator());
   trayVisibility=TrayItem(menu,"显示卡片",()=>{if(IsVisible)Hide();else ShowCard(false);});
   TrayItem(menu,"展开 / 收纳",()=>{SetPresentation(!preferences.Compact);Show();Activate();});
   trayPin=TrayItem(menu,"窗口置顶",()=>{Topmost=!Topmost;pinButton.Content=Topmost?"已置顶":"置顶";Save();UpdateTray();});
   TrayItem(menu,"关注球队…",()=>{ShowCard();ShowFavorites();});TrayItem(menu,"打开卡片并刷新",OpenAndRefresh);
   trayStartup=TrayItem(menu,"启动时仅显示托盘图标",()=>{preferences.StartInTray=!preferences.StartInTray;Save();UpdateTray();});
   TrayItem(menu,"设置…",()=>{ShowCard();ShowSettings();});trayUpdates=TrayItem(menu,"检查更新…",ShowUpdates);
   menu.Items.Add(new Forms.ToolStripSeparator());TrayItem(menu,"退出看球",Quit);menu.Opening+=(s,e)=>UpdateTray();UpdateTray();
  }
  Forms.ToolStripMenuItem TrayItem(Forms.ContextMenuStrip menu,string title,Action action){var item=new Forms.ToolStripMenuItem(title);item.Click+=(s,e)=>Dispatcher.Invoke(action);menu.Items.Add(item);return item;}
  void UpdateTray(){
   if(tray==null||exiting)return;
   int live=ScheduleView.Select(results.Where(x=>preferences.ShowCups||!League.IsCup(x.League.Id)).SelectMany(x=>x.Matches),preferences.Favorites,filter,liveOnly).Count(x=>x.State=="in");
   traySummary.Text=CanRefresh?"当前赛程 · "+live+" 场进行中":"比分已暂停 · 展开后同步";trayVisibility.Text=IsVisible?"隐藏卡片":"显示卡片";trayPin.Checked=Topmost;trayStartup.Checked=preferences.StartInTray;
   trayUpdates.Text=updater.Available==null?"检查更新…":"发现新版 "+updater.Available.Version+(updater.Ready?" · 已下载":"…");
   var team=teamDirectory.Primary(preferences.Favorites);string name=team==null?"看球":Names.Team(team.Name),tooltip=name+" · "+traySummary.Text;tray.Text=tooltip.Length>63?tooltip.Substring(0,63):tooltip;
   string key=team==null?"none":team.Id+"|"+team.Logo;if(trayBadgeKey==key)return;trayBadgeKey=key;int request=++trayBadgeRequest;
   tray.Icon=applicationIcon;if(teamTrayIcon!=null){teamTrayIcon.Dispose();teamTrayIcon=null;}if(team!=null)LoadTrayBadge(team,request);
  }
  async void LoadTrayBadge(TeamEntry team,int request){
   var bitmap=await badges.Load(teamDirectory.Logos(team.Id,team.Logo)).ConfigureAwait(false);
   try{await Dispatcher.InvokeAsync(()=>{
    if(exiting||request!=trayBadgeRequest)return;if(bitmap==null){trayBadgeKey=null;return;}
    try{var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var stream=new MemoryStream()){encoder.Save(stream);stream.Position=0;using(var source=new System.Drawing.Bitmap(stream))using(var scaled=new System.Drawing.Bitmap(source,32,32)){var handle=scaled.GetHicon();try{using(var icon=System.Drawing.Icon.FromHandle(handle)){teamTrayIcon=(System.Drawing.Icon)icon.Clone();tray.Icon=teamTrayIcon;}}finally{DestroyIcon(handle);}}}}catch(Exception){trayBadgeKey=null;}
   }).Task.ConfigureAwait(false);}catch(System.Threading.Tasks.TaskCanceledException){}
  }
  [System.Runtime.InteropServices.DllImport("user32.dll")]static extern bool DestroyIcon(IntPtr icon);
  void ShowUpdates(){
   if(updateDialog!=null){updateDialog.Activate();return;}
   Hide();
   var panel=new StackPanel{Margin=new Thickness(22)};var dialog=new Window{Title="看球更新",Width=390,SizeToContent=SizeToContent.Height,ResizeMode=ResizeMode.NoResize,Background=B("#182830"),Foreground=Ink,FontFamily=FontFamily,Content=panel,WindowStartupLocation=WindowStartupLocation.CenterScreen};updateDialog=dialog;dialog.Resources=Resources;
   panel.Children.Add(T("看球 · "+AppUpdater.CurrentVersion,19,Ink,true));var status=T(updater.Status,12,Muted);status.Margin=new Thickness(0,18,0,18);panel.Children.Add(status);
   var check=Btn("检查更新",async ()=>await updater.Check());panel.Children.Add(check);
   var install=Btn("下载更新",async ()=>{if(!updater.Ready){await updater.Download();return;}try{Save();updater.BeginInstall(true);dialog.Close();exiting=true;Close();}catch(Exception ex){MessageBox.Show(dialog,"更新未安装："+ex.Message,"看球更新");}});install.Margin=new Thickness(0,8,0,0);panel.Children.Add(install);
   Action draw=()=>{status.Text=updater.Status;check.IsEnabled=!updater.Busy;install.IsEnabled=!updater.Busy&&updater.Available!=null;install.Content=updater.Ready?"安装并重启":"下载更新";};updater.Changed+=draw;dialog.Closed+=(s,e)=>{updater.Changed-=draw;updateDialog=null;};draw();dialog.Show();if(updater.Available==null)CheckManually();
  }
  async void CheckManually(){await updater.Check();}
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
     var teamLabel=new StackPanel{Orientation=Orientation.Horizontal};var icon=TeamBadge(team.Name,team.Logo,26,team.Id);icon.Margin=new Thickness(0,0,8,0);teamLabel.Children.Add(icon);teamLabel.Children.Add(label);
     var cb=new CheckBox{Content=teamLabel,IsChecked=preferences.Favorites.Contains(team.Id),Foreground=Ink,Margin=new Thickness(3,8,7,8),VerticalContentAlignment=VerticalAlignment.Center,HorizontalContentAlignment=HorizontalAlignment.Stretch,Padding=new Thickness(7,0,0,0)};
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
   var dialog=new Window{Title="看球设置",Owner=this,Width=380,Height=Math.Min(660,SystemParameters.WorkArea.Height-40),ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=B("#182830"),Foreground=Ink,FontFamily=FontFamily,Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto},Topmost=Topmost};
   panel.Children.Add(T("让比分留在你身边",17,Ink,true));
   var startTray=new CheckBox{Content="启动时仅显示托盘图标",IsChecked=preferences.StartInTray,Foreground=Ink,Margin=new Thickness(0,18,0,12)};startTray.Click+=(s,e)=>{preferences.StartInTray=startTray.IsChecked==true;Save();UpdateTray();};panel.Children.Add(startTray);
   var cups=new CheckBox{Content="显示国内杯赛",IsChecked=preferences.ShowCups,Foreground=Ink,Margin=new Thickness(0,0,0,12)};cups.Click+=(s,e)=>SetCups(cups.IsChecked==true);panel.Children.Add(cups);
   var autoStart=new CheckBox{Content="开机自启",Foreground=Ink,Margin=new Thickness(0,22,0,14),ToolTip="默认关闭；开启后登录 Windows 自动运行"};
   try{autoStart.IsChecked=startup.Enabled;}catch(Exception ex){autoStart.IsEnabled=false;autoStart.ToolTip="无法读取自启设置："+ex.Message;}
   autoStart.Click+=(s,e)=>{
    bool enabled=autoStart.IsChecked==true;
    try{startup.SetEnabled(enabled);}catch(Exception ex){autoStart.IsChecked=!enabled;MessageBox.Show(dialog,"自启设置未能保存："+ex.Message,"开机自启",MessageBoxButton.OK,MessageBoxImage.Warning);}
   };panel.Children.Add(autoStart);
   var sound=new CheckBox{Content="比分变化时播放提示音",IsChecked=preferences.Sound,Foreground=Ink,Margin=new Thickness(0,0,0,20)};sound.Checked+=(s,e)=>{preferences.Sound=true;Save();};sound.Unchecked+=(s,e)=>{preferences.Sound=false;Save();};panel.Children.Add(sound);
   foreach(bool compact in new[]{false,true}){var transparency=OpacityControls(compact);transparency.Margin=new Thickness(0,0,0,18);panel.Children.Add(transparency);}
   panel.Children.Add(T("完整卡片显示时更新比分；收纳或隐藏后暂停。退出请使用托盘菜单。",11,Muted));
   var checkUpdates=new CheckBox{Content="自动检查新版本（每天）",IsChecked=preferences.AutoCheckUpdates,Foreground=Ink,Margin=new Thickness(0,18,0,12)};
   var downloadUpdates=new CheckBox{Content="自动下载并在退出时安装",IsChecked=preferences.AutoDownloadUpdates,IsEnabled=preferences.AutoCheckUpdates,Foreground=Ink,Margin=new Thickness(0,0,0,12)};
   checkUpdates.Click+=(s,e)=>{preferences.AutoCheckUpdates=checkUpdates.IsChecked==true;downloadUpdates.IsEnabled=preferences.AutoCheckUpdates;Save();};downloadUpdates.Click+=(s,e)=>{preferences.AutoDownloadUpdates=downloadUpdates.IsChecked==true;Save();};panel.Children.Add(checkUpdates);panel.Children.Add(downloadUpdates);
   panel.Children.Add(Btn("检查更新 · "+AppUpdater.CurrentVersion,()=>{dialog.Close();ShowUpdates();}));
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
   preferences.CaptureLayout(Left,Top,Width,Height,Opacity);preferences.Pinned=Topmost;
   try{preferenceStore.Save(preferences);}catch(Exception){if(notice!=null){notice.Text="窗口设置未能保存，请检查本地目录权限。";noticeBox.Visibility=Visibility.Visible;noticeUntil=DateTimeOffset.UtcNow.AddMinutes(2);}}
  }
  const string Styles=@"<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
  <Style TargetType='Button'><Setter Property='Foreground' Value='#CBD8DE'/><Setter Property='Background' Value='#22323A'/><Setter Property='BorderThickness' Value='0'/><Setter Property='FontFamily' Value='Microsoft YaHei UI'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'><Border x:Name='surface' Background='{TemplateBinding Background}' CornerRadius='6' Padding='{TemplateBinding Padding}'><ContentPresenter HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}' VerticalAlignment='Center'/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='surface' Property='Opacity' Value='.75'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter TargetName='surface' Property='Opacity' Value='.4'/></Trigger><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='surface' Property='BorderBrush' Value='#63E7AE'/><Setter TargetName='surface' Property='BorderThickness' Value='1'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
  <Style x:Key='CardButton' TargetType='Button'><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'><Border x:Name='surface' Background='Transparent' CornerRadius='10'><ContentPresenter HorizontalAlignment='Stretch'/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='surface' Property='Background' Value='#102FE0AC'/></Trigger><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='surface' Property='BorderBrush' Value='#63E7AE'/><Setter TargetName='surface' Property='BorderThickness' Value='1'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
  <Style TargetType='ScrollBar'><Setter Property='Width' Value='5'/><Setter Property='Background' Value='Transparent'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ScrollBar'><Track x:Name='PART_Track' IsDirectionReversed='True' Orientation='{TemplateBinding Orientation}' Minimum='{TemplateBinding Minimum}' Maximum='{TemplateBinding Maximum}' Value='{Binding Value, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}' ViewportSize='{TemplateBinding ViewportSize}'><Track.DecreaseRepeatButton><RepeatButton Command='ScrollBar.PageUpCommand' Opacity='0'/></Track.DecreaseRepeatButton><Track.Thumb><Thumb><Thumb.Template><ControlTemplate TargetType='Thumb'><Border Background='#46606B' CornerRadius='3'/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb><Track.IncreaseRepeatButton><RepeatButton Command='ScrollBar.PageDownCommand' Opacity='0'/></Track.IncreaseRepeatButton></Track></ControlTemplate></Setter.Value></Setter></Style>
 </ResourceDictionary>";
 }
 public static class Program {
  [STAThread] public static int Main(string[] args) {
   if(args.Length==2&&args[0]=="--apply-update")return AppUpdater.Install(args[1]);
   bool created;using(var mutex=new Mutex(true,"Local\\TouchlineCard.Desktop",out created)) {
    if(!created){MessageBox.Show("看球已经在运行。请在系统托盘中双击图标恢复卡片。","看球");return 0;}
    try{var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};var window=new CardWindow();app.MainWindow=window;window.Dispatcher.BeginInvoke(new Action(window.Start));app.Run();}catch(Exception ex){MessageBox.Show("无法启动看球卡片："+ex.Message,"看球");return 1;}
   }
   return 0;
  }
 }
}
