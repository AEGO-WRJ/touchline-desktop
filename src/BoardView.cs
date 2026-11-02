using System;using System.Collections.Generic;using System.Linq;using System.Threading;using System.Windows;using System.Windows.Controls;
namespace Touchline {
 public partial class CardWindow {
  readonly BoardFeed boardFeed=new BoardFeed();
  Window boardWindow;StackPanel boardRows;TextBlock boardStatus;ComboBox boardLeague;string boardTab="table";LeagueBoard currentBoard;List<Leader> boardLeaders;
  CancellationTokenSource boardRequest;DateTimeOffset boardDue;string boardError;
  readonly Dictionary<string,string> roundLabels=new Dictionary<string,string>();
  CancellationTokenSource roundRequest;
  void CancelRounds(){var r=roundRequest;roundRequest=null;if(r!=null)r.Cancel();}
  async void RefreshRounds(LeagueResult[] incoming){
   CancelRounds();var request=new CancellationTokenSource();roundRequest=request;
   try{var labels=await boardFeed.Rounds(incoming.SelectMany(x=>x.Matches),request.Token);request.Token.ThrowIfCancellationRequested();if(CanRefresh&&Object.ReferenceEquals(results,incoming)){foreach(var m in incoming.SelectMany(x=>x.Matches)){string label;if(labels.TryGetValue(m.Id,out label)){m.Round=label;roundLabels[m.Id]=label;}}if(labels.Count>0)Render();}}
   catch(OperationCanceledException){}catch(Exception){}finally{if(Object.ReferenceEquals(roundRequest,request))roundRequest=null;request.Dispose();}
  }
  void CloseBoard(){if(boardWindow!=null)boardWindow.Close();}
  void ShowBoard(){
   if(boardWindow!=null){boardWindow.Activate();return;}ShowCard();var panel=new DockPanel{Margin=new Thickness(18),Background=B("#22373C")};var top=new StackPanel();DockPanel.SetDock(top,Dock.Top);panel.Children.Add(top);
   var controls=new StackPanel{Orientation=Orientation.Horizontal};boardLeague=new ComboBox{ItemsSource=League.All.Select(l=>l.Name).ToArray(),SelectedIndex=Math.Max(0,Array.FindIndex(League.All,l=>l.Id==filter)),Foreground=B("#22373C"),Width=170,Margin=new Thickness(0,0,12,0)};controls.Children.Add(boardLeague);
   foreach(string mode in new[]{"table","goals","goal_assist"}){string selected=mode;controls.Children.Add(Btn(mode=="table"?"积分榜":mode=="goals"?"射手榜":"助攻榜",()=>{if(boardTab==selected)return;boardTab=selected;boardLeaders=null;boardError=null;DrawBoard();LoadBoard(false);}));}
   controls.Children.Add(Btn("刷新",()=>LoadBoard(true)));top.Children.Add(controls);boardStatus=T("正在读取榜单…",11,Muted);boardStatus.Margin=new Thickness(0,14,0,14);top.Children.Add(boardStatus);
   boardRows=new StackPanel{MinWidth=690};panel.Children.Add(new ScrollViewer{Content=boardRows,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto});
   var area=SystemParameters.WorkArea;boardWindow=new Window{Title="联赛榜单",Owner=this,Width=Math.Min(850,area.Width-30),Height=Math.Min(730,area.Height-30),MinWidth=Math.Min(640,area.Width-30),MinHeight=400,Background=B("#22373C"),Foreground=Ink,FontFamily=FontFamily,Content=panel,Topmost=Topmost,WindowStartupLocation=WindowStartupLocation.CenterOwner};boardWindow.Resources=Resources;
   boardLeague.SelectionChanged+=(s,e)=>{currentBoard=null;boardLeaders=null;boardError=null;DrawBoard();LoadBoard(false);};
   boardWindow.Closed+=(s,e)=>{var r=boardRequest;boardRequest=null;if(r!=null)r.Cancel();boardWindow=null;currentBoard=null;boardLeaders=null;};boardWindow.Show();LoadBoard(false);
  }
  async void LoadBoard(bool force){
   if(boardWindow==null||!CanRefresh||boardLeague.SelectedIndex<0)return;var prior=boardRequest;if(prior!=null)prior.Cancel();var request=new CancellationTokenSource();boardRequest=request;var league=League.All[boardLeague.SelectedIndex];string tab=boardTab;boardStatus.Text="正在读取 "+league.Name+" 榜单…";
   try{var data=await boardFeed.Load(league.Id,force,request.Token);List<Leader> players=null;if(tab!="table")players=await boardFeed.Leaders(data,tab,request.Token);request.Token.ThrowIfCancellationRequested();if(boardWindow==null)return;currentBoard=data;boardLeaders=players;boardError=null;DrawBoard();}
   catch(OperationCanceledException){}catch(Exception){if(Object.ReferenceEquals(request,boardRequest)&&boardWindow!=null){boardError="连接异常";DrawBoard();}}
   finally{if(Object.ReferenceEquals(request,boardRequest)){boardRequest=null;boardDue=DateTimeOffset.UtcNow.AddMinutes(boardError==null?5:1);}request.Dispose();}
  }
  void DrawBoard(){
   if(boardRows==null||boardWindow==null||boardLeague.SelectedIndex<0)return;boardRows.Children.Clear();string title=boardTab=="table"?"积分榜":boardTab=="goals"?"射手榜":"助攻榜";
   boardStatus.Text=(League.All[boardLeague.SelectedIndex]).Name+" · "+title+(currentBoard==null?"":" · "+currentBoard.Season+" 赛季 · "+currentBoard.Updated.ToOffset(TimeSpan.FromHours(8)).ToString("MM-dd HH:mm")+" 读取 · FotMob")+(boardError==null?"":" · 连接异常"+(currentBoard==null||(boardTab!="table"&&boardLeaders==null)?"，请重试":"，显示上次数据"));
   if(currentBoard==null){boardRows.Children.Add(T(boardError==null?"正在读取…":"榜单暂不可用，点击刷新重试。",12,Muted));return;}
   if(boardTab=="table"){
    foreach(var group in currentBoard.Groups){var label=T(currentBoard.Groups.Count==1?League.All[boardLeague.SelectedIndex].Name:group.Name,13,Green,true);label.Margin=new Thickness(0,12,0,10);boardRows.Children.Add(label);boardRows.Children.Add(StandingRow(null));foreach(var row in group.Rows)boardRows.Children.Add(StandingRow(row));}
    if(currentBoard.Groups.Count==0)boardRows.Children.Add(T("数据源暂无本赛季积分榜；淘汰赛可能不设积分榜。",12,Muted));
   }else{
    if(boardLeaders==null){boardRows.Children.Add(T(boardError==null?"正在读取球员榜单…":"球员榜单暂不可用，点击刷新重试。",12,Muted));return;}
    var head=Cols(40,-1,75);At(head,T("排名",10,Muted),0);At(head,T("球员 / 球队",10,Muted),1);At(head,T(boardTab=="goals"?"进球":"助攻",10,Muted),2);boardRows.Children.Add(head);
    foreach(var player in boardLeaders){var row=Cols(40,-1,75);row.Margin=new Thickness(4,12,4,12);At(row,T(player.Rank,12,Muted),0);var name=new StackPanel();name.Children.Add(T(player.Name,14,Ink,true));name.Children.Add(T(Names.Team(player.Team),11,Muted));At(row,name,1);At(row,T(player.Value,16,Green,true),2);boardRows.Children.Add(new Border{BorderBrush=B("#40565D"),BorderThickness=new Thickness(0,0,0,1),Child=row});}
    if(boardLeaders.Count==0)boardRows.Children.Add(T("本赛季暂无此项球员榜单。",12,Muted));
   }
  }
  FrameworkElement StandingRow(Standing row){
   var grid=Cols(35,-1,46,40,40,40,46,46,46,50);grid.Margin=new Thickness(4,11,4,11);
   string[] cells=row==null?new[]{"排名","球队","场次","胜","平","负","进球","失球","净胜","积分"}:new[]{row.Rank,Names.Team(row.Name),row.Played,row.Wins,row.Draws,row.Losses,row.For,row.Against,row.Difference,row.Points};
   for(int i=0;i<cells.Length;i++){if(i==1&&row!=null){var name=new StackPanel{Orientation=Orientation.Horizontal};var team=teamDirectory.All.FirstOrDefault(t=>Names.Team(t.Name)==Names.Team(row.Name));if(team!=null){var badge=TeamBadge(team.Name,team.Logo,24,team.Id);badge.Margin=new Thickness(0,0,8,0);name.Children.Add(badge);}var label=T(cells[i],12,Ink,true);label.VerticalAlignment=VerticalAlignment.Center;name.Children.Add(label);At(grid,name,i);}else At(grid,T(String.IsNullOrEmpty(cells[i])?"—":cells[i],row==null?10:12,row==null?Muted:i==9?Green:Ink,i==9),i);}
   return new Border{Background=row==null?null:BadgeSurface(),BorderBrush=B("#40565D"),BorderThickness=new Thickness(0,0,0,1),Child=grid};
  }
 }
}
