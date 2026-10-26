using System;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using Touchline;

class Tests {
 static int failures;
 static void Check(string name, Action test) { try { test(); Console.WriteLine("PASS " + name); } catch(Exception e) { failures++; Console.WriteLine("FAIL " + name + ": " + e.Message); } }
 static void Equal<T>(T want,T got) { if(!Object.Equals(want,got)) throw new Exception("expected " + want + ", got " + got); }
 static void WithStorage(Action<string> test) {
  var path=Path.Combine(Path.GetTempPath(),"TouchlineStorageTest-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(path);
  try{test(path);}finally{Directory.Delete(path,true);}
 }
 static string Event(string id,string date,string state,string status,string homeScore,string awayScore) {
  return "{\"id\":\""+id+"\",\"date\":\""+date+"\",\"competitions\":[{\"status\":{\"displayClock\":\"45'+2'\",\"type\":{\"name\":\""+status+"\",\"state\":\""+state+"\"}},\"competitors\":[{\"homeAway\":\"away\",\"id\":\"2\",\"score\":\""+awayScore+"\",\"team\":{\"displayName\":\"Away\"}},{\"homeAway\":\"home\",\"id\":\"1\",\"score\":\""+homeScore+"\",\"team\":{\"displayName\":\"Home\"}}]}]}";
 }
 static string Board(params string[] events) { return "{\"events\":["+String.Join(",",events)+"]}"; }
 static Match M(string state,string h,string a) { return new Match{Id="one",Home="Home",Away="Away",State=state,HomeScore=h,AwayScore=a}; }
 static int Main(string[] args) {
  var day = new DateTime(2026,10,26);
  Check("UTC requests cover Beijing midnight",()=>Equal("20261025,20261026",String.Join(",",MatchData.QueryDates(day))));
  Check("Beijing day includes 00:00 and excludes next midnight",()=>{
   var x=MatchData.Parse(Board(Event("before","2026-10-25T15:59Z","post","STATUS_FULL_TIME","0","0"),Event("start","2026-10-25T16:00Z","pre","STATUS_SCHEDULED","0","0"),Event("end","2026-10-26T15:59Z","pre","STATUS_SCHEDULED","0","0"),Event("after","2026-10-26T16:00Z","pre","STATUS_SCHEDULED","0","0")),"eng.1",day,false);
   Equal("start,end",String.Join(",",x.Select(m=>m.Id)));
   Equal("00:00",x[0].Kickoff.ToOffset(TimeSpan.FromHours(8)).ToString("HH:mm"));
  });
  Check("home away mapping is independent of API order",()=>{
   var m=MatchData.Parse(Board(Event("1","2026-10-26T12:00Z","in","STATUS_IN_PROGRESS","2","1")),"eng.1",day,false).Single();
   Equal("Home",m.Home); Equal("2",m.HomeScore); Equal("1",m.AwayScore);
  });
  Check("a live match survives Beijing midnight",()=>Equal(1,MatchData.Parse(Board(Event("1","2026-10-25T15:00Z","in","STATUS_IN_PROGRESS","1","1")),"eng.1",day,true).Count));
  Check("postponed matches never look live",()=>Equal("延期",MatchData.Parse(Board(Event("1","2026-10-26T12:00Z","pre","STATUS_POSTPONED","0","0")),"eng.1",day,false).Single().Status));
  Check("invalid provider response is not an empty schedule",()=>{bool thrown=false;try{MatchData.Parse("{\"error\":\"unavailable\"}","eng.1",day,false);}catch{thrown=true;} Equal(true,thrown);});
  Check("real provider stats and goal events parse",()=>{
   var m=MatchData.Parse(File.ReadAllText(args[0]),"eng.1",new DateTime(2026,10,25),false).First(x=>x.Id=="401879254");
   Equal("17",m.HomeStats["totalShots"]); Equal("47.4",m.AwayStats["possessionPct"]);
   Equal(2,m.Events.Count(x=>x.Kind=="进球")); Equal("William Osula",m.Events.First(x=>x.Kind=="进球").Player);
  });
  Check("first load is quiet and a new goal alerts only once",()=>{
   var t=new ScoreTracker(); Equal(0,t.Observe(new[]{M("in","0","0")}).Count);
   Equal(1,t.Observe(new[]{M("in","1","0")}).Count); Equal(0,t.Observe(new[]{M("in","1","0")}).Count);
  });
  Check("final whistle goals are retained",()=>{var t=new ScoreTracker();t.Observe(new[]{M("in","0","0")});Equal(1,t.Observe(new[]{M("post","1","0")}).Count);});
  Check("VAR correction is not announced as a goal",()=>{var t=new ScoreTracker();t.Observe(new[]{M("in","1","0")});Equal(true,t.Observe(new[]{M("in","0","0")}).Single().Contains("更正"));});
  Check("historical scores do not trigger notifications",()=>{var t=new ScoreTracker();t.Observe(new[]{M("post","1","0")});Equal(0,t.Observe(new[]{M("post","2","0")}).Count);});
  Check("schedule stays chronological across days and statuses",()=>{
   var games=new[]{new Match{Id="future",Kickoff=DateTimeOffset.Parse("2026-10-28T03:00+08:00"),State="pre"},new Match{Id="live",Kickoff=DateTimeOffset.Parse("2026-10-27T23:00+08:00"),State="in"},new Match{Id="finished",Kickoff=DateTimeOffset.Parse("2026-10-26T22:00+08:00"),State="post"}};
   Equal("finished,live,future",String.Join(",",ScheduleView.Select(games,new FavoriteSelection(),"all",false).Select(x=>x.Id)));
  });
  Check("either a home or away favorite qualifies, using stable team IDs",()=>{
   var f=new FavoriteSelection{OnlyFavorites=true};f.Set("42",true);f.Set("43",true);
   var games=new[]{new Match{Id="home",HomeId="42",AwayId="99"},new Match{Id="away",HomeId="99",AwayId="43"},new Match{Id="unrelated",HomeId="88",AwayId="99"}};
   Equal("away,home",String.Join(",",ScheduleView.Select(games,f,"all",false).Select(x=>x.Id)));
  });
  Check("following twice and then removing leaves no duplicate favorite",()=>{var f=new FavoriteSelection();f.Set("42",true);f.Set("42",true);Equal(1,f.TeamIds.Count);f.Set("42",false);Equal(false,f.Contains("42"));});
  Check("favorite teams and filter survive saving and reopening",()=>{var f=new FavoriteSelection{OnlyFavorites=true};f.Set("42",true);f.Set("43",true);var json=new JavaScriptSerializer().Serialize(f);var restored=new JavaScriptSerializer().Deserialize<FavoriteSelection>(json);Equal(true,restored.Contains("43"));Equal(true,restored.OnlyFavorites);});
  Check("only favorites with no teams is empty, toggling off restores matches",()=>{var f=new FavoriteSelection{OnlyFavorites=true};var games=new[]{new Match{Id="one",HomeId="42"}};Equal(0,ScheduleView.Select(games,f,"all",false).Count);f.OnlyFavorites=false;Equal(1,ScheduleView.Select(games,f,"all",false).Count);});
  Check("league, live and favorite filters intersect",()=>{var f=new FavoriteSelection{OnlyFavorites=true};f.Set("42",true);var games=new[]{new Match{Id="wanted",League="eng.1",State="in",HomeId="42"},new Match{Id="other-league",League="esp.1",State="in",HomeId="42"},new Match{Id="not-live",League="eng.1",State="pre",HomeId="42"}};Equal("wanted",ScheduleView.Select(games,f,"eng.1",true).Single().Id);});
  Check("multi-day query covers every UTC date without duplicate days",()=>Equal("20261025,20261026,20261027,20261028",String.Join(",",MatchData.QueryRange(day,day.AddDays(2)))));
  Check("multi-day parser includes future days and excludes next range midnight",()=>{
   var json=Board(Event("start","2026-10-25T16:00Z","post","STATUS_FULL_TIME","0","0"),Event("next","2026-10-27T10:00Z","pre","STATUS_SCHEDULED","0","0"),Event("end","2026-10-28T15:59Z","pre","STATUS_SCHEDULED","0","0"),Event("outside","2026-10-28T16:00Z","pre","STATUS_SCHEDULED","0","0"));
   Equal("start,next,end",String.Join(",",MatchData.ParseRange(json,"eng.1",day,day.AddDays(2),false).Select(x=>x.Id)));
  });
  Check("favorite order can move up or down and survives saving",()=>{var f=new FavoriteSelection();f.Set("83",true);f.Set("359",true);f.Set("86",true);f.Move("86",-1);Equal("83,86,359",String.Join(",",f.TeamIds));f.Move("83",1);Equal("86,83,359",String.Join(",",f.TeamIds));var restored=new JavaScriptSerializer().Deserialize<FavoriteSelection>(new JavaScriptSerializer().Serialize(f));Equal("86",restored.TeamIds[0]);});
  Check("favorite moves at boundaries do not remove or wrap a team",()=>{var f=new FavoriteSelection();f.Set("83",true);f.Set("86",true);f.Move("83",-1);f.Move("86",1);f.Move("missing",-1);Equal("83,86",String.Join(",",f.TeamIds));});
  Check("reordering favorites leaves fixture order unchanged",()=>{var f=new FavoriteSelection{OnlyFavorites=true};f.Set("83",true);f.Set("86",true);f.Move("86",-1);var games=new[]{new Match{Id="late",HomeId="86",Kickoff=DateTimeOffset.Parse("2026-10-27T12:00Z")},new Match{Id="early",HomeId="83",Kickoff=DateTimeOffset.Parse("2026-10-26T12:00Z")}};Equal("early,late",String.Join(",",ScheduleView.Select(games,f,"all",false).Select(x=>x.Id)));});
  Check("following an already followed team preserves its position",()=>{var f=new FavoriteSelection();f.Set("83",true);f.Set("86",true);f.Set("83",true);Equal("83,86",String.Join(",",f.TeamIds));});
  Check("primary team badge follows favorite order",()=>{var catalog=new TeamDirectory();var f=new FavoriteSelection();f.Set("83",true);f.Set("359",true);var primary=catalog.Primary(f);Equal("83",primary==null?null:primary.Id);Equal(true,primary.Logo.Contains("/83.png"));f.Move("359",-1);Equal("359",catalog.Primary(f).Id);Equal(true,catalog.Primary(f).Logo.Contains("/359.png"));f.Set("359",false);Equal("83",catalog.Primary(f).Id);f.Set("83",false);Equal<TeamEntry>(null,catalog.Primary(f));});
  Check("favorites, order and filter persist to disk across store instances",()=>WithStorage(path=>{var f=new FavoriteSelection{OnlyFavorites=true};f.Set("83",true);f.Set("359",true);f.Move("359",-1);new FavoriteStore(path).Save(f);var loaded=new FavoriteStore(path).Load();Equal("359,83",String.Join(",",loaded.TeamIds));Equal(true,loaded.OnlyFavorites);Equal(true,File.Exists(Path.Combine(path,"favorites.json")));}));
  Check("legacy favorites migrate independently of unrelated window settings",()=>WithStorage(path=>{File.WriteAllText(Path.Combine(path,"settings.json"),"{\"Left\":\"invalid-layout\",\"Favorites\":{\"TeamIds\":[\"83\",\"86\"],\"OnlyFavorites\":true}}");var loaded=new FavoriteStore(path).Load();Equal("83,86",String.Join(",",loaded.TeamIds));Equal(true,loaded.OnlyFavorites);Equal(true,File.Exists(Path.Combine(path,"favorites.json")));}));
  Check("a corrupted primary file recovers the latest saved backup",()=>WithStorage(path=>{var f=new FavoriteSelection();f.Set("83",true);new FavoriteStore(path).Save(f);f.Set("86",true);new FavoriteStore(path).Save(f);File.WriteAllText(Path.Combine(path,"favorites.json"),"{broken");var store=new FavoriteStore(path);Equal("83,86",String.Join(",",store.Load().TeamIds));Equal(true,!String.IsNullOrEmpty(store.Warning));Equal("83,86",String.Join(",",new FavoriteStore(path).Load().TeamIds));}));
  Check("clearing every favorite remains empty after restart, ignoring legacy data",()=>WithStorage(path=>{File.WriteAllText(Path.Combine(path,"settings.json"),"{\"Favorites\":{\"TeamIds\":[\"83\"]}}");new FavoriteStore(path).Save(new FavoriteSelection());Equal(0,new FavoriteStore(path).Load().TeamIds.Count);}));
  Check("locked storage reports failure and preserves previous choices",()=>WithStorage(path=>{var store=new FavoriteStore(path);var f=new FavoriteSelection();f.Set("83",true);store.Save(f);File.WriteAllText(store.FilePath,"{\"TeamIds\":[\"83\"],\"OnlyFavorites\":false}");f.Set("86",true);bool failed=false;using(var held=new FileStream(store.FilePath,FileMode.Open,FileAccess.Read,FileShare.None)){try{store.Save(f);}catch(IOException){failed=true;}catch(UnauthorizedAccessException){failed=true;}}Equal(true,failed);Equal("83",String.Join(",",new FavoriteStore(path).Load().TeamIds));}));
  Check("unrecoverable storage is reported and not silently overwritten",()=>WithStorage(path=>{File.WriteAllText(Path.Combine(path,"favorites.json"),"broken-primary");File.WriteAllText(Path.Combine(path,"favorites.json.bak"),"broken-backup");var store=new FavoriteStore(path);store.Load();Equal(true,!String.IsNullOrEmpty(store.Warning));Equal("broken-primary",File.ReadAllText(store.FilePath));}));
  Console.WriteLine("Failures: "+failures);return failures==0?0:1;
 }
}
