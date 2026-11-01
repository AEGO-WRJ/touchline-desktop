using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Touchline {
 public class PlayerFeed {
  static readonly HttpClient Http=CreateClient();
  readonly object cacheLock=new object();
  readonly Dictionary<string,string> ratingIds=new Dictionary<string,string>();
  string boardDate,boardJson;
  DateTimeOffset boardUpdated;
  static HttpClient CreateClient(){ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;var client=new HttpClient{Timeout=TimeSpan.FromSeconds(12),MaxResponseContentBufferSize=8000000};client.DefaultRequestHeaders.UserAgent.ParseAdd("TouchlineDesktop/1.4");return client;}
  static async Task<string> Read(string url,CancellationToken token){token.ThrowIfCancellationRequested();using(var response=await Http.GetAsync(url,token).ConfigureAwait(false)){response.EnsureSuccessStatusCode();return await response.Content.ReadAsStringAsync().ConfigureAwait(false);}}
  public async Task<MatchSquad> Load(Match match,CancellationToken token){
   var json=await Read("https://site.api.espn.com/apis/site/v2/sports/soccer/"+Uri.EscapeDataString(match.League)+"/summary?event="+Uri.EscapeDataString(match.Id),token).ConfigureAwait(false);
   token.ThrowIfCancellationRequested();var squad=PlayerData.Parse(json,match);
   try{
    string key=match.League+":"+match.Id,id;lock(cacheLock)ratingIds.TryGetValue(key,out id);
    if(id==null){
     string date=match.Kickoff.UtcDateTime.ToString("yyyyMMdd");
     string schedule;lock(cacheLock)schedule=boardDate==date&&DateTimeOffset.UtcNow-boardUpdated<TimeSpan.FromMinutes(5)?boardJson:null;
     if(schedule==null){
      schedule=await Read("https://www.fotmob.com/api/data/matches?date="+date,token).ConfigureAwait(false);token.ThrowIfCancellationRequested();lock(cacheLock){boardJson=schedule;boardDate=date;boardUpdated=DateTimeOffset.UtcNow;}
     }
     id=PlayerData.FindRatingMatch(schedule,match);
     if(id!=null)lock(cacheLock){if(ratingIds.Count>100)ratingIds.Clear();ratingIds[key]=id;}
    }
    if(id!=null){var ratings=await Read("https://www.fotmob.com/api/data/matchDetails?matchId="+Uri.EscapeDataString(id),token).ConfigureAwait(false);token.ThrowIfCancellationRequested();PlayerData.ApplyRatings(squad,ratings,match,id);}
    else squad.RatingNote="本场暂无可匹配的评分";
   }catch(Exception){token.ThrowIfCancellationRequested();squad.RatingNote="评分暂未同步，稍后重试";}
   token.ThrowIfCancellationRequested();squad.Updated=DateTimeOffset.UtcNow;return squad;
  }
 }
}
