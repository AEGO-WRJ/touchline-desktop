using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Touchline {
 public class League {
  public string Id, Name, Color;
  public League(string id,string name,string color) { Id=id;Name=name;Color=color; }
  public static readonly League[] Regular={new League("eng.1","英超","#A795FF"),new League("esp.1","西甲","#FF987C"),new League("ita.1","意甲","#6FB7FF"),new League("ger.1","德甲","#FF7898"),new League("fra.1","法甲","#F3CE68"),new League("uefa.champions","欧冠","#83AFFF")};
  public static readonly League[] Cups={new League("eng.fa","英足总杯","#F1C779"),new League("eng.league_cup","英联赛杯","#63E7AE"),new League("ger.dfb_pokal","德国杯","#F1C779"),new League("esp.copa_del_rey","国王杯","#FF987C")};
  public static readonly League[] All=Regular.Concat(Cups).ToArray();
  public static bool IsCup(string id){return Cups.Any(x=>x.Id==id);}
  public static IEnumerable<League> Enabled(bool showCups){return showCups?All:Regular;}
 }
 public class LeagueResult {
  public League League;public List<Match> Matches=new List<Match>(); public bool Stale;public DateTimeOffset? Updated;public string Error;
 }
 public class ScoreFeed {
  static readonly HttpClient Http=CreateClient();
  static readonly SemaphoreSlim Slots=new SemaphoreSlim(6);
  readonly string cacheDir;
  public ScoreFeed(string directory) { cacheDir=directory;Directory.CreateDirectory(directory); }
  static HttpClient CreateClient() {
   ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;
   ServicePointManager.DefaultConnectionLimit=16;
   var c=new HttpClient();c.Timeout=TimeSpan.FromSeconds(18);c.DefaultRequestHeaders.UserAgent.ParseAdd("TouchlineDesktop/1.0");return c;
  }
  public Task<LeagueResult[]> Load(DateTime day) { return LoadRange(day,day,false); }
  public async Task<LeagueResult[]> LoadRange(DateTime start,DateTime end,bool force,bool showCups=true,CancellationToken token=default(CancellationToken)) { token.ThrowIfCancellationRequested();return await Task.WhenAll(League.Enabled(showCups).Select(x=>LoadLeague(x,start,end,force,token))); }
  async Task<LeagueResult> LoadLeague(League league,DateTime start,DateTime end,bool force,CancellationToken token) {
   var chunks=await Task.WhenAll(MatchData.QueryRange(start,end).Select(d=>LoadDay(league.Id,d,start,end,force,token)));token.ThrowIfCancellationRequested();
   var result=new LeagueResult{League=league,Stale=chunks.Any(x=>x.Stale),Error=String.Join("; ",chunks.Where(x=>x.Error!=null).Select(x=>x.Error))};
   result.Matches=chunks.SelectMany(x=>x.Matches).GroupBy(x=>x.Id).Select(x=>x.Last()).OrderBy(x=>x.Kickoff).ToList();
   var times=chunks.Where(x=>x.Updated.HasValue).Select(x=>x.Updated.Value).ToList();if(times.Count>0) result.Updated=times.Min();
   return result;
  }
  async Task<LeagueResult> LoadDay(string league,string utcDate,DateTime start,DateTime end,bool force,CancellationToken token) {
   var result=new LeagueResult();var path=Path.Combine(cacheDir,league+"_"+utcDate+".json");
   bool carry=start==MatchData.BeijingToday;
   try {
    token.ThrowIfCancellationRequested();
    var queryDay=DateTime.ParseExact(utcDate,"yyyyMMdd",System.Globalization.CultureInfo.InvariantCulture);
    var now=DateTime.UtcNow;bool recent=queryDay>=now.Date.AddDays(-1)&&queryDay<=now.Date;
    bool cached=!force&&!recent&&File.Exists(path)&&now-File.GetLastWriteTimeUtc(path)<(queryDay>now.Date?TimeSpan.FromMinutes(15):TimeSpan.FromHours(6));
    string json;
    if(cached) {json=File.ReadAllText(path);result.Updated=File.GetLastWriteTimeUtc(path);}
    else {
     await Slots.WaitAsync(token);
     try{token.ThrowIfCancellationRequested();using(var response=await Http.GetAsync("https://site.api.espn.com/apis/site/v2/sports/soccer/"+league+"/scoreboard?dates="+utcDate+"&limit=100",token)){response.EnsureSuccessStatusCode();json=await response.Content.ReadAsStringAsync();}}
     finally{Slots.Release();}
     result.Updated=DateTimeOffset.UtcNow;
    }
    token.ThrowIfCancellationRequested();result.Matches=MatchData.ParseRange(json,league,start,end,carry);
    if(!cached)try { File.WriteAllText(path,json); } catch(IOException) {} catch(UnauthorizedAccessException) {}
   } catch(Exception ex) {
    token.ThrowIfCancellationRequested();
    result.Stale=true;result.Error=ex is TaskCanceledException?"连接超时":ex.Message;
    try { if(File.Exists(path)) {result.Matches=MatchData.ParseRange(File.ReadAllText(path),league,start,end,carry);result.Updated=File.GetLastWriteTimeUtc(path);} } catch(Exception) {}
   }
   foreach(var m in result.Matches){m.DataStale=result.Stale;m.DataUpdated=result.Updated;}
   return result;
  }
 }
}
