using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace Touchline {
 public class MatchPlayer {
  public string Id,Name,Number,Position,Country,Age;
  public bool Starter,SubbedIn,SubbedOut,SentOff;
  public double? Rating;
  public Dictionary<string,string> Stats=new Dictionary<string,string>();
  public List<string> Events=new List<string>();
  public bool OnPitch {get{return (Starter||SubbedIn)&&!SubbedOut&&!SentOff;}}
 }
 public class TeamSquad {
  public string Id,Name,Formation;
  public List<MatchPlayer> Players=new List<MatchPlayer>();
 }
 public class MatchSquad {
  public string State,Clock,RatingNote;
  public DateTimeOffset Updated=DateTimeOffset.UtcNow;
  public List<TeamSquad> Teams=new List<TeamSquad>();
 }
 public static class PlayerData {
  static object Get(object x,string key){return MatchData.Get(x,key);}
  static string Str(object x,string key){return MatchData.Str(x,key);}
  static IEnumerable<object> Items(object x){return MatchData.Items(x);}
  static object Json(string json){return new JavaScriptSerializer{MaxJsonLength=8000000}.DeserializeObject(json);}
  static string Value(object stat){return Convert.ToString(Get(stat,"displayValue")??Get(stat,"value"),CultureInfo.InvariantCulture);}
  public static MatchSquad Parse(string json,Match match){
   var root=Json(json);var header=Get(root,"header");
   if(Str(header,"id")!=match.Id)throw new FormatException("比赛详情不匹配");
   var status=Get(Items(Get(header,"competitions")).FirstOrDefault(),"status");
   var result=new MatchSquad{State=Str(Get(status,"type"),"state"),Clock=Str(status,"displayClock")};
   if(result.State=="")result.State=match.State;
   foreach(var side in Items(Get(root,"rosters"))){
    var team=Get(side,"team");string id=Str(team,"id");if(id!=match.HomeId&&id!=match.AwayId)continue;
    var squad=new TeamSquad{Id=id,Name=id==match.HomeId?match.Home:match.Away,Formation=Str(side,"formation")};
    foreach(var entry in Items(Get(side,"roster"))){
     var athlete=Get(entry,"athlete");string playerId=Str(athlete,"id");if(playerId==""||squad.Players.Any(x=>x.Id==playerId))continue;
     var p=new MatchPlayer{Id=playerId,Name=Str(athlete,"displayName"),Number=Str(entry,"jersey"),Position=Position(Get(entry,"position")??Get(athlete,"position")),Starter=MatchData.Flag(entry,"starter"),SubbedIn=MatchData.Flag(entry,"subbedIn"),SubbedOut=MatchData.Flag(entry,"subbedOut")};
     if(p.Name=="")p.Name=Str(athlete,"fullName");
     foreach(var stat in Items(Get(entry,"stats")))if(Str(stat,"name")!="")p.Stats[Str(stat,"name")]=Value(stat);
     string red;double cards;p.SentOff=p.Stats.TryGetValue("redCards",out red)&&Double.TryParse(red,NumberStyles.Any,CultureInfo.InvariantCulture,out cards)&&cards>0;
     foreach(var play in Items(Get(entry,"plays"))){
      string kind=MatchData.Flag(play,"redCard")?"红牌":MatchData.Flag(play,"yellowCard")?"黄牌":MatchData.Flag(play,"ownGoal")?"乌龙球":MatchData.Flag(play,"scoringPlay")?"进球":MatchData.Flag(play,"substitution")?"换人":"";
      if(kind=="红牌")p.SentOff=true;if(kind!="")p.Events.Add(Str(Get(play,"clock"),"displayValue")+" "+kind);
     }
     string incoming=Str(Get(Get(entry,"subbedInFor"),"athlete"),"displayName");if(incoming!="")p.Events.Add("换下 "+incoming);
     string outgoing=Str(Get(Get(entry,"subbedOutFor"),"athlete"),"displayName");if(outgoing!="")p.Events.Add("被 "+outgoing+" 换下");
     squad.Players.Add(p);
    }
    result.Teams.Add(squad);
   }
   return result;
  }
  static string Position(object position){
   switch(Str(position,"abbreviation").ToUpperInvariant()){
    case "G":case "GK":return "门将";case "D":return "后卫";case "CD":case "CB":return "中后卫";case "CD-L":return "左中卫";case "CD-R":return "右中卫";
    case "LB":return "左后卫";case "RB":return "右后卫";case "LWB":return "左翼卫";case "RWB":return "右翼卫";
    case "M":case "CM":return "中场";case "LM":return "左中场";case "RM":return "右中场";case "AM":return "前腰";case "DM":return "后腰";
    case "AM-L":return "左前腰";case "AM-R":return "右前腰";case "AM-C":return "前腰";case "CM-L":return "左中场";case "CM-R":return "右中场";case "DM-L":case "DM-R":return "后腰";
    case "F":case "FW":case "CF":case "ST":return "前锋";case "LF":case "LW":return "左边锋";case "RF":case "RW":return "右边锋";case "SUB":case "":return "位置待提供";
    default:return Str(position,"displayName")==""?Str(position,"abbreviation"):Str(position,"displayName");
   }
  }
  static string Normalize(string value){
   var b=new StringBuilder();foreach(char c in (value??"").Replace("ø","o").Replace("Ø","O").Replace("ß","ss").Normalize(NormalizationForm.FormD))if(Char.IsLetterOrDigit(c)&&CharUnicodeInfo.GetUnicodeCategory(c)!=UnicodeCategory.NonSpacingMark)b.Append(Char.ToLowerInvariant(c));return b.ToString();
  }
  static string TeamKey(string value){
   string key=Normalize(value);
   switch(key){case "tottenham":value="Tottenham Hotspur";break;case "brighton":value="Brighton & Hove Albion";break;case "wolves":value="Wolverhampton Wanderers";break;case "newcastle":value="Newcastle United";break;case "inter":value="Internazionale";break;case "psg":value="Paris Saint-Germain";break;case "bayernmunchen":value="Bayern Munich";break;case "atleticomadrid":value="Atlético Madrid";break;case "athleticclub":case "athleticbilbao":value="Athletic Club";break;}
   return Normalize(Names.Team(value));
  }
  static string LeagueId(string league){switch(league){case "eng.1":return "47";case "esp.1":return "87";case "ger.1":return "54";case "ita.1":return "55";case "fra.1":return "53";case "uefa.champions":return "42";case "eng.fa":return "132";case "eng.league_cup":return "133";case "ger.dfb_pokal":return "209";case "esp.copa_del_rey":return "138";default:return "";}}
  static bool SameFixture(object home,object away,string time,Match m){
   DateTimeOffset date;return DateTimeOffset.TryParse(time,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out date)&&Math.Abs((date-m.Kickoff).TotalMinutes)<=5&&TeamKey(Str(home,"name"))==TeamKey(m.Home)&&TeamKey(Str(away,"name"))==TeamKey(m.Away);
  }
  public static string FindRatingMatch(string json,Match match){
   var found=new List<string>();string leagueId=LeagueId(match.League);if(leagueId=="")return null;
   foreach(var league in Items(Get(Json(json),"leagues"))){
    if(Str(league,"primaryId")!=leagueId&&Str(league,"id")!=leagueId)continue;
    foreach(var item in Items(Get(league,"matches")))if(SameFixture(Get(item,"home"),Get(item,"away"),Str(Get(item,"status"),"utcTime"),match)&&Str(item,"id")!="")found.Add(Str(item,"id"));
   }
   return found.Distinct().Count()==1?found[0]:null;
  }
  static bool SamePlayer(MatchPlayer p,object other){
   if(String.IsNullOrEmpty(p.Number)||p.Number!=Str(other,"shirtNumber"))return false;
   string name=Normalize(Str(other,"name"));if(name==Normalize(p.Name))return true;
   string last=Normalize(Str(other,"lastName"));return last.Length>=4&&name.EndsWith(last,StringComparison.Ordinal)&&p.Name.Split(' ','-').Any(x=>Normalize(x)==last);
  }
  public static void ApplyRatings(MatchSquad squad,string json,Match match,string expectedId){
   var root=Json(json);var general=Get(root,"general");
   if(Str(general,"matchId")!=expectedId||Str(general,"parentLeagueId")!=LeagueId(match.League)||!SameFixture(Get(general,"homeTeam"),Get(general,"awayTeam"),Str(general,"matchTimeUTCDate"),match))throw new FormatException("评分赛事不匹配");
   var lineup=Get(Get(root,"content"),"lineup");
   foreach(var team in squad.Teams){
    var side=Get(lineup,team.Id==match.HomeId?"homeTeam":"awayTeam");if(TeamKey(Str(side,"name"))!=TeamKey(team.Name))continue;
    var players=Items(Get(side,"starters")).Concat(Items(Get(side,"subs"))).ToList();
    foreach(var p in team.Players){
     var candidates=players.Where(x=>SamePlayer(p,x)).ToList();if(candidates.Count!=1)continue;var player=candidates[0];
     double rating;if(Double.TryParse(Str(Get(player,"performance"),"rating"),NumberStyles.Any,CultureInfo.InvariantCulture,out rating)&&rating>0&&rating<=10)p.Rating=rating;
     p.Age=Str(player,"age");p.Country=Str(player,"countryName");
     if(p.Position=="位置待提供")switch(Str(player,"usualPlayingPositionId")){case "0":p.Position="门将";break;case "1":p.Position="后卫";break;case "2":p.Position="中场";break;case "3":p.Position="前锋";break;}
    }
   }
  }
 }
}
