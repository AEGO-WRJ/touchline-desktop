using System;using System.Collections.Concurrent;using System.Collections.Generic;using System.Linq;using System.Net;using System.Net.Http;using System.Threading;using System.Threading.Tasks;
namespace Touchline {
 public class BoardFeed {
  static readonly HttpClient Http=CreateClient();static readonly SemaphoreSlim Slots=new SemaphoreSlim(3);
  readonly ConcurrentDictionary<string,LeagueBoard> cache=new ConcurrentDictionary<string,LeagueBoard>();
  static HttpClient CreateClient(){ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;var client=new HttpClient(new HttpClientHandler{AutomaticDecompression=DecompressionMethods.GZip|DecompressionMethods.Deflate}){Timeout=TimeSpan.FromSeconds(12),MaxResponseContentBufferSize=16000000};client.DefaultRequestHeaders.UserAgent.ParseAdd("TouchlineDesktop/1.7");return client;}
  async Task<string> Read(string url,CancellationToken token){await Slots.WaitAsync(token).ConfigureAwait(false);try{token.ThrowIfCancellationRequested();using(var r=await Http.GetAsync(url,token).ConfigureAwait(false)){r.EnsureSuccessStatusCode();return await r.Content.ReadAsStringAsync().ConfigureAwait(false);}}finally{Slots.Release();}}
  public async Task<LeagueBoard> Load(string league,bool force,CancellationToken token){
   token.ThrowIfCancellationRequested();string id=PlayerData.LeagueId(league);if(id=="")throw new ArgumentException("不支持的赛事");LeagueBoard board;
   if(!force&&cache.TryGetValue(id,out board)&&DateTimeOffset.UtcNow-board.Updated<TimeSpan.FromMinutes(5))return board;
   string json=await Read("https://www.fotmob.com/api/data/leagues?id="+id,token).ConfigureAwait(false);token.ThrowIfCancellationRequested();board=BoardData.Parse(json,id);cache[id]=board;return board;
  }
  public async Task<List<Leader>> Leaders(LeagueBoard board,string stat,CancellationToken token){string url;if(!board.Stats.TryGetValue(stat,out url))return new List<Leader>();string json=await Read(url,token).ConfigureAwait(false);token.ThrowIfCancellationRequested();return BoardData.Leaders(json,stat);}
  public async Task<Dictionary<string,string>> Rounds(IEnumerable<Match> matches,CancellationToken token){
   var output=new ConcurrentDictionary<string,string>();await Task.WhenAll(matches.GroupBy(m=>m.League).Select(async group=>{try{var b=await Load(group.Key,false,token).ConfigureAwait(false);foreach(var m in group){string round=BoardData.Round(b,m);if(round!=null)output[m.Id]=round;}}catch(Exception){token.ThrowIfCancellationRequested();}})).ConfigureAwait(false);return new Dictionary<string,string>(output);
  }
 }
}
