# memorix.hiqer.top 发布说明

该发布包用于“预览站点＋云端程序”共存部署。域名根路径 `/`
默认显示 `preview/preview.html`，同时保留 Memorix 云端登录、业务页面、
API、Swagger 和健康检查，并包含 Memorix 0.1.3 的 macOS 与 Windows
安装程序。

## 共存路由

| 公网路径 | 目标 |
| --- | --- |
| `/` | 本地静态文件 `preview/preview.html` |
| `/preview/*` | 本地预览静态文件 |
| `/downloads/*` | 本地安装包 |
| `/desktop-updates/*` | Tauri 桌面端在线升级清单与签名产物 |
| `/api/*` | `http://127.0.0.1:9101/api/*` |
| `/swagger/*` | `http://127.0.0.1:9101/swagger/*` |
| `/health*` | `http://127.0.0.1:9101/health*` |
| `/login`、`/register` 及其他路径 | `http://127.0.0.1:3100/*` |

## IIS 部署

1. 将发布包解压到 `memorix.hiqer.top` 站点根目录。
2. 安装 IIS URL Rewrite 与 Application Request Routing（ARR）模块。
3. 在 ARR Server Proxy Settings 中启用 `Enable proxy`。
4. 确认 API 已监听 `127.0.0.1:9101`，Next.js 已监听
   `127.0.0.1:3100`。
5. 确认站点已绑定 `memorix.hiqer.top` 和 HTTPS 证书。
6. 保留根目录中的 `web.config`，不要再覆盖为旧版纯代理配置。
7. 依次检查 `/`、`/login`、`/api/auth/me`、`/health` 和下载地址。

## Nginx 部署

1. 将发布目录同步到 `/var/www/memorix.hiqer.top`。
2. 将 `nginx.conf` 放入 Nginx 站点配置目录。
3. 按服务器实际位置修改证书路径和站点根目录。
4. 确认 API 已监听 `127.0.0.1:9101`，Next.js 已监听
   `127.0.0.1:3100`。
5. 使用 `nginx -t` 检查配置后重新加载 Nginx。

## 下载地址

- macOS Apple Silicon:
  `/downloads/memorix-macos-arm64.zip`
- Windows x64:
  `/downloads/memorix-windows-x64.zip`

文件大小和 SHA-256 校验值见 `release-manifest.json`。

## 桌面端在线升级发布

1. 在 GitHub Actions 中使用版本标签构建，例如 `v0.1.4`。
2. 下载构建结果 `memorix-desktop-updater-0.1.4` 并解压。
3. 在服务器管理员 PowerShell 中执行：

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\Publish-DesktopUpdate.ps1 `
  -PackageRoot "C:\Temp\memorix-desktop-updater-0.1.4" `
  -SiteRoot "C:\Memorix" `
  -Channel stable
```

脚本会先复制版本化产物，最后原子替换 `stable/latest.json`。

发布后验证：

```powershell
curl.exe https://memorix.hiqer.top/desktop-updates/stable/latest.json
curl.exe -I https://memorix.hiqer.top/desktop-updates/releases/0.1.4/<更新包文件名>
```

GitHub 仓库必须配置以下 Actions Secrets：

- `MEMORIX_UPDATER_PUBLIC_KEY`
- `TAURI_SIGNING_PRIVATE_KEY`
- `TAURI_SIGNING_PRIVATE_KEY_PASSWORD`

macOS Developer ID、公证和 Windows Authenticode 所需凭据需按证书供应方式另行配置。
