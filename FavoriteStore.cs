using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using System.Web.Script.Serialization;

namespace Touchline {
 public class FavoriteStore {
  public string FilePath { get; private set; }
  public string Warning { get; private set; }
  readonly string directory;
  public FavoriteStore(string directory) { this.directory=Path.GetFullPath(directory);FilePath=Path.Combine(this.directory,"favorites.json"); }
  public FavoriteSelection Load() {
   Warning=null;FavoriteSelection value;
   if(TryRead(FilePath,out value))return value;
   if(TryRead(FilePath+".bak",out value)) {
    try{Save(value);Warning="关注配置已从本地备份恢复。";}
    catch(Exception){Warning="已读取关注备份，但主文件修复失败。请检查配置目录权限。";}
    return value;
   }
   var legacyPath=Path.Combine(directory,"settings.json");
   if(File.Exists(legacyPath)) {
    try {
     var json=new JavaScriptSerializer().DeserializeObject(File.ReadAllText(legacyPath));
     var legacy=MatchData.Get(json,"Favorites");
     if(legacy!=null) {
      value=Parse(legacy);
      try{Save(value);}catch(Exception){Warning="已读取旧版关注名单，但迁移保存失败。请检查配置目录权限。";}
      return value;
     }
    } catch(Exception){Warning="旧版配置无法读取；原文件已保留。";}
   }
   if(File.Exists(FilePath)||File.Exists(FilePath+".bak"))Warning="关注配置及备份无法读取；原文件已保留，请检查配置目录。";
   return new FavoriteSelection();
  }
  static bool TryRead(string path,out FavoriteSelection value) {
   value=null;if(!File.Exists(path))return false;
   try{value=Parse(new JavaScriptSerializer().DeserializeObject(File.ReadAllText(path)));return true;}
   catch(Exception){return false;}
  }
  static FavoriteSelection Parse(object data) {
   var ids=MatchData.Get(data,"TeamIds") as object[];
   if(ids==null||ids.Any(x=>!(x is string)))throw new FormatException("关注名单格式无效");
   var only=MatchData.Get(data,"OnlyFavorites");if(only!=null&&!(only is bool))throw new FormatException("关注筛选格式无效");
   return new FavoriteSelection{TeamIds=ids.Cast<string>().Select(x=>x.Trim()).Where(x=>x.Length>0).Distinct().ToList(),OnlyFavorites=Object.Equals(only,true)};
  }
  public void Save(FavoriteSelection favorites) {
   if(favorites==null)throw new ArgumentNullException("favorites");
   Warning=null;Directory.CreateDirectory(directory);
   var copy=new FavoriteSelection{TeamIds=(favorites.TeamIds??new List<string>()).Where(x=>!String.IsNullOrWhiteSpace(x)).Select(x=>x.Trim()).Distinct().ToList(),OnlyFavorites=favorites.OnlyFavorites};
   string json=new JavaScriptSerializer().Serialize(copy);
   AtomicWrite(FilePath,json);
   try{AtomicWrite(FilePath+".bak",json);}catch(IOException){Warning="关注名单已保存，但备份未能更新。";}catch(UnauthorizedAccessException){Warning="关注名单已保存，但备份未能更新。";}
  }
  static void AtomicWrite(string path,string text) {
   string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
   try {
    using(var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None))
    using(var writer=new StreamWriter(stream,new UTF8Encoding(false))){writer.Write(text);writer.Flush();stream.Flush(true);}
    if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);
   }finally{if(File.Exists(temp))File.Delete(temp);}
  }
 }
}
