# 一起改进

欢迎通过 Issue 反馈问题，通过 Fork 和 Pull Request 提交修改。请说明出现问题的版本、复现步骤，以及希望看到的结果。截图请遮盖账号、手机号和密码，不要上传本机数据库、注册草稿或浏览器目录。

开发环境为 Windows、PowerShell 7、.NET SDK 10.0.404。克隆后运行：

```powershell
.\scripts\test.ps1
.\scripts\build.ps1
.\scripts\package.ps1 -SkipBuild
```

仓库中的测试账号、号码和密码均应使用独立的虚构数据。浏览器自动化测试拦截外部请求，不发送真实短信或执行真实注册。

请保持 QQ 官方来源检查、用户协议确认、安全验证和明确成功后保存的行为。账号到期、注销和手机号限制以实际官方结果记录；未知日期保持未知。

维护者发布新版时提升 App 和 Updater 项目的版本，更新说明后可运行 GitHub Actions 的「发布 Windows 版本」。该流程先测试、构建和打包，再创建包含 ZIP 与 update-manifest.json 的正式 Release。已经发布的版本不覆盖重发；修复后使用新的版本号。
