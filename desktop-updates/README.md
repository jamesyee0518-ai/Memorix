# Memorix 桌面更新发布目录

该目录是 `memorix.hiqer.top/desktop-updates/` 的部署源结构示例。

正式发布时，将 GitHub Actions 生成的 `memorix-desktop-updater-<version>` 内容复制到站点根目录下的 `desktop-updates`：

```text
desktop-updates/
├── stable/latest.json
└── releases/<version>/
    ├── macOS 更新包及 .sig
    ├── Windows 更新包及 .sig
    └── checksums.sha256
```

发布必须先上传 `releases/<version>`，验证下载和签名后，最后替换 `stable/latest.json`。

不要在本目录提交签名私钥、证书密码或 GitHub Secrets。
