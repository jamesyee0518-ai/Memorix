# Memorix IIS 站点建立与生产部署操作手册

> 版本：V1.1  
> 日期：2026-07-18  
> 正式域名：`https://memorix.hiqer.top`  
> 适用环境：Windows Server、IIS、ASP.NET Core API、Next.js、PostgreSQL、腾讯云 COS

## 1. 目标架构

采用“一个公网入口站点 + 两个内部服务”的部署结构：

```text
公网用户
    │
    ▼
https://memorix.hiqer.top:443
IIS 公网站点 MemorixGateway
    ├── /api/*       → http://127.0.0.1:9101/api/*
    ├── /swagger/*   → http://127.0.0.1:9101/swagger/*
    ├── /health      → http://127.0.0.1:9101/health
    └── 其他请求      → http://127.0.0.1:3000/*
                              │
                 ┌────────────┴────────────┐
                 ▼                         ▼
        IIS 内部 API 站点             Next.js 服务
        MemorixApi                    Windows Service
        127.0.0.1:9101                127.0.0.1:3000
```

ASP.NET Core API 由 IIS `inprocess` 托管，不依赖交互式 PowerShell。Next.js 注册为 Windows Service。公网只开放 TCP 80 和 443，端口 3000、9101、5432 不对公网开放。

## 2. 部署前检查

服务器需要安装：

1. IIS Web Server 与 IIS Management Console；
2. IIS URL Rewrite Module；
3. Application Request Routing（ARR）；
4. 与项目 `net10.0` 匹配的 ASP.NET Core Hosting Bundle；
5. Node.js LTS；
6. NSSM 或 WinSW，用于托管 Next.js；
7. PostgreSQL 16 和 pgvector。

检查运行环境：

```powershell
dotnet --list-runtimes
node --version
npm --version

Get-WebGlobalModule |
    Where-Object Name -Like "AspNetCore*"
```

安装或更新 Hosting Bundle 后执行：

```powershell
iisreset
```

## 3. 创建服务器目录

使用管理员 PowerShell：

```powershell
$directories = @(
    "C:\Apps\Memorix\Api\current",
    "C:\Apps\Memorix\Api\releases",
    "C:\Apps\Memorix\Api\logs",
    "C:\Apps\Memorix\Web\current",
    "C:\Apps\Memorix\Web\logs",
    "C:\Apps\Memorix\Gateway",
    "C:\ProgramData\Memorix\DataProtection-Keys"
)

foreach ($directory in $directories) {
    New-Item $directory -ItemType Directory -Force
}
```

| 目录 | 用途 |
|---|---|
| `C:\Apps\Memorix\Api\current` | 当前 API 发布包 |
| `C:\Apps\Memorix\Api\releases` | API 历史发布包与回滚版本 |
| `C:\Apps\Memorix\Api\logs` | API 运维日志 |
| `C:\Apps\Memorix\Web\current` | Next.js standalone 发布包 |
| `C:\Apps\Memorix\Web\logs` | Next.js stdout/stderr 日志 |
| `C:\Apps\Memorix\Gateway` | IIS 公网反向代理站点 |
| `C:\ProgramData\Memorix\DataProtection-Keys` | ASP.NET Core Data Protection 密钥 |

## 4. 配置生产环境变量

### 4.1 基础配置

```powershell
[Environment]::SetEnvironmentVariable(
    "ASPNETCORE_ENVIRONMENT", "Production", "Machine")

[Environment]::SetEnvironmentVariable(
    "DatabaseProvider", "postgres", "Machine")

[Environment]::SetEnvironmentVariable(
    "Cors__AllowedOrigins__0", "https://memorix.hiqer.top", "Machine")

[Environment]::SetEnvironmentVariable(
    "Jwt__Issuer", "Memorix", "Machine")

[Environment]::SetEnvironmentVariable(
    "Jwt__Audience", "MemorixClients", "Machine")

[Environment]::SetEnvironmentVariable(
    "DataProtection__KeysPath",
    "C:\ProgramData\Memorix\DataProtection-Keys",
    "Machine")
```

CORS 域名末尾不要添加 `/`。

### 4.2 PostgreSQL 与 JWT

```powershell
[Environment]::SetEnvironmentVariable(
    "ConnectionStrings__DefaultConnection",
    "Host=127.0.0.1;Port=5432;Database=memorix;Username=memorix_app;Password=<数据库密码>",
    "Machine")

[Environment]::SetEnvironmentVariable(
    "Jwt__Secret", "<至少64字节的生产Secret>", "Machine")
```

不得继续使用仓库中的默认数据库密码或默认 JWT Secret。

### 4.3 腾讯云 COS

程序为兼容历史配置继续使用 `Minio__*` 键名：

```powershell
[Environment]::SetEnvironmentVariable(
    "Minio__Endpoint", "https://cos.ap-shanghai.myqcloud.com", "Machine")

[Environment]::SetEnvironmentVariable(
    "Minio__AccessKey", "<COS SecretId>", "Machine")

[Environment]::SetEnvironmentVariable(
    "Minio__SecretKey", "<COS SecretKey>", "Machine")

[Environment]::SetEnvironmentVariable(
    "Minio__Bucket", "<Bucket名称-APPID>", "Machine")

[Environment]::SetEnvironmentVariable(
    "Minio__Region", "ap-shanghai", "Machine")

[Environment]::SetEnvironmentVariable(
    "Minio__UseSsl", "true", "Machine")

[Environment]::SetEnvironmentVariable(
    "Minio__ForcePathStyle", "false", "Machine")

[Environment]::SetEnvironmentVariable(
    "Minio__AutoCreateBucket", "false", "Machine")

[Environment]::SetEnvironmentVariable(
    "Minio__UseConfiguredBucket", "true", "Machine")
```

COS 地域不是上海时，必须同步修改 Endpoint 和 Region。`UseConfiguredBucket=true` 用于将程序内部的逻辑 Bucket 映射到配置的 COS Bucket。

### 4.4 安全检查环境变量

只检查变量是否存在，不输出秘密值：

```powershell
$names = @(
    "ASPNETCORE_ENVIRONMENT",
    "ConnectionStrings__DefaultConnection",
    "Jwt__Secret",
    "Cors__AllowedOrigins__0",
    "Minio__Endpoint",
    "Minio__AccessKey",
    "Minio__SecretKey",
    "Minio__Bucket"
)

foreach ($name in $names) {
    $value = [Environment]::GetEnvironmentVariable($name, "Machine")
    [PSCustomObject]@{
        Name = $name
        Present = -not [string]::IsNullOrWhiteSpace($value)
        Length = if ($null -eq $value) { 0 } else { $value.Length }
    }
}
```

环境变量修改后回收应用池；如果 IIS 仍读取旧值，执行 `iisreset`。

## 5. API 上线前代码调整

### 5.1 修复重复日志

`Program.cs` 中 `ReadFrom.Configuration()` 已读取 `appsettings.json` 的 Console Sink，不应再次调用 `WriteTo.Console()`。

非 MCP 分支改为：

```csharp
else
{
    lc.ReadFrom.Configuration(ctx.Configuration)
      .Enrich.FromLogContext();
}
```

MCP 分支的 stderr Console Sink 保持不变。

### 5.2 配置 Data Protection

在 `Program.cs` 添加：

```csharp
using Microsoft.AspNetCore.DataProtection;
```

在 `builder.Build()` 之前注册：

```csharp
var keysPath = builder.Configuration["DataProtection:KeysPath"]
    ?? @"C:\ProgramData\Memorix\DataProtection-Keys";

builder.Services.AddDataProtection()
    .SetApplicationName("Memorix")
    .PersistKeysToFileSystem(new DirectoryInfo(keysPath));
```

### 5.3 配置 Forwarded Headers

添加：

```csharp
using Microsoft.AspNetCore.HttpOverrides;
```

在 `builder.Build()` 之前注册：

```csharp
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor |
        ForwardedHeaders.XForwardedProto |
        ForwardedHeaders.XForwardedHost;

    options.KnownProxies.Add(System.Net.IPAddress.Loopback);
    options.KnownProxies.Add(System.Net.IPAddress.IPv6Loopback);
});
```

在 `builder.Build()` 之后、其他中间件之前调用：

```csharp
app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}
```

## 6. 发布 API

在构建机器执行：

```powershell
dotnet publish .\src\KnowledgeEngine.Api\KnowledgeEngine.Api.csproj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -o .\publish\MemorixApi
```

将完整发布目录复制到：

```text
C:\Apps\Memorix\Api\current
```

确认至少包含：

```text
KnowledgeEngine.Api.exe
KnowledgeEngine.Api.dll
KnowledgeEngine.Api.deps.json
KnowledgeEngine.Api.runtimeconfig.json
appsettings.json
web.config
```

API 的 `web.config`：

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <location path="." inheritInChildApplications="false">
    <system.webServer>
      <handlers>
        <add name="aspNetCore"
             path="*"
             verb="*"
             modules="AspNetCoreModuleV2"
             resourceType="Unspecified" />
      </handlers>
      <aspNetCore processPath=".\KnowledgeEngine.Api.exe"
                  stdoutLogEnabled="false"
                  stdoutLogFile=".\logs\stdout"
                  hostingModel="inprocess" />
    </system.webServer>
  </location>
</configuration>
```

`inprocess` 模式的监听端口由 IIS 站点绑定控制，不需要通过 `ASPNETCORE_URLS` 设置 9101。

## 7. 建立 API 应用池和内部站点

### 7.1 创建应用池

```powershell
Import-Module WebAdministration

New-WebAppPool -Name MemorixApiPool

Set-ItemProperty IIS:\AppPools\MemorixApiPool `
    -Name managedRuntimeVersion -Value ""

Set-ItemProperty IIS:\AppPools\MemorixApiPool `
    -Name startMode -Value AlwaysRunning

Set-ItemProperty IIS:\AppPools\MemorixApiPool `
    -Name processModel.idleTimeout -Value ([TimeSpan]::Zero)

Set-ItemProperty IIS:\AppPools\MemorixApiPool `
    -Name processModel.identityType -Value ApplicationPoolIdentity
```

应用池参数应为：

```text
.NET CLR Version: No Managed Code
Pipeline: Integrated
Start Mode: AlwaysRunning
Idle Time-out: 0
Identity: ApplicationPoolIdentity
Maximum Worker Processes: 1
```

### 7.2 授权目录

```powershell
icacls C:\Apps\Memorix\Api\current `
    /grant "IIS AppPool\MemorixApiPool:(OI)(CI)RX"

icacls C:\Apps\Memorix\Api\logs `
    /grant "IIS AppPool\MemorixApiPool:(OI)(CI)M"

icacls C:\ProgramData\Memorix\DataProtection-Keys `
    /inheritance:r `
    /grant:r "SYSTEM:(OI)(CI)F" `
    "Administrators:(OI)(CI)F" `
    "IIS AppPool\MemorixApiPool:(OI)(CI)M"
```

### 7.3 建立内部 API 站点

```powershell
New-Website `
    -Name MemorixApi `
    -PhysicalPath C:\Apps\Memorix\Api\current `
    -ApplicationPool MemorixApiPool `
    -IPAddress 127.0.0.1 `
    -Port 9101

Set-ItemProperty IIS:\Sites\MemorixApi `
    -Name applicationDefaults.preloadEnabled `
    -Value $true
```

站点绑定：

```text
Type: http
IP address: 127.0.0.1
Port: 9101
Host name: 留空
```

验证内部 API：

```powershell
Restart-WebAppPool MemorixApiPool
Invoke-RestMethod http://127.0.0.1:9101/health
```

只有该地址返回 `healthy` 后，才继续配置公网 Gateway。

## 8. 部署 Next.js Web

构建环境设置：

```text
NEXT_PUBLIC_API_BASE_URL=/api
```

Next.js 配置应启用：

```javascript
const nextConfig = {
  output: "standalone"
};
```

构建：

```powershell
Set-Location C:\Build\Memorix\web
npm ci
npm run build

Copy-Item .next\static `
    .next\standalone\.next\static `
    -Recurse -Force

Copy-Item public `
    .next\standalone\public `
    -Recurse -Force
```

将 standalone 内容复制到：

```text
C:\Apps\Memorix\Web\current
```

手动验证：

```powershell
Set-Location C:\Apps\Memorix\Web\current
$env:NODE_ENV = "production"
$env:HOSTNAME = "127.0.0.1"
$env:PORT = "3000"
node server.js
```

另开窗口测试：

```powershell
curl.exe -I http://127.0.0.1:3000/
```

## 9. 将 Next.js 注册为 Windows Service

以 NSSM 为例：

```powershell
nssm install MemorixWeb
```

填写：

```text
Path: C:\Program Files\nodejs\node.exe
Startup directory: C:\Apps\Memorix\Web\current
Arguments: server.js
```

环境变量：

```text
NODE_ENV=production
HOSTNAME=127.0.0.1
PORT=3000
```

日志路径：

```text
stdout: C:\Apps\Memorix\Web\logs\stdout.log
stderr: C:\Apps\Memorix\Web\logs\stderr.log
```

配置并启动：

```powershell
nssm set MemorixWeb Start SERVICE_AUTO_START
nssm set MemorixWeb AppExit Default Restart
nssm start MemorixWeb

Get-Service MemorixWeb
curl.exe -I http://127.0.0.1:3000/
```

关闭 PowerShell 后再次验证，确保服务仍运行。

## 10. 建立公网 Gateway 站点

```powershell
New-WebAppPool -Name MemorixGatewayPool

Set-ItemProperty IIS:\AppPools\MemorixGatewayPool `
    -Name managedRuntimeVersion -Value ""

New-Website `
    -Name MemorixGateway `
    -PhysicalPath C:\Apps\Memorix\Gateway `
    -ApplicationPool MemorixGatewayPool `
    -IPAddress "*" `
    -Port 80 `
    -HostHeader memorix.hiqer.top
```

站点必须保持分离：

```text
MemorixApi      127.0.0.1:9101
MemorixGateway  *:80、*:443 / memorix.hiqer.top
```

## 11. 启用 ARR 代理

IIS Manager：

```text
服务器根节点
→ Application Request Routing Cache
→ Server Proxy Settings
→ Enable Proxy
→ Apply
```

建议设置：

```text
Enable Proxy: Yes
Preserve client IP: Yes
Reverse rewrite host in response headers: Yes
Time-out: 300 秒
```

## 12. 配置 Gateway 反向代理

创建 `C:\Apps\Memorix\Gateway\web.config`：

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <system.webServer>
    <rewrite>
      <rules>
        <rule name="API" stopProcessing="true">
          <match url="^api/(.*)$" />
          <action type="Rewrite"
                  url="http://127.0.0.1:9101/api/{R:1}"
                  appendQueryString="true" />
        </rule>

        <rule name="API Root" stopProcessing="true">
          <match url="^api/?$" />
          <action type="Rewrite"
                  url="http://127.0.0.1:9101/api/"
                  appendQueryString="true" />
        </rule>

        <rule name="Swagger" stopProcessing="true">
          <match url="^swagger/(.*)$" />
          <action type="Rewrite"
                  url="http://127.0.0.1:9101/swagger/{R:1}"
                  appendQueryString="true" />
        </rule>

        <rule name="Health" stopProcessing="true">
          <match url="^health/?$" />
          <action type="Rewrite"
                  url="http://127.0.0.1:9101/health"
                  appendQueryString="true" />
        </rule>

        <rule name="Next.js" stopProcessing="true">
          <match url="(.*)" />
          <action type="Rewrite"
                  url="http://127.0.0.1:3000/{R:1}"
                  appendQueryString="true" />
        </rule>
      </rules>
    </rewrite>

    <security>
      <requestFiltering>
        <requestLimits maxAllowedContentLength="62914560" />
      </requestFiltering>
    </security>
  </system.webServer>
</configuration>
```

60 MiB 请求限制为 50 MiB 文件和 multipart 开销预留空间。规则顺序不能改变，Next.js 必须是最后的 catch-all 规则。

## 13. DNS、防火墙和公网端口

DNS 添加：

```text
类型：A
主机记录：memorix
记录值：Windows Server 公网 IPv4
TTL：600 或默认
```

验证：

```powershell
Resolve-DnsName memorix.hiqer.top
```

Windows 防火墙：

```powershell
New-NetFirewallRule `
    -DisplayName "Memorix HTTP" `
    -Direction Inbound `
    -Protocol TCP `
    -LocalPort 80 `
    -Action Allow

New-NetFirewallRule `
    -DisplayName "Memorix HTTPS" `
    -Direction Inbound `
    -Protocol TCP `
    -LocalPort 443 `
    -Action Allow
```

如果服务器位于 NAT 后，还需配置公网 80/443 到该服务器的端口映射。不得向公网开放 3000、9101、5432。

## 14. 配置 HTTPS

证书必须覆盖：

```text
memorix.hiqer.top
```

将证书导入本地计算机的 Personal/Certificates，然后在 `MemorixGateway` 添加绑定：

```text
Type: https
IP address: All Unassigned
Port: 443
Host name: memorix.hiqer.top
Require Server Name Indication: 勾选
SSL certificate: memorix.hiqer.top 对应证书
```

内部 `MemorixApi` 不需要公网 HTTPS 绑定。

HTTPS 验证成功后，在 Gateway `web.config` 的 `<rules>` 第一项加入：

```xml
<rule name="Redirect to HTTPS" stopProcessing="true">
  <match url="(.*)" />
  <conditions>
    <add input="{HTTPS}" pattern="off" ignoreCase="true" />
    <add input="{HTTP_HOST}"
         pattern="^memorix\.hiqer\.top$"
         ignoreCase="true" />
  </conditions>
  <action type="Redirect"
          url="https://memorix.hiqer.top/{R:1}"
          redirectType="Permanent"
          appendQueryString="true" />
</rule>
```

最终规则顺序：

1. HTTP → HTTPS；
2. API；
3. API Root；
4. Swagger；
5. Health；
6. Next.js。

## 15. 分层验收

### 15.1 PostgreSQL

```powershell
Test-NetConnection 127.0.0.1 -Port 5432
```

### 15.2 内部 API

```powershell
Invoke-RestMethod http://127.0.0.1:9101/health
```

### 15.3 内部 Web

```powershell
curl.exe -I http://127.0.0.1:3000/
```

### 15.4 公网 HTTPS

```powershell
curl.exe -I https://memorix.hiqer.top/
curl.exe https://memorix.hiqer.top/health
curl.exe -I https://memorix.hiqer.top/swagger/index.html
```

### 15.5 HTTP 跳转

```powershell
curl.exe -I http://memorix.hiqer.top/health
```

预期：

```text
HTTP/1.1 301 Moved Permanently
Location: https://memorix.hiqer.top/health
```

生产稳定后应关闭 Swagger，或通过 IP、身份认证等方式限制访问。

## 16. HTTP 500.30 排障

`HTTP Error 500.30` 表示 IIS 和 ASP.NET Core Module 已工作，但 API 在启动初始化阶段异常退出。此问题与域名、证书和 ARR 无关。

首先保证以下内部地址成功：

```powershell
Invoke-RestMethod http://127.0.0.1:9101/health
```

临时把 API `web.config` 改为：

```xml
stdoutLogEnabled="true"
```

创建日志目录并授权：

```powershell
New-Item C:\Apps\Memorix\Api\current\logs `
    -ItemType Directory -Force

icacls C:\Apps\Memorix\Api\current\logs `
    /grant "IIS AppPool\MemorixApiPool:(OI)(CI)M"
```

触发启动并读取日志：

```powershell
Restart-WebAppPool MemorixApiPool

try {
    Invoke-RestMethod http://127.0.0.1:9101/health
} catch {
    $_.Exception.Message
}

$log = Get-ChildItem C:\Apps\Memorix\Api\current\logs\stdout*.log |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

Get-Content $log.FullName -Tail 200
```

同时检查事件日志：

```powershell
Get-WinEvent -FilterHashtable @{
    LogName = "Application"
    StartTime = (Get-Date).AddMinutes(-20)
} |
Where-Object {
    $_.ProviderName -match "IIS|AspNetCore|Application Error|\.NET Runtime"
} |
Select-Object TimeCreated, ProviderName, Id, LevelDisplayName, Message |
Format-List
```

常见原因：

1. PostgreSQL 不可达或账号密码错误；
2. IIS 尚未读取新机器环境变量；
3. .NET Runtime/Hosting Bundle 缺失；
4. API 发布文件不完整；
5. 应用池身份无目录权限；
6. Data Protection 目录不可写；
7. 生产配置仍包含默认或空连接串。

修复后将 `stdoutLogEnabled` 恢复为 `false`，避免日志无限增长。

## 17. 文件下载与删除验收

### 17.1 下载

```powershell
$baseUrl = "https://memorix.hiqer.top"
$token = "<JWT>"
$fileId = "<文件ID>"
$headers = @{ Authorization = "Bearer $token" }

$response = Invoke-RestMethod `
    -Uri "$baseUrl/api/files/$fileId/download-url" `
    -Headers $headers

$downloadUrl = $response.data.download_url

Invoke-WebRequest `
    -Uri $downloadUrl `
    -OutFile C:\Temp\memorix-download-test.pdf

Get-FileHash C:\Temp\memorix-download-test.pdf -Algorithm SHA256
```

验收：API 返回 200、URL 不包含 SecretKey、文件大小和哈希一致、越权返回 403、无认证返回 401。

### 17.2 删除

当前代码需要补充标准接口：

```http
DELETE /api/files/{fileId}
Authorization: Bearer <JWT>
```

正确处理顺序：

1. 查询文件记录；
2. 校验工作区权限；
3. 删除 COS 对象；
4. COS 成功后删除数据库记录；
5. 返回 204；
6. 再次下载应返回 404，COS 控制台中对象也应消失。

测试命令：

```powershell
Invoke-WebRequest `
    -Method Delete `
    -Uri "$baseUrl/api/files/$fileId" `
    -Headers $headers
```

## 18. COS 密钥轮换

执行顺序：

1. 为 Memorix CAM 子用户创建新 SecretId/SecretKey；
2. 权限只包含目标 Bucket 的上传、下载、删除和必要查询；
3. 更新机器级 `Minio__AccessKey` 和 `Minio__SecretKey`；
4. 执行 `iisreset` 或回收应用池；
5. 验证新文件上传、下载和删除；
6. 禁用旧密钥并观察；
7. 确认无异常后删除旧密钥。

不要将 SecretKey 输出到聊天、工单、截图、日志或 Git。

## 19. 发布与回滚

每次发布先创建版本目录：

```text
C:\Apps\Memorix\Api\releases\20260718-001
```

发布流程：

```powershell
Stop-WebAppPool MemorixApiPool
```

1. 复制完整发布包到新 release；
2. 将 `MemorixApi` 物理路径切换到新 release；
3. 启动应用池；
4. 验证内部 `/health`；
5. 验证公网 `/health`；
6. 验证登录、上传和下载。

```powershell
Start-WebAppPool MemorixApiPool

Invoke-RestMethod http://127.0.0.1:9101/health
Invoke-RestMethod https://memorix.hiqer.top/health
```

如果失败，将站点物理路径切回上一版本并重新启动应用池。

## 20. 最终验收清单

- [ ] PostgreSQL 仅绑定本机或内网地址；
- [ ] API 只监听 `127.0.0.1:9101`；
- [ ] Next.js 只监听 `127.0.0.1:3000`；
- [ ] 公网只开放 80、443；
- [ ] `http://memorix.hiqer.top` 永久跳转 HTTPS；
- [ ] `https://memorix.hiqer.top/health` 返回 healthy；
- [ ] 关闭 PowerShell 后 API 和 Web 仍运行；
- [ ] 服务器重启后 API 和 Web 自动恢复；
- [ ] API 日志不再重复输出；
- [ ] Data Protection 密钥目录已生成 XML 密钥且权限正确；
- [ ] CORS 只允许 `https://memorix.hiqer.top`；
- [ ] JWT、数据库密码和 COS 密钥不在发布配置及 Git 中；
- [ ] 文件上传、下载、删除及权限隔离通过；
- [ ] 新 COS 密钥已验证，旧密钥已禁用并删除；
- [ ] stdout 启动诊断日志已恢复关闭；
- [ ] API 和 Web 均保留可快速回滚的上一版本。

