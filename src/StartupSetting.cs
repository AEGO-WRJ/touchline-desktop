using System;
using System.IO;
using Microsoft.Win32;

namespace Touchline {
 public class StartupSetting {
  const string ValueName="TouchlineDesktop";
  readonly string registryPath,command;

  public StartupSetting(string executablePath,string registryPath=@"Software\Microsoft\Windows\CurrentVersion\Run") {
   this.registryPath=registryPath;
   command="\""+Path.GetFullPath(executablePath)+"\"";
  }

  public bool Enabled {
   get {using(var key=Registry.CurrentUser.OpenSubKey(registryPath))return key!=null&&!String.IsNullOrEmpty(key.GetValue(ValueName) as string);}
  }

  public void SetEnabled(bool enabled) {
   using(var key=enabled?Registry.CurrentUser.CreateSubKey(registryPath):Registry.CurrentUser.OpenSubKey(registryPath,true)) {
    if(key==null)return;
    if(enabled)key.SetValue(ValueName,command,RegistryValueKind.String);
    else key.DeleteValue(ValueName,false);
   }
  }

  public void RefreshPath() {
   using(var key=Registry.CurrentUser.OpenSubKey(registryPath)) {
    var saved=key==null?null:key.GetValue(ValueName) as string;
    if(String.IsNullOrEmpty(saved)||String.Equals(saved,command,StringComparison.OrdinalIgnoreCase))return;
   }
   SetEnabled(true);
  }
 }
}
