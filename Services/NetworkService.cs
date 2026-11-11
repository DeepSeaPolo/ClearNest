using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
namespace ClearNest.Services;
public enum NetworkRepair {FlushDns,ResetWinsock,ResetTcpIp}
public record NetworkCheck(string Step,bool Success,string Details)
{
 public string Display=>$"{(Success?"通过":"未通过")} · {Step}\n{Details}";
}
public static class NetworkService
{
 private static readonly HttpClient http=new(new HttpClientHandler {AllowAutoRedirect=false}) {Timeout=Timeout.InfiniteTimeSpan};
 [DllImport("dnsapi.dll")] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool DnsFlushResolverCache();
 [DllImport("kernel32.dll")] private static extern uint GetOEMCP();
 public static string NormalizeHost(string input)
 {
  string host=input.Trim();
  if(host.Length==0||host.Length>253||host.Any(char.IsWhiteSpace)||host.Contains('/')||host.Contains('\\')||host.Contains('@'))throw new ArgumentException("请输入域名或 IP 地址，不要包含网址路径、端口或命令。");
  if(IPAddress.TryParse(host,out var ip))return ip.ToString();
  host=new IdnMapping().GetAscii(host);
  if(Uri.CheckHostName(host)!=UriHostNameType.Dns||host.Any(c=>!(char.IsAsciiLetterOrDigit(c)||c is '-' or '.')))throw new ArgumentException("域名格式不正确。");
  return host;
 }
 public static string ReadAdapters(CancellationToken token=default)
 {
  var text=new StringBuilder();
  foreach(var adapter in NetworkInterface.GetAllNetworkInterfaces().Take(64))
  {
   token.ThrowIfCancellationRequested();
   text.AppendLine($"{adapter.Name} · {adapter.NetworkInterfaceType} · {adapter.OperationalStatus}");
   text.AppendLine(adapter.Description);
   try
   {
    var properties=adapter.GetIPProperties();
    text.AppendLine("地址："+string.Join(" · ",properties.UnicastAddresses.Select(x=>x.Address.ToString()).Take(12)));
    text.AppendLine("网关："+string.Join(" · ",properties.GatewayAddresses.Select(x=>x.Address.ToString()).Take(8)));
    text.AppendLine("DNS："+string.Join(" · ",properties.DnsAddresses.Take(8)));
    if(adapter.Speed>0)text.AppendLine($"链路速率：{adapter.Speed/1_000_000d:F0} Mbps（不是实测网速）");
    var statistics=adapter.GetIPStatistics();
    text.AppendLine($"本次连接累计：接收 {statistics.BytesReceived/1048576d:F1} MB · 发送 {statistics.BytesSent/1048576d:F1} MB");
   }
   catch(NetworkInformationException ex){text.AppendLine("部分信息不可用："+ex.Message);}
   text.AppendLine();
  }
  return text.Length==0?"没有检测到网络适配器。":text.ToString();
 }
 public static async Task ProbeTcpAsync(IPAddress[] addresses,int port,CancellationToken token)
 {
  token.ThrowIfCancellationRequested();
  using var client=new TcpClient();
  await client.ConnectAsync(addresses,port,token);
 }
 public static async Task<IReadOnlyList<NetworkCheck>> DiagnoseAsync(string input,IProgress<NetworkCheck>? progress,CancellationToken token)
 {
  string host=NormalizeHost(input);var rows=new List<NetworkCheck>();IPAddress[] addresses=Array.Empty<IPAddress>();
  async Task<bool> Step(string name,Func<CancellationToken,Task<string>> action)
  {
   token.ThrowIfCancellationRequested();using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(5));
   var clock=Stopwatch.StartNew();NetworkCheck row;
   try {string detail=await action(timeout.Token);row=new(name,true,$"{detail}\n耗时 {clock.Elapsed.TotalMilliseconds:F0} ms");}
   catch(OperationCanceledException)when(!token.IsCancellationRequested){row=new(name,false,"超过 5 秒，已停止等待。");}
   catch(Exception ex)when(ex is not OperationCanceledException){row=new(name,false,ex.Message);}
   rows.Add(row);progress?.Report(row);return row.Success;
  }
  if(!await Step("系统 DNS 解析",async ct=>{addresses=await Dns.GetHostAddressesAsync(host,ct);if(addresses.Length==0)throw new InvalidOperationException("系统没有返回地址。");return string.Join(" · ",addresses.Take(8).Select(x=>x.ToString()));}))return rows;
  await Step("直连 TCP 443",async ct=>{await ProbeTcpAsync(addresses,443,ct);return "目标 HTTPS 端口可连接。此项不经过系统 HTTP 代理。";});
  await Step("HTTPS 握手与响应",async ct=>
  {
   using var request=new HttpRequestMessage(HttpMethod.Head,new UriBuilder(Uri.UriSchemeHttps,host).Uri);
   using var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);
   return $"收到 HTTP {(int)response.StatusCode} {response.ReasonPhrase}；保留系统代理和证书验证，不下载正文、不跟随重定向。";
  });
  return rows;
 }
 public static async Task<string> RepairAsync(NetworkRepair repair,CancellationToken token)
 {
  token.ThrowIfCancellationRequested();
  if(!Enum.IsDefined(repair))throw new ArgumentOutOfRangeException(nameof(repair));
  using var identity=WindowsIdentity.GetCurrent();
  if(!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))throw new InvalidOperationException("此操作需要以管理员身份启动 ClearNest。");
  if(repair==NetworkRepair.FlushDns)
  {
   bool result=await Task.Run(DnsFlushResolverCache);
   if(!result)throw new InvalidOperationException("Windows 未能清空 DNS 缓存。");
   return "已清空本机 DNS 解析缓存。没有更改 DNS 服务器地址。";
  }
  string folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ClearNest","Logs");
  Directory.CreateDirectory(folder);
  string stamp=DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+"-"+Guid.NewGuid().ToString("N")[..6];
  string log=Path.Combine(folder,"network-"+stamp+".log");
  // Capture the pre-operation adapter configuration before changing system networking.
  await File.WriteAllTextAsync(log,"操作前网卡信息\n"+ReadAdapters(token),token);
  var start=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"netsh.exe")) {UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
  Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
  start.StandardOutputEncoding=start.StandardErrorEncoding=Encoding.GetEncoding((int)GetOEMCP());
  if(repair==NetworkRepair.ResetWinsock){start.ArgumentList.Add("winsock");start.ArgumentList.Add("reset");}
  else{start.ArgumentList.Add("int");start.ArgumentList.Add("ip");start.ArgumentList.Add("reset");start.ArgumentList.Add(Path.Combine(folder,"ip-reset-"+stamp+".log"));}
  token.ThrowIfCancellationRequested();
  using var process=Process.Start(start)??throw new InvalidOperationException("无法启动 Windows 网络工具。");
  var output=process.StandardOutput.ReadToEndAsync();var errors=process.StandardError.ReadToEndAsync();
  // Once a repair starts, let the single command finish; cancellation cannot roll system changes back.
  using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(45));
  try {await process.WaitForExitAsync(timeout.Token);}
  catch(OperationCanceledException)
  {
   try{process.Kill(true);await process.WaitForExitAsync();}catch(InvalidOperationException){}
   await File.AppendAllTextAsync(log,"\n网络命令超时，可能已部分修改系统设置。\n"+await output+"\n"+await errors);
   throw new TimeoutException("网络修复超过 45 秒，已停止命令；可能已部分生效。请检查日志："+log);
  }
  string details=await output+"\n"+await errors;
  await File.AppendAllTextAsync(log,$"\n操作：{repair}；退出码：{process.ExitCode}\n"+details);
  if(process.ExitCode!=0)throw new InvalidOperationException($"Windows 返回错误 {process.ExitCode}，可能已部分生效。\n{details}\n日志：{log}");
  return "Windows 网络命令已完成。部分变更需要你手动重启电脑才能生效。\n"+details+"\n日志："+log;
 }
}
