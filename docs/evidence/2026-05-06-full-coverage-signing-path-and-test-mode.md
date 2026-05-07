# 2026-05-06 Full Coverage Signing Path And Test-Mode Verify / full coverage 签名路径与 test-mode verify

English summary: Full coverage runtime build produced a flat Release `.tmx`; CLI signing now resolves that layout and verify can explicitly accept the optcnc test-mode certificate warning.

## 结论

- `engineering.build-solution` 生成的 C++ binary 实际路径是 `FullCpp\_products\TwinCAT OS (x64)\Release\FullCpp.tmx`。
- 旧 CLI 自动推导路径只检查了 `Release\FullCpp\FullCpp.tmx`，导致 `signing.sign-twincat-binary` 在 full coverage plan 第 66 步失败。
- 修复后 `signing.sign-twincat-binary` 会优先查找 flat layout，再兼容 nested layout，并在找不到时列出所有候选路径。
- `optcnc` 本地测试证书签名后，`TcSignTool verify` 输出文件已有签名，但返回 exit code 2，因为证书不是 Beckhoff 签发的 OEM 证书，只能用于 test mode。
- 新增 `AllowTestModeWarning` / CLI `allow-test-mode-warning` 后，只有在 verify 输出包含 signed file 和 Beckhoff warning 时，才把 exit code 2 作为成功。

## Commands

签名验证使用当前 full coverage build 产物：

```powershell
dotnet run --project .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj -- invoke-step --kind=signing.sign-twincat-binary --project-path=.\.artifacts\full-coverage-runtime\FullCoverage\FullCoverage.tsproj --cpp-project-name=FullCpp --configuration=Release --platform="TwinCAT OS (x64)" --certificate-path=C:\ProgramData\Beckhoff\TwinCAT\3.1\CustomConfig\Certificates\optcnc.tccert --password=123 --quiet=true

dotnet run --project .\src\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli\TwinCatAutomationKit.Cli.csproj -- invoke-step --kind=signing.verify-twincat-binary --project-path=.\.artifacts\full-coverage-runtime\FullCoverage\FullCoverage.tsproj --cpp-project-name=FullCpp --configuration=Release --platform="TwinCAT OS (x64)" --allow-test-mode-warning=true --quiet=true
```

## Observed Result

- Signing succeeded with target path:
  `D:\3rd_year\TwinCatAutomationKit\.artifacts\full-coverage-runtime\FullCoverage\FullCpp\_products\TwinCAT OS (x64)\Release\FullCpp.tmx`
- Verify succeeded with:
  - `exitCode: 2`
  - `acceptedTestModeWarning: true`
  - command line omitted `/q` internally so the warning text can be detected.

非 quiet verify 的关键输出：

```text
File '...\FullCpp.tmx' has signature.
issuer optcnc (optcnc@abc.com), certificate expires on 04/16/2028
Warning: Signature found, but OEM certificate was not signed by Beckhoff. Driver can only be used in test mode.
```

## 后续注意

- `allow-test-mode-warning` 只适合本地测试证书和 test mode runtime 验证；生产/OEM release 不应依赖它。
- 对 full coverage plan 来说，这是 activation 前的签名检查 gate；第 67 步通过后，计划才能继续到 `engineering.activate-configuration` 和 ADS readback。
