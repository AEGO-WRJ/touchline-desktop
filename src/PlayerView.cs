using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Touchline {
 public partial class CardWindow {
  readonly PlayerFeed playerFeed=new PlayerFeed();
  bool showPlayers,playerAway;
  int lineupMode;
  string squadKey,selectedPlayer,playerError;
  Match playerMatch;
  MatchSquad squad;
  StackPanel playerHost;
  Window pitchWindow;
  CancellationTokenSource playerRequest;
  DateTimeOffset playerDue=DateTimeOffset.MinValue;
  void CancelPlayers(){var request=playerRequest;playerRequest=null;if(request!=null)request.Cancel();playerDue=DateTimeOffset.MinValue;}
  void AddPlayerPanel(Match match,Panel parent){
   playerMatch=match;playerHost=new StackPanel();parent.Children.Add(playerHost);
   string key=match.League+":"+match.Id;
   if(squadKey!=key){ClosePitch();CancelPlayers();squadKey=key;squad=null;playerError=null;selectedPlayer=null;playerAway=false;}
   RenderPlayers();
  }
  async void SyncPlayers(bool force=false){
   if(!CanRefresh||playerHost==null||playerMatch==null){ClosePitch();CancelPlayers();return;}
   if(force)CancelPlayers();
   if(playerRequest!=null||DateTimeOffset.UtcNow<playerDue)return;
   var request=new CancellationTokenSource();playerRequest=request;var match=playerMatch;string key=squadKey;RenderPlayers();
   try{
    var loaded=await playerFeed.Load(match,request.Token);request.Token.ThrowIfCancellationRequested();
    if(!CanRefresh||key!=squadKey||playerHost==null)return;
    squad=loaded;playerError=null;
   }catch(OperationCanceledException){}catch(Exception){if(Object.ReferenceEquals(playerRequest,request))playerError="阵容同步失败";}
   finally{
    if(Object.ReferenceEquals(playerRequest,request)){playerRequest=null;playerDue=DateTimeOffset.UtcNow.AddSeconds(playerError!=null||(squad!=null?squad.State:match.State)=="in"?30:120);if(!exiting&&playerHost!=null)RenderPlayers();}
    request.Dispose();
   }
  }
  void RenderPlayers(){
   if(playerHost==null)return;playerHost.Children.Clear();
   var teams=Cols(-1,-1);var home=Btn(Names.Team(playerMatch.Home),()=>{playerAway=false;selectedPlayer=null;RenderPlayers();});var away=Btn(Names.Team(playerMatch.Away),()=>{playerAway=true;selectedPlayer=null;RenderPlayers();});home.Foreground=playerAway?Muted:Green;away.Foreground=playerAway?Green:Muted;At(teams,home,0);At(teams,away,1);playerHost.Children.Add(teams);
   var info=T(squad==null?(playerError??"正在读取阵容与评分…"):(playerError!=null?"连接异常 · 以下为上次阵容 · ":"")+"读取于 "+squad.Updated.ToOffset(TimeSpan.FromHours(8)).ToString("HH:mm:ss")+(String.IsNullOrEmpty(squad.Clock)?"":" · 比赛 "+squad.Clock)+(playerRequest!=null?" · 更新中":""),10,playerError==null?Muted:Amber);info.Margin=new Thickness(0,10,0,8);playerHost.Children.Add(info);
   if(squad==null){if(playerError!=null)playerHost.Children.Add(Btn("重试",()=>SyncPlayers(true)));return;}
   var team=squad.Teams.FirstOrDefault(x=>x.Id==(playerAway?playerMatch.AwayId:playerMatch.HomeId));
   if(team==null||team.Players.Count==0){RefreshPitch();playerHost.Children.Add(T(squad.State=="pre"?"本场阵容尚未公布，请临近开赛时再看。":"数据源暂未提供本场球员名单。",11,Muted));return;}
   var formation=T("首发阵型 · "+(String.IsNullOrEmpty(team.Formation)?"暂未提供":team.Formation),12,Ink,true);formation.Margin=new Thickness(0,3,0,10);playerHost.Children.Add(formation);
   var modes=Cols(-1,-1,-1);string[] labels={squad.State=="pre"?"赛前名单":squad.State=="post"?"结束时在场":"当前在场","首发","替补"};
   for(int i=0;i<labels.Length;i++){int mode=i;var button=Btn(labels[i],()=>{lineupMode=mode;selectedPlayer=null;RenderPlayers();});button.FontSize=10;button.Foreground=lineupMode==mode?Green:Muted;At(modes,button,i);}playerHost.Children.Add(modes);
   var players=team.Players.Where(p=>lineupMode==1?p.Starter:lineupMode==2?!p.Starter:p.OnPitch).ToList();
   if(lineupMode!=2){
    var positions=PitchLayout.Build(team,lineupMode==0);
    if(positions.Count>0){
     double height=Math.Max(420,positions.Select(p=>Math.Round(p.Y,2)).Distinct().Count()*74);
     var pitch=new PitchView(team,null,lineupMode==0,(p,a)=>{selectedPlayer=p.Id;RenderPlayers();});pitch.Height=height;pitch.Margin=new Thickness(0,12,0,0);playerHost.Children.Add(pitch);
    }
    var note=T(positions.Count==0?"暂无站位数据，可查看下方名单。":"阵型站位示意 · 号码下方为本场评分"+(players.Count>positions.Count?"\n"+(players.Count-positions.Count)+" 人位置未提供，见下方名单。":""),10,Muted);note.Margin=new Thickness(0,8,0,8);playerHost.Children.Add(note);
   }
   var fullPitch=Btn("双方球场  ↗",ShowPitch);fullPitch.Margin=new Thickness(0,4,0,8);playerHost.Children.Add(fullPitch);
   var selected=team.Players.FirstOrDefault(p=>p.Id==selectedPlayer);if(selected!=null)playerHost.Children.Add(PlayerFacts(selected));
   RefreshPitch();
   var heading=Cols(-1,48);heading.Margin=new Thickness(0,12,0,5);At(heading,T(players.Count+" 人 · 点击球员查看数据",10,Muted),0);At(heading,T("评分",10,Muted),1);playerHost.Children.Add(heading);
   foreach(var p in players){
    string state=p.SentOff?"红牌离场":p.SubbedOut?"已换下":p.SubbedIn?"替补上场":p.Starter?"首发":"未上场";
    var row=Cols(31,-1,44);row.Margin=new Thickness(0,8,0,8);var number=T(String.IsNullOrEmpty(p.Number)?"—":p.Number,12,Muted,true);number.VerticalAlignment=VerticalAlignment.Center;At(row,number,0);
    var name=new StackPanel();name.Children.Add(PitchView.PlayerLabel(p,11,Ink,true));name.Children.Add(T(p.Position+" · "+state,9,p.SentOff?Amber:Muted));At(row,name,1);
    var rating=T(p.Rating.HasValue?p.Rating.Value.ToString("0.0",CultureInfo.InvariantCulture):"—",13,p.Rating.HasValue?(p.Rating.Value>=7?Green:Ink):Muted,true);rating.VerticalAlignment=VerticalAlignment.Center;rating.ToolTip=p.Rating.HasValue?"FotMob 本场评分（10分制）":"暂无本场评分";At(row,rating,2);
    var button=new Button{Content=row,HorizontalContentAlignment=HorizontalAlignment.Stretch,Style=(Style)Resources["CardButton"],ToolTip="球员数据"};var player=p;button.Click+=(s,e)=>{selectedPlayer=selectedPlayer==player.Id?null:player.Id;RenderPlayers();};playerHost.Children.Add(button);
   }
   if(players.Count==0)playerHost.Children.Add(T("暂无此类球员记录",11,Muted));
   var source=T("↑ 换上 · ↓ 换下 · — 暂无评分\n阵容 / 统计：ESPN · 站位 / 本场评分：FotMob\n换人沿用被换下球员位置。"+(String.IsNullOrEmpty(squad.RatingNote)?"":"\n"+squad.RatingNote),9,Muted);source.Margin=new Thickness(0,12,0,0);playerHost.Children.Add(source);
  }
  FrameworkElement PlayerFacts(MatchPlayer p){
     var facts=new StackPanel{Margin=new Thickness(10,8,10,10)};
     facts.Children.Add(PitchView.PlayerLabel(p,12,Ink,true));facts.Children.Add(T(p.Number+"号 · "+p.Position,10,Muted));
     if(!String.IsNullOrEmpty(p.Age)||!String.IsNullOrEmpty(p.Country))facts.Children.Add(T((String.IsNullOrEmpty(p.Age)?"":p.Age+" 岁  ")+(p.Country??""),10,Muted));
     string[,] stats={{"totalGoals","进球"},{"goalAssists","助攻"},{"totalShots","射门"},{"shotsOnTarget","射正"},{"saves","扑救"},{"goalsConceded","失球"},{"foulsCommitted","犯规"},{"yellowCards","黄牌"},{"redCards","红牌"}};
     int visible=0;for(int n=0;n<stats.GetLength(0);n++){string value;if((stats[n,0]=="saves"||stats[n,0]=="goalsConceded")&&p.Position!="门将")continue;if(!p.Stats.TryGetValue(stats[n,0],out value))continue;var stat=Cols(-1,50);stat.Margin=new Thickness(0,3,0,3);At(stat,T(stats[n,1],10,Muted),0);At(stat,T(value,10,Ink),1);facts.Children.Add(stat);visible++;}
     if(visible==0)facts.Children.Add(T("暂无个人比赛统计",10,Muted));
     foreach(string ev in p.Events)facts.Children.Add(T(ev,10,Green));
     return new Border{Background=B("#23383D"),CornerRadius=new CornerRadius(6),Child=facts};
  }
  void ClosePitch(){if(pitchWindow!=null)pitchWindow.Close();}
  void ShowPitch(){
   if(pitchWindow!=null){pitchWindow.Activate();return;}if(squad==null||!CanRefresh)return;
   var area=SystemParameters.WorkArea;
   pitchWindow=new Window{Title="双方球场",Owner=this,Width=Math.Min(1100,area.Width-40),Height=Math.Min(820,area.Height-40),MinWidth=Math.Min(900,area.Width-40),MinHeight=Math.Min(570,area.Height-40),WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=B("#1D2C30"),Foreground=Ink,FontFamily=FontFamily,Topmost=Topmost};
   pitchWindow.Resources=Resources;pitchWindow.Closed+=(s,e)=>pitchWindow=null;pitchWindow.SizeChanged+=(s,e)=>RefreshPitch();RefreshPitch();pitchWindow.Show();
  }
  void RefreshPitch(){
   if(pitchWindow==null||squad==null)return;
   var home=squad.Teams.FirstOrDefault(t=>t.Id==playerMatch.HomeId)??new TeamSquad{Name=playerMatch.Home};var away=squad.Teams.FirstOrDefault(t=>t.Id==playerMatch.AwayId)??new TeamSquad{Name=playerMatch.Away};
   bool current=lineupMode!=1;string mode=current?(squad.State=="post"?"结束时在场":squad.State=="pre"?"赛前名单":"当前在场"):"首发";
   var root=new Grid{Margin=new Thickness(18)};for(int i=0;i<4;i++)root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
   var header=Cols(-1,-1,-1);header.Margin=new Thickness(0,0,0,12);
   var homeLabel=new StackPanel{Orientation=Orientation.Horizontal};homeLabel.Children.Add(TeamBadge(home.Name,playerMatch.HomeLogo,30,playerMatch.HomeId));var hn=T("  ● "+Names.Team(home.Name),14,Ink,true);hn.VerticalAlignment=VerticalAlignment.Center;homeLabel.Children.Add(hn);At(header,homeLabel,0);
   var formations=T((String.IsNullOrEmpty(home.Formation)?"—":home.Formation)+"  vs  "+(String.IsNullOrEmpty(away.Formation)?"—":away.Formation)+"\n"+mode,12,Ink,true);formations.TextAlignment=TextAlignment.Center;At(header,formations,1);
   var awayLabel=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};var an=T(Names.Team(away.Name)+"  ○  ",14,Ink,true);an.VerticalAlignment=VerticalAlignment.Center;awayLabel.Children.Add(an);awayLabel.Children.Add(TeamBadge(away.Name,playerMatch.AwayLogo,30,playerMatch.AwayId));At(header,awayLabel,2);
   header.Margin=new Thickness(10,10,10,10);var top=new StackPanel();top.Children.Add(new Border{Background=BadgeSurface(),CornerRadius=new CornerRadius(9),Margin=new Thickness(0,0,0,10),Child=header});var modes=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,0,10)};modes.Children.Add(Btn(squad.State=="post"?"结束时在场":squad.State=="pre"?"赛前名单":"当前在场",()=>{lineupMode=0;RenderPlayers();}));modes.Children.Add(Btn("首发阵容",()=>{lineupMode=1;RenderPlayers();}));top.Children.Add(modes);root.Children.Add(top);
   var chosen=(playerAway?away:home).Players.FirstOrDefault(p=>p.Id==selectedPlayer);
   var pitch=new PitchView(home,away,current,(p,isAway)=>{playerAway=isAway;selectedPlayer=p.Id;RenderPlayers();}){Height=Math.Max(440,pitchWindow.ActualHeight-220-(chosen!=null?180:0))};Grid.SetRow(pitch,1);root.Children.Add(pitch);
   int total=home.Players.Concat(away.Players).Count(p=>current?p.OnPitch:p.Starter),placed=PitchLayout.Build(home,current).Count+PitchLayout.Build(away,current).Count;
   var caption=T((playerError==null?"":"连接异常 · 上次阵容 · ")+"阵型站位示意 · 主队珊瑚红 / 客队米白 · ↑ 换上 · ↓ 换下 · 点击球员查看详情\n换人沿用被换下球员位置，非实时跑位。"+(placed==0?" 暂无站位数据。":total>placed?" "+(total-placed)+" 人位置未提供，可在卡片名单查看。":"")+"  读取于 "+squad.Updated.ToOffset(TimeSpan.FromHours(8)).ToString("HH:mm:ss"),10,playerError==null?Muted:Amber);caption.Margin=new Thickness(0,10,0,8);Grid.SetRow(caption,2);root.Children.Add(caption);
   if(chosen!=null){var details=new ScrollViewer{Content=PlayerFacts(chosen),MaxHeight=180,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};Grid.SetRow(details,3);root.Children.Add(details);}
   pitchWindow.Content=new ScrollViewer{Background=B("#1D2C30"),Content=root,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
  }
 }
}
