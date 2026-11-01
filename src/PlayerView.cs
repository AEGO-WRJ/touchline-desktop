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
  CancellationTokenSource playerRequest;
  DateTimeOffset playerDue=DateTimeOffset.MinValue;
  void CancelPlayers(){var request=playerRequest;playerRequest=null;if(request!=null)request.Cancel();playerDue=DateTimeOffset.MinValue;}
  void AddPlayerPanel(Match match,Panel parent){
   playerMatch=match;playerHost=new StackPanel();parent.Children.Add(playerHost);
   string key=match.League+":"+match.Id;
   if(squadKey!=key){CancelPlayers();squadKey=key;squad=null;playerError=null;selectedPlayer=null;playerAway=false;}
   RenderPlayers();
  }
  async void SyncPlayers(bool force=false){
   if(!CanRefresh||playerHost==null||playerMatch==null){CancelPlayers();return;}
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
   if(team==null||team.Players.Count==0){playerHost.Children.Add(T(squad.State=="pre"?"本场阵容尚未公布，请临近开赛时再看。":"数据源暂未提供本场球员名单。",11,Muted));return;}
   var formation=T("首发阵型 · "+(String.IsNullOrEmpty(team.Formation)?"暂未提供":team.Formation),12,Ink,true);formation.Margin=new Thickness(0,3,0,10);playerHost.Children.Add(formation);
   var modes=Cols(-1,-1,-1);string[] labels={squad.State=="pre"?"赛前名单":squad.State=="post"?"结束时在场":"当前在场","首发","替补"};
   for(int i=0;i<labels.Length;i++){int mode=i;var button=Btn(labels[i],()=>{lineupMode=mode;selectedPlayer=null;RenderPlayers();});button.FontSize=10;button.Foreground=lineupMode==mode?Green:Muted;At(modes,button,i);}playerHost.Children.Add(modes);
   var players=team.Players.Where(p=>lineupMode==1?p.Starter:lineupMode==2?!p.Starter:p.OnPitch).ToList();
   var heading=Cols(-1,48);heading.Margin=new Thickness(0,12,0,5);At(heading,T(players.Count+" 人 · 点击球员查看数据",10,Muted),0);At(heading,T("评分",10,Muted),1);playerHost.Children.Add(heading);
   foreach(var p in players){
    string state=p.SentOff?"红牌离场":p.SubbedOut?"已换下":p.SubbedIn?"替补上场":p.Starter?"首发":"未上场";
    var row=Cols(31,-1,44);row.Margin=new Thickness(0,8,0,8);var number=T(String.IsNullOrEmpty(p.Number)?"—":p.Number,12,Muted,true);number.VerticalAlignment=VerticalAlignment.Center;At(row,number,0);
    var name=new StackPanel();name.Children.Add(T(p.Name,11,Ink,true));name.Children.Add(T(p.Position+" · "+state,9,p.SentOff?Amber:Muted));At(row,name,1);
    var rating=T(p.Rating.HasValue?p.Rating.Value.ToString("0.0",CultureInfo.InvariantCulture):"—",13,p.Rating.HasValue?(p.Rating.Value>=7?Green:Ink):Muted,true);rating.VerticalAlignment=VerticalAlignment.Center;rating.ToolTip=p.Rating.HasValue?"FotMob 本场评分（10分制）":"暂无本场评分";At(row,rating,2);
    var button=new Button{Content=row,HorizontalContentAlignment=HorizontalAlignment.Stretch,Style=(Style)Resources["CardButton"],ToolTip="球员数据"};var player=p;button.Click+=(s,e)=>{selectedPlayer=selectedPlayer==player.Id?null:player.Id;RenderPlayers();};playerHost.Children.Add(button);
    if(selectedPlayer==p.Id){
     var facts=new StackPanel{Margin=new Thickness(10,8,10,10)};
     if(!String.IsNullOrEmpty(p.Age)||!String.IsNullOrEmpty(p.Country))facts.Children.Add(T((String.IsNullOrEmpty(p.Age)?"":p.Age+" 岁  ")+(p.Country??""),10,Muted));
     string[,] stats={{"totalGoals","进球"},{"goalAssists","助攻"},{"totalShots","射门"},{"shotsOnTarget","射正"},{"saves","扑救"},{"goalsConceded","失球"},{"foulsCommitted","犯规"},{"yellowCards","黄牌"},{"redCards","红牌"}};
     int visible=0;for(int n=0;n<stats.GetLength(0);n++){string value;if((stats[n,0]=="saves"||stats[n,0]=="goalsConceded")&&p.Position!="门将")continue;if(!p.Stats.TryGetValue(stats[n,0],out value))continue;var stat=Cols(-1,50);stat.Margin=new Thickness(0,3,0,3);At(stat,T(stats[n,1],10,Muted),0);At(stat,T(value,10,Ink),1);facts.Children.Add(stat);visible++;}
     if(visible==0)facts.Children.Add(T("暂无个人比赛统计",10,Muted));
     foreach(string ev in p.Events)facts.Children.Add(T(ev,10,Green));
     playerHost.Children.Add(new Border{Background=B("#16282F"),CornerRadius=new CornerRadius(6),Child=facts});
    }
   }
   if(players.Count==0)playerHost.Children.Add(T("暂无此类球员记录",11,Muted));
   var source=T("阵容 / 统计：ESPN · 本场评分：FotMob\n— 表示暂无评分；在场名单随换人、红牌记录更新。"+(String.IsNullOrEmpty(squad.RatingNote)?"":"\n"+squad.RatingNote),9,Muted);source.Margin=new Thickness(0,12,0,0);playerHost.Children.Add(source);
  }
 }
}
