using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
namespace Touchline {
 public class UpdateManifest {
  public string Version,Platform,Url,Sha256;
  public long Size;
  class Envelope {public string Payload {get;set;} public string Signature {get;set;}}
  public static UpdateManifest Verify(string envelope,string publicKey){
   var serializer=new JavaScriptSerializer();var signed=serializer.Deserialize<Envelope>(envelope);
   if(signed==null)throw new FormatException("更新说明无效");
   var bytes=Convert.FromBase64String(signed.Payload);var signature=Convert.FromBase64String(signed.Signature);
   using(var rsa=new RSACryptoServiceProvider()){rsa.PersistKeyInCsp=false;rsa.FromXmlString(publicKey);if(!rsa.VerifyData(bytes,"SHA256",signature))throw new CryptographicException("更新签名验证失败");}
   var update=serializer.Deserialize<UpdateManifest>(Encoding.UTF8.GetString(bytes));Uri uri;
   if(update==null||update.Platform!="windows"||!Uri.TryCreate(update.Url,UriKind.Absolute,out uri)||uri.Scheme!="https"||uri.Host!="github.com"||!uri.AbsolutePath.StartsWith("/AEGO-WRJ/touchline-desktop/releases/download/",StringComparison.Ordinal)||!uri.AbsolutePath.EndsWith("/Touchline.exe",StringComparison.Ordinal)||uri.Query.Length>0||uri.Fragment.Length>0)throw new FormatException("不是 Windows 官方更新包");
   ParseVersion(update.Version);
   if(update.Size<=0||update.Size>64*1024*1024||update.Sha256==null||update.Sha256.Length!=64||!update.Sha256.All(c=>Uri.IsHexDigit(c)))throw new FormatException("更新校验信息无效");
   return update;
  }
  static System.Version ParseVersion(string text){System.Version value;if(!System.Version.TryParse(text,out value))throw new FormatException("版本号无效");return new System.Version(value.Major,value.Minor,Math.Max(0,value.Build),Math.Max(0,value.Revision));}
  public bool IsNewerThan(string version){return ParseVersion(Version)>ParseVersion(version);}
  public void VerifyFile(string path){
   if(new FileInfo(path).Length!=Size)throw new CryptographicException("更新包大小不符");
   string hash;using(var sha=SHA256.Create())using(var stream=File.OpenRead(path))hash=BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","");
   if(!String.Equals(hash,Sha256,StringComparison.OrdinalIgnoreCase))throw new CryptographicException("更新包校验失败");
   if(ParseVersion(AssemblyName.GetAssemblyName(path).Version.ToString())!=ParseVersion(Version))throw new CryptographicException("更新包版本不符");
  }
 }
}
