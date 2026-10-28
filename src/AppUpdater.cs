using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace Touchline {
 public class AppUpdater {
  public static string CurrentVersion {get{return Assembly.GetExecutingAssembly().GetName().Version.ToString(3);}}
  const string FeedUrl="https://raw.githubusercontent.com/AEGO-WRJ/touchline-desktop/main/windows/update.json";
  readonly string directory;
  readonly Preferences preferences;
  readonly Action save;
  readonly HttpClient http;
  string signedManifest;
  DateTimeOffset nextAttempt=DateTimeOffset.MinValue;
  public event Action Changed;
  public UpdateManifest Available {get;private set;}
  public bool Busy {get;private set;}
  public bool Ready {get;private set;}
  public string Status {get;private set;}
  string PendingFile {get{return Path.Combine(directory,"pending.json");}}
  string Package {get{return Path.Combine(directory,"Touchline-"+Available.Version+".exe");}}
  public static string PublicKey {get{using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("Touchline.UpdateKey"))using(var reader=new StreamReader(stream))return reader.ReadToEnd();}}

  public AppUpdater(string dataDirectory,Preferences preferences,Action save){
   directory=Path.Combine(dataDirectory,"updates");this.preferences=preferences;this.save=save;
   ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;
   http=new HttpClient{Timeout=TimeSpan.FromSeconds(90),MaxResponseContentBufferSize=64*1024*1024};http.DefaultRequestHeaders.UserAgent.ParseAdd("TouchlineDesktop/"+CurrentVersion);
   Status="当前版本 "+CurrentVersion;
   try {if(File.Exists(PendingFile)){signedManifest=File.ReadAllText(PendingFile);Available=UpdateManifest.Verify(signedManifest,PublicKey);if(Available.IsNewerThan(CurrentVersion)){Available.VerifyFile(Package);Ready=true;Status="新版 "+Available.Version+" 已下载";}else Available=null;}}catch(Exception){Available=null;Ready=false;}
   string result=Path.Combine(directory,"install-result.json");
   try{if(File.Exists(result)){var report=new JavaScriptSerializer().Deserialize<InstallResult>(File.ReadAllText(result));if(!report.Success)Status="上次更新未安装，当前版本可继续使用："+report.Error;File.Delete(result);}}catch(Exception){}
  }
  void Notify(){if(Changed!=null)Changed();}
  public async Task Tick(){
   if(Busy||!preferences.AutoCheckUpdates||DateTimeOffset.UtcNow<nextAttempt)return;
   DateTimeOffset last;if(DateTimeOffset.TryParse(preferences.LastUpdateCheck,out last)&&DateTimeOffset.UtcNow-last<TimeSpan.FromDays(1))return;
   await Check(preferences.AutoDownloadUpdates);
  }
  public async Task Check(bool automaticDownload=false){
   if(Busy)return;Busy=true;Status="正在检查更新…";Notify();nextAttempt=DateTimeOffset.UtcNow.AddHours(1);
   try {
    var signed=await http.GetStringAsync(FeedUrl);var manifest=UpdateManifest.Verify(signed,PublicKey);
    preferences.LastUpdateCheck=DateTimeOffset.UtcNow.ToString("o");save();
    if(manifest.IsNewerThan(CurrentVersion)){
     if(Available==null||Available.Version!=manifest.Version){Ready=false;Available=manifest;signedManifest=signed;}
     Status=Ready?"新版 "+Available.Version+" 已下载":"发现新版 "+Available.Version;
    }else if(!Ready){Available=null;Status="已是最新版本 "+CurrentVersion;}
   }catch(Exception ex){Status="检查失败，可稍后重试："+ex.Message;}
   finally{Busy=false;Notify();}
   if(automaticDownload&&preferences.AutoCheckUpdates&&preferences.AutoDownloadUpdates&&Available!=null&&!Ready)await Download();
  }
  public async Task Download(){
   if(Busy||Ready||Available==null)return;Busy=true;Status="正在下载 "+Available.Version+"…";Notify();string temp=null;
   try {
    Directory.CreateDirectory(directory);temp=Path.Combine(directory,Guid.NewGuid().ToString("N")+".download");
    var bytes=await http.GetByteArrayAsync(Available.Url);File.WriteAllBytes(temp,bytes);Available.VerifyFile(temp);
    if(File.Exists(Package))File.Delete(Package);File.Move(temp,Package);temp=null;PreferenceStore.WriteAtomic(PendingFile,signedManifest);
    Ready=true;Status="新版 "+Available.Version+" 已验证，可安装并重启";
   }catch(Exception ex){Status="下载未完成，当前版本不受影响："+ex.Message;}
   finally{try{if(temp!=null&&File.Exists(temp))File.Delete(temp);}catch(IOException){}catch(UnauthorizedAccessException){}Busy=false;Notify();}
  }
  public void BeginInstall(bool restart){
   if(!Ready||Available==null)throw new InvalidOperationException("请先下载更新");
   Available.VerifyFile(Package);
   string helper=Path.Combine(directory,"installer-"+Guid.NewGuid().ToString("N")+".exe");File.Copy(Assembly.GetExecutingAssembly().Location,helper);
   var current=Process.GetCurrentProcess();
   var job=new InstallJob{ParentId=current.Id,ParentStarted=current.StartTime.ToUniversalTime().Ticks,Target=Assembly.GetExecutingAssembly().Location,Package=Package,Manifest=signedManifest,Restart=restart};
   string path=Path.Combine(directory,"install-"+Guid.NewGuid().ToString("N")+".json");PreferenceStore.WriteAtomic(path,new JavaScriptSerializer().Serialize(job));
   Process.Start(new ProcessStartInfo(helper,"--apply-update \""+path+"\""){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden});
  }
  class InstallJob {public int ParentId;public long ParentStarted;public string Target,Package,Manifest;public bool Restart;}
  class InstallResult {public bool Success;public string Error;}
  public static int Install(string jobPath){
   string directory=Path.GetDirectoryName(Path.GetFullPath(jobPath)),temp=null;var result=new InstallResult();
   try {
    var job=new JavaScriptSerializer().Deserialize<InstallJob>(File.ReadAllText(jobPath));var update=UpdateManifest.Verify(job.Manifest,PublicKey);
    if(!update.IsNewerThan(CurrentVersion))throw new InvalidOperationException("更新版本必须高于当前版本");
    string package=Path.GetFullPath(job.Package),target=Path.GetFullPath(job.Target);
    if(!package.StartsWith(directory+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||!target.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)||target==package)throw new FormatException("更新路径无效");
    update.VerifyFile(package);
    try {using(var parent=Process.GetProcessById(job.ParentId)){
     if(parent.StartTime.ToUniversalTime().Ticks!=job.ParentStarted||!String.Equals(parent.MainModule.FileName,target,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("更新目标不是原程序");
     if(!parent.WaitForExit(60000))throw new IOException("旧版尚未退出，未替换程序");
    }}catch(ArgumentException){} // The original process has already exited.
    temp=target+".update-"+Guid.NewGuid().ToString("N")+".tmp";File.Copy(package,temp);update.VerifyFile(temp);
    File.Replace(temp,target,target+".previous",true);temp=null;
    if(job.Restart)Process.Start(new ProcessStartInfo(target){UseShellExecute=true,WorkingDirectory=Path.GetDirectoryName(target)});
    result.Success=true;
   }catch(Exception ex){result.Error=ex.Message;}
   finally {
    if(temp!=null&&File.Exists(temp))try{File.Delete(temp);}catch(IOException){}
    try{PreferenceStore.WriteAtomic(Path.Combine(directory,"install-result.json"),new JavaScriptSerializer().Serialize(result));File.Delete(jobPath);}catch(Exception){}
   }
   return result.Success?0:1;
  }
 }
}
