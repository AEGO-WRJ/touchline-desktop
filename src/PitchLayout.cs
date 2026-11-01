using System;
using System.Collections.Generic;
using System.Linq;

namespace Touchline {
 public class PitchPosition {
  public MatchPlayer Player;
  public double X,Y;
 }
 public static class PitchLayout {
  public static List<PitchPosition> Build(TeamSquad team,bool current){
   var result=new List<PitchPosition>();if(team==null)return result;
   foreach(var player in team.Players.Where(p=>current?p.OnPitch:p.Starter)){
    var source=player;var visited=new HashSet<string>();
    while(source!=null&&visited.Add(source.Id??"")){
     if(source.PitchX.HasValue&&source.PitchY.HasValue){
      double x=source.PitchX.Value,y=source.PitchY.Value;
      if(x>=0&&x<=1&&y>=0&&y<=1)result.Add(new PitchPosition{Player=player,X=x,Y=y});break;
     }
     if(!current||String.IsNullOrEmpty(source.ReplacesId))break;
     source=team.Players.FirstOrDefault(p=>p.Id==source.ReplacesId);
    }
   }
   return result;
  }
 }
}
