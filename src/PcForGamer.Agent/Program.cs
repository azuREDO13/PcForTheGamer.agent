using System.Diagnostics;
using System.Management;
using System.Text.Json;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:47613");
builder.Host.UseWindowsService();

// поменяй под свой сайт/поддомены
var allowedOrigins = new[] { "https://xn--90adear.xn--p1ai", "https://пкдляигрока.рф" };
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));
var app = builder.Build();
app.UseCors();

var token = AgentToken.LoadOrCreate();

// простая bearer-авторизация
bool Authed(HttpRequest r) =>
  r.Headers.TryGetValue("Authorization", out var v) &&
  v.ToString().Equals($"Bearer {token.Value}", StringComparison.Ordinal);

app.MapGet("/health", () => Results.Ok(new { ok = true, ver = "1.0.0" }));
app.MapGet("/v1/token", () => Results.Ok(new { token = token.Value })); // опционально выключить в проде

app.MapGet("/v1/info", (HttpRequest req) =>
{
  if (!Authed(req)) return Results.Unauthorized();
  var info = new {
    system = Wmi.Os(),
    cpu    = Wmi.Cpu(),
    motherboard = Wmi.Board(),
    gpu    = Wmi.Gpu(),
    audio  = Wmi.Audio(),
    network= Wmi.Net()
  };
  return Results.Json(info);
});

app.MapGet("/v1/issues", (HttpRequest req) =>
{
  if (!Authed(req)) return Results.Unauthorized();
  var issues = Pnp.Devices()
    .Where(d => !d.Status.Equals("OK", StringComparison.OrdinalIgnoreCase)
             || d.Class.Equals("Unknown", StringComparison.OrdinalIgnoreCase)
             || d.ProblemCode > 0)
    .ToList();
  return Results.Json(new { issues });
});

app.MapPost("/v1/install", async (HttpRequest req) =>
{
  if (!Authed(req)) return Results.Unauthorized();
  var body = await JsonSerializer.DeserializeAsync<InstallBody>(req.Body) ?? new InstallBody(new(), true);
  if (body.Packages.Count == 0) return Results.BadRequest(new { error = "packages required" });

  var cmds = body.Packages.Select(id =>
    $"winget install --id {Escape(id)} -e --source winget --accept-package-agreements --accept-source-agreements{(body.Silent?" --silent --disable-interactivity":"")}"
  );
  var ps = string.Join(" ; ", cmds);
  var r = RunPS(ps, TimeSpan.FromMinutes(30));
  return Results.Json(new { ok = r.ExitCode==0, r.ExitCode, r.Stdout, r.Stderr });
});

app.Run();

/* ===== helpers ===== */
static (int ExitCode, string Stdout, string Stderr) RunPS(string command, TimeSpan timeout)
{
  var psi = new ProcessStartInfo{
    FileName="powershell",
    Arguments=$"-NoProfile -ExecutionPolicy Bypass -Command \"{command}\"",
    RedirectStandardOutput=true, RedirectStandardError=true, UseShellExecute=false, CreateNoWindow=true
  };
  using var p = Process.Start(psi)!;
  var so = p.StandardOutput.ReadToEndAsync();
  var se = p.StandardError.ReadToEndAsync();
  if (!p.WaitForExit((int)timeout.TotalMilliseconds)) try { p.Kill(true);} catch {}
  return (p.ExitCode, so.Result, se.Result);
}
static string Escape(string s)=>s.Replace("\"","");

sealed class AgentToken{
  [JsonPropertyName("token")] public string Value { get; set; } = Guid.NewGuid().ToString("N");
  static string Path => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"PcForGamer","agent.json");
  public static AgentToken LoadOrCreate(){
    try{
      if (File.Exists(Path)) return JsonSerializer.Deserialize<AgentToken>(File.ReadAllText(Path))!;
      Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
      var t=new AgentToken(); File.WriteAllText(Path, JsonSerializer.Serialize(t)); return t;
    }catch{ return new AgentToken(); }
  }
}

static class Wmi{
  public static object Os(){ try{
    using var s=new ManagementObjectSearcher("SELECT Caption,Version,OSArchitecture FROM Win32_OperatingSystem");
    var x=s.Get().Cast<ManagementObject>().FirstOrDefault();
    return new { os=x?["Caption"]?.ToString(), version=x?["Version"]?.ToString(), arch=x?["OSArchitecture"]?.ToString() };
  }catch{ return new{}; } }

  public static object Cpu(){ try{
    using var s=new ManagementObjectSearcher("SELECT Name,Manufacturer FROM Win32_Processor");
    return s.Get().Cast<ManagementObject>().Select(m=> new { Name=m["Name"]?.ToString(), Manufacturer=m["Manufacturer"]?.ToString() }).ToList();
  }catch{ return Array.Empty<object>(); } }

  public static object Board(){ try{
    using var s=new ManagementObjectSearcher("SELECT Product,Manufacturer FROM Win32_BaseBoard");
    var x=s.Get().Cast<ManagementObject>().FirstOrDefault();
    return new { Product=x?["Product"]?.ToString(), Manufacturer=x?["Manufacturer"]?.ToString() };
  }catch{ return new{}; } }

  public static object Gpu(){ try{
    using var s=new ManagementObjectSearcher("SELECT Name,AdapterCompatibility,DriverVersion FROM Win32_VideoController");
    return s.Get().Cast<ManagementObject>().Select(m=> new {
      FriendlyName=m["Name"]?.ToString(), Manufacturer=m["AdapterCompatibility"]?.ToString(), DriverVersion=m["DriverVersion"]?.ToString()
    }).ToList();
  }catch{ return Array.Empty<object>(); } }

  public static object Audio(){ try{
    using var s=new ManagementObjectSearcher("SELECT Name,Manufacturer FROM Win32_SoundDevice");
    return s.Get().Cast<ManagementObject>().Select(m=> new { Name=m["Name"]?.ToString(), Manufacturer=m["Manufacturer"]?.ToString() }).ToList();
  }catch{ return Array.Empty<object>(); } }

  public static object Net(){ try{
    using var s=new ManagementObjectSearcher("SELECT Name,NetConnectionStatus,Manufacturer FROM Win32_NetworkAdapter WHERE PhysicalAdapter=TRUE");
    return s.Get().Cast<ManagementObject>().Select(m=> new {
      Name=m["Name"]?.ToString(), Status=m["NetConnectionStatus"]?.ToString(), Manufacturer=m["Manufacturer"]?.ToString()
    }).ToList();
  }catch{ return Array.Empty<object>(); } }
}

static class Pnp{
  public record PnpDevice(string Class,string FriendlyName,string Status,string InstanceId,int ProblemCode,string Manufacturer);
  public static IEnumerable<PnpDevice> Devices(){
    try{
      using var s=new ManagementObjectSearcher("SELECT Class,Name,FriendlyName,Status,PNPDeviceID,Manufacturer,ConfigManagerErrorCode FROM Win32_PnPEntity");
      foreach(ManagementObject m in s.Get()){
        yield return new PnpDevice(
          m["Class"]?.ToString()??"",
          m["FriendlyName"]?.ToString()??(m["Name"]?.ToString()??"Unknown device"),
          m["Status"]?.ToString()??"",
          m["PNPDeviceID"]?.ToString()??"",
          Convert.ToInt32(m["ConfigManagerErrorCode"]??0),
          m["Manufacturer"]?.ToString()??""
        );
      }
    }catch{ yield break; }
  }
}

record InstallBody(List<string> Packages, bool Silent);
