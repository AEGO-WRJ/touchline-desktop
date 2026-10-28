using System;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
namespace Touchline {
 public class PreferenceStore {
  readonly string path;
  public string Warning {get;private set;}
  public PreferenceStore(string directory){path=Path.Combine(directory,"settings.json");}
  public Preferences Load(){
   foreach(var file in new[]{path,path+".bak"}) {
    try {if(File.Exists(file)){var value=new JavaScriptSerializer().Deserialize<Preferences>(File.ReadAllText(file));if(value!=null){if(file!=path)Warning="窗口设置已从备份恢复。";return value;}}}catch(Exception){}
   }
   if(File.Exists(path)){try{File.Copy(path,path+".damaged-"+DateTime.UtcNow.Ticks);}catch(IOException){}Warning="窗口设置无法读取，已保留原文件并使用默认设置。";}
   return new Preferences();
  }
  public void Save(Preferences value){var json=new JavaScriptSerializer().Serialize(value);WriteAtomic(path,json);WriteAtomic(path+".bak",json);}
  internal static void WriteAtomic(string file,string text){
   Directory.CreateDirectory(Path.GetDirectoryName(file));string temp=file+"."+Guid.NewGuid().ToString("N")+".tmp";
   try {File.WriteAllText(temp,text,new UTF8Encoding(false));if(File.Exists(file))File.Replace(temp,file,null);else File.Move(temp,file);}
   finally{if(File.Exists(temp))File.Delete(temp);}
  }
 }
}
