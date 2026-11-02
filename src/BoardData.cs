using System;using System.Collections.Generic;using System.Globalization;using System.Linq;using System.Web.Script.Serialization;
namespace Touchline {
 public class Standing {public string Rank,Name,Played,Wins,Draws,Losses,For,Against,Difference,Points;}
 public class StandingGroup {public string Name;public List<Standing> Rows=new List<Standing>();}
 public class Leader {public string Rank,Name,Team,Value;}
 public class LeagueBoard {
  public string Season,Id;public DateTimeOffset Updated=DateTimeOffset.UtcNow;
  public List<StandingGroup> Groups=new List<StandingGroup>();public List<object> Fixtures=new List<object>();
  public Dictionary<string,string> Stats=new Dictionary<string,string>();
 }
 public static class BoardData {
  static object G(object o,string k){return MatchData.Get(o,k);}static string S(object o,string k){return MatchData.Str(o,k);}static IEnumerable<object> A(object o){return MatchData.Items(o);}
  static object Json(string text){return new JavaScriptSerializer{MaxJsonLength=16000000}.DeserializeObject(text);}
  public static LeagueBoard Parse(string json,string id){
   var root=Json(json);var details=G(root,"details");if(S(details,"id")!=id||String.IsNullOrEmpty(S(details,"selectedSeason")))throw new FormatException("榜单赛事或赛季无效");
   var b=new LeagueBoard{Id=id,Season=S(details,"selectedSeason"),Fixtures=A(G(G(root,"fixtures"),"allMatches")).ToList()};
   foreach(var table in A(G(root,"table")))ReadTable(G(table,"data"),b);
   foreach(var stat in A(G(G(root,"stats"),"players"))){string key=S(stat,"name"),url=S(stat,"fetchAllUrl");Uri uri;if((key=="goals"||key=="goal_assist")&&Uri.TryCreate(url,UriKind.Absolute,out uri)&&uri.Scheme=="https"&&uri.Host=="data.fotmob.com"&&uri.AbsolutePath.StartsWith("/stats/"+id+"/",StringComparison.Ordinal)&&uri.AbsolutePath.EndsWith("/"+key+".json",StringComparison.Ordinal))b.Stats[key]=url;}
   return b;
  }
  static void ReadTable(object data,LeagueBoard board){
   var all=A(G(G(data,"table"),"all")).ToList();if(all.Count>0){var group=new StandingGroup{Name=Stage(S(data,"leagueName"))};foreach(var row in all){var goals=S(row,"scoresStr").Split('-');group.Rows.Add(new Standing{Rank=S(row,"idx"),Name=S(row,"name"),Played=S(row,"played"),Wins=S(row,"wins"),Draws=S(row,"draws"),Losses=S(row,"losses"),For=goals.Length==2?goals[0]:"—",Against=goals.Length==2?goals[1]:"—",Difference=S(row,"goalConDiff"),Points=S(row,"pts")});}board.Groups.Add(group);}
   foreach(var child in A(G(data,"tables")))ReadTable(child,board);
  }
  public static List<Leader> Leaders(string json,string stat){
   var root=Json(json);if(!(G(root,"TopLists") is object[]))throw new FormatException("球员榜单暂不可用");var list=A(G(root,"TopLists")).FirstOrDefault(x=>S(x,"StatName")==stat);if(list==null)throw new FormatException("球员榜单类型不匹配");
   return A(G(list,"StatList")).Select(x=>new Leader{Rank=S(x,"Rank"),Name=S(x,"ParticipantName"),Team=S(x,"TeamName"),Value=G(x,"StatValue")==null?"—":Convert.ToDecimal(G(x,"StatValue"),CultureInfo.InvariantCulture).ToString("0.##",CultureInfo.InvariantCulture)}).ToList();
  }
  public static string Round(LeagueBoard b,Match match){
   var fixtures=b.Fixtures.Where(f=>PlayerData.SameFixture(G(f,"home"),G(f,"away"),S(G(f,"status"),"utcTime"),match)).ToList();if(fixtures.Count!=1)return null;
   string value=S(fixtures[0],"roundName");if(value=="")value=S(fixtures[0],"round");int number;if(Int32.TryParse(value,out number)&&number>0)return "第"+number+"轮";return String.IsNullOrEmpty(value)?null:Stage(value);
  }
  public static string Stage(string s){switch(s){case "Eastern":return "东区";case "Western":return "西区";case "Supporters Shield":return "总榜（支持者盾）";case "Final":return "决赛";case "Semi-finals":case "Semi Final":return "半决赛";case "Quarter-finals":return "四分之一决赛";case "Round of 16":return "十六强";case "Group Stage":return "小组赛";case "League Stage":return "联赛阶段";default:return s;}}
 }
}
