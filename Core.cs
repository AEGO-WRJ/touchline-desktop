using System;
using System.Collections.Generic;
using System.Collections;
using System.Globalization;
using System.Linq;
using System.Web.Script.Serialization;

namespace Touchline {
 public class Match {
  public string Id, League, Home, Away, HomeId, AwayId, HomeLogo, AwayLogo, State, StatusName, Clock, Status;
  public string HomeScore, AwayScore, Venue, Note;
  public DateTimeOffset Kickoff;
  public bool DataStale;
  public DateTimeOffset? DataUpdated;
  public Dictionary<string,string> HomeStats = new Dictionary<string,string>(), AwayStats = new Dictionary<string,string>();
  public List<MatchEvent> Events = new List<MatchEvent>();
 }
 public class MatchEvent { public string Clock, TeamId, Player, Kind; }
 public class FavoriteSelection {
  public List<string> TeamIds=new List<string>();
  public bool OnlyFavorites;
  public void Set(string id,bool followed) {
   if(TeamIds==null)TeamIds=new List<string>();
   TeamIds=TeamIds.Where(x=>!String.IsNullOrEmpty(x)).Distinct().ToList();
   if(followed&&!String.IsNullOrEmpty(id)){if(!TeamIds.Contains(id))TeamIds.Add(id);}
   if(!followed)TeamIds.RemoveAll(x=>x==id);
  }
  public bool Contains(string id) { return !String.IsNullOrEmpty(id) && TeamIds!=null && TeamIds.Contains(id); }
  public bool Includes(Match match) { return Contains(match.HomeId)||Contains(match.AwayId); }
  public void Move(string id,int offset) {
   if(TeamIds==null||offset==0)return;int current=TeamIds.IndexOf(id),target=current+Math.Sign(offset);
   if(current<0||target<0||target>=TeamIds.Count)return;
   TeamIds.RemoveAt(current);TeamIds.Insert(target,id);
  }
 }
 public static class ScheduleView {
  public static List<Match> Select(IEnumerable<Match> matches,FavoriteSelection favorites,string league,bool liveOnly) {
   return matches.Where(x=>(league=="all"||x.League==league)&&(!liveOnly||x.State=="in")&&(!favorites.OnlyFavorites||favorites.Includes(x)))
    .GroupBy(x=>x.Id).Select(x=>x.Last()).OrderBy(x=>x.Kickoff).ThenBy(x=>x.Id,StringComparer.Ordinal).ToList();
  }
 }
 public static class MatchData {
  public static string[] QueryDates(DateTime day) { return new[]{day.AddDays(-1).ToString("yyyyMMdd"),day.ToString("yyyyMMdd")}; }
  public static string[] QueryRange(DateTime start,DateTime end) {
   if(end.Date<start.Date)throw new ArgumentException("结束日期早于开始日期");
   return Enumerable.Range(0,(end.Date-start.Date).Days+2).Select(x=>start.Date.AddDays(x-1).ToString("yyyyMMdd")).ToArray();
  }
  public static DateTime BeijingToday { get { return DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8)).Date; } }
  public static Dictionary<string,object> Obj(object x) { return x as Dictionary<string,object> ?? new Dictionary<string,object>(); }
  public static object Get(object x,string key) { object v; return Obj(x).TryGetValue(key,out v)?v:null; }
  public static string Str(object x,string key) { return Convert.ToString(Get(x,key),CultureInfo.InvariantCulture); }
  public static IEnumerable<object> Items(object x) { return (x as IEnumerable ?? new object[0]).Cast<object>(); }
  public static bool Flag(object x,string key) { return Object.Equals(Get(x,key),true); }
  static Dictionary<string,string> Stats(object team) {
   return Items(Get(team,"statistics")).Where(x=>Str(x,"name")!="").GroupBy(x=>Str(x,"name")).ToDictionary(x=>x.Key,x=>Str(x.Last(),"displayValue"));
  }
  public static List<Match> Parse(string json, string league, DateTime day, bool carryLive) {
   return ParseRange(json,league,day,day,carryLive);
  }
  public static List<Match> ParseRange(string json,string league,DateTime start,DateTime end,bool carryLive) {
   var root=new JavaScriptSerializer{MaxJsonLength=8000000}.DeserializeObject(json);
   if(!(Get(root,"events") is object[])) throw new FormatException("数据源未返回有效赛程");
   var matches=new List<Match>();
   foreach(var ev in Items(Get(root,"events"))) {
    var comp=Items(Get(ev,"competitions")).FirstOrDefault(); if(comp==null) continue;
    var status=Get(comp,"status") ?? Get(ev,"status");var type=Get(status,"type");
    DateTimeOffset kickoff;
    if(!DateTimeOffset.TryParse(Str(ev,"date"),CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out kickoff)) continue;
    var localDay=kickoff.ToOffset(TimeSpan.FromHours(8)).Date;
    var state=Str(type,"state");
    if((localDay<start.Date||localDay>end.Date) && !(carryLive && state=="in" && localDay==start.Date.AddDays(-1))) continue;
    var sides=Items(Get(comp,"competitors")).ToList();
    var home=sides.FirstOrDefault(x=>Str(x,"homeAway")=="home");var away=sides.FirstOrDefault(x=>Str(x,"homeAway")=="away");
    if(home==null || away==null) continue;
    var m=new Match{Id=Str(ev,"id"),League=league,Kickoff=kickoff,State=state,StatusName=Str(type,"name"),Clock=Str(status,"displayClock"),
     Home=Str(Get(home,"team"),"displayName"),Away=Str(Get(away,"team"),"displayName"),HomeId=Str(home,"id"),AwayId=Str(away,"id"),
     HomeLogo=Str(Get(home,"team"),"logo"),AwayLogo=Str(Get(away,"team"),"logo"),HomeScore=Str(home,"score"),AwayScore=Str(away,"score"),
     Venue=Str(Get(comp,"venue"),"fullName"),HomeStats=Stats(home),AwayStats=Stats(away)};
    switch(m.StatusName) {
     case "STATUS_POSTPONED":m.Status="延期";break;
     case "STATUS_CANCELED":m.Status="取消";break;
     case "STATUS_SUSPENDED":m.Status="中断";break;
     case "STATUS_ABANDONED":m.Status="腰斩";break;
     case "STATUS_HALFTIME":m.Status="中场";break;
     case "STATUS_FINAL_PEN":m.Status="点球结束";break;
     case "STATUS_FINAL_AET":m.Status="加时结束";break;
     default:m.Status=state=="post"?"已结束":state=="in"?(m.Clock==""?"进行中":m.Clock):"未开始";break;
    }
    if(Object.Equals(Get(comp,"timeValid"),false)) m.Note="时间待定";
    foreach(var item in Items(Get(comp,"details"))) {
     string kind=Flag(item,"scoringPlay")?(Flag(item,"shootout")?"点球大战":Flag(item,"ownGoal")?"乌龙球":Flag(item,"penaltyKick")?"点球":"进球"):Flag(item,"redCard")?"红牌":Flag(item,"yellowCard")?"黄牌":"";
     if(kind=="") continue;
     var player=Items(Get(item,"athletesInvolved")).FirstOrDefault();
     m.Events.Add(new MatchEvent{Kind=kind,Clock=Str(Get(item,"clock"),"displayValue"),TeamId=Str(Get(item,"team"),"id"),Player=Str(player,"displayName")});
    }
    matches.Add(m);
   }
   return matches.GroupBy(x=>x.Id).Select(x=>x.Last()).OrderBy(x=>x.Kickoff).ToList();
  }
 }
 public class ScoreTracker {
  readonly Dictionary<string,Match> previous=new Dictionary<string,Match>();
  public List<string> Observe(IEnumerable<Match> matches) {
   var result=new List<string>();
   foreach(var m in matches) {
    Match old;
    if(previous.TryGetValue(m.Id,out old) && (old.State=="in" || m.State=="in") && (old.HomeScore!=m.HomeScore || old.AwayScore!=m.AwayScore)) {
     int h,a,oh,oa;
     if(Int32.TryParse(m.HomeScore,out h) && Int32.TryParse(m.AwayScore,out a) && Int32.TryParse(old.HomeScore,out oh) && Int32.TryParse(old.AwayScore,out oa))
      result.Add((h<oh || a<oa?"比分更正":"进球")+" · "+m.Home+" "+h+" : "+a+" "+m.Away);
    }
    previous[m.Id]=m;
   }
   return result;
  }
 }
}
