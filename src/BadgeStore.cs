using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
namespace Touchline {
 public class BadgeStore {
  static readonly HttpClient Http=CreateClient();
  static readonly SemaphoreSlim Slots=new SemaphoreSlim(6);
  readonly object gate=new object();
  readonly Dictionary<string,BitmapSource> images=new Dictionary<string,BitmapSource>();
  readonly Dictionary<string,Task<BitmapSource>> pending=new Dictionary<string,Task<BitmapSource>>();
  readonly string cacheDir;
  public BadgeStore(string directory) {cacheDir=directory;}
  static HttpClient CreateClient() {
   ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;
   var client=new HttpClient{Timeout=TimeSpan.FromSeconds(10),MaxResponseContentBufferSize=2*1024*1024};
   client.DefaultRequestHeaders.UserAgent.ParseAdd("TouchlineDesktop/1.1");return client;
  }
  public async Task<BitmapSource> Load(IEnumerable<string> urls) {
   var sources=urls.Where(TeamDirectory.IsLogo).Distinct().ToArray();
   for(int attempt=0;attempt<2 && sources.Length>0;attempt++) {
    foreach(var url in sources){var image=await LoadOne(url).ConfigureAwait(false);if(image!=null)return image;}
    if(attempt==0)await Task.Delay(1500).ConfigureAwait(false);
   }
   return null;
  }
  Task<BitmapSource> LoadOne(string url) {
   lock(gate) {
    BitmapSource image;if(images.TryGetValue(url,out image))return Task.FromResult(image);
    Task<BitmapSource> task;if(pending.TryGetValue(url,out task))return task;
    task=Task.Run(async ()=>{
     try {var loaded=await Read(url).ConfigureAwait(false);if(loaded!=null)lock(gate)images[url]=loaded;return loaded;}
     finally {lock(gate)pending.Remove(url);}
    });
    pending[url]=task;return task;
   }
  }
  async Task<BitmapSource> Read(string url) {
   string key;using(var hash=SHA256.Create())key=BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(url))).Replace("-","");
   string path=Path.Combine(cacheDir,key+".png");BitmapSource cached=null;
   try {cached=Decode(File.ReadAllBytes(path));if(DateTime.UtcNow-File.GetLastWriteTimeUtc(path)<TimeSpan.FromDays(30))return cached;}catch(Exception){}
   await Slots.WaitAsync().ConfigureAwait(false);
   try {
    var bytes=await Http.GetByteArrayAsync(url).ConfigureAwait(false);var image=Decode(bytes);
    try {Directory.CreateDirectory(cacheDir);File.WriteAllBytes(path,bytes);}catch(IOException){}catch(UnauthorizedAccessException){}
    return image;
   }catch(Exception){return cached;}
   finally {Slots.Release();}
  }
  static BitmapSource Decode(byte[] bytes) {
   using(var stream=new MemoryStream(bytes)) {
    var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.DecodePixelWidth=128;image.StreamSource=stream;image.EndInit();image.Freeze();return image;
   }
  }
 }
}
