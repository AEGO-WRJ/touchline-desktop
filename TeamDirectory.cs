using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;
namespace Touchline {
 public class TeamEntry { public string Id,Name,League,Logo; }
 public class TeamDirectory {
  readonly List<TeamEntry> entries=new List<TeamEntry>();
  public TeamDirectory() {
   using(var input=Assembly.GetExecutingAssembly().GetManifestResourceStream("Touchline.Teams")) {
    if(input!=null)using(var reader=new StreamReader(input))entries.AddRange(new JavaScriptSerializer().Deserialize<List<TeamEntry>>(reader.ReadToEnd()));
   }
  }
  public void Learn(IEnumerable<Match> matches) {
   foreach(var m in matches) {Add(m.HomeId,m.Home,m.League,m.HomeLogo);Add(m.AwayId,m.Away,m.League,m.AwayLogo);}
  }
  public TeamEntry Primary(FavoriteSelection favorites) {
   string first=favorites.TeamIds==null?null:favorites.TeamIds.FirstOrDefault();
   return String.IsNullOrEmpty(first)?null:All.FirstOrDefault(x=>x.Id==first);
  }
  public IEnumerable<string> Logos(string id,string logo) {
   var sources=new List<string>();if(IsLogo(logo))sources.Add(logo);
   sources.AddRange(entries.Where(x=>x.Id==id&&IsLogo(x.Logo)).Select(x=>x.Logo));
   if(!String.IsNullOrEmpty(id)&&id.All(c=>c>='0'&&c<='9'))sources.Add("https://a.espncdn.com/i/teamlogos/soccer/500/"+id+".png");
   return sources.Distinct().ToArray();
  }
  internal static bool IsLogo(string url) {Uri uri;return Uri.TryCreate(url,UriKind.Absolute,out uri)&&uri.Scheme=="https"&&uri.Host=="a.espncdn.com";}
  void Add(string id,string name,string league,string logo) {
   if(!String.IsNullOrEmpty(id)&&!entries.Any(x=>x.Id==id&&x.League==league))entries.Add(new TeamEntry{Id=id,Name=name,League=league,Logo=logo});
  }
  public List<TeamEntry> All {
   get {return entries.GroupBy(x=>x.Id).Select(x=>new TeamEntry{Id=x.Key,Name=x.First().Name,Logo=x.Select(t=>t.Logo).FirstOrDefault(s=>!String.IsNullOrEmpty(s)),League=String.Join(" / ",x.Select(t=>League.All.FirstOrDefault(l=>l.Id==t.League)).Where(l=>l!=null).Select(l=>l.Name).Distinct())}).OrderBy(x=>Names.Team(x.Name),StringComparer.CurrentCulture).ToList();}
  }
 }
}
