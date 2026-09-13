# TIA Portal V16 标准 PLCSIM 最小闭环

本文只记录当前 V16 版本已经验证的最小运行链路，不把 OPC UA、S7 外部读值、PLCSIM Advanced 或业务工程作为前置条件。

## 验证链路

```text
PowerOnStandardPlcSim
→ Connect
→ OpenProject
→ SetSimulationDuringBlockCompilation
→ CompileSoftware
→ CheckDownloadReadiness
→ InspectDownloadRoutes
→ DownloadToStandardPlcSim
→ RunStandardPlcSim
→ ReadStandardPlcSimOutput
→ StopStandardPlcSim
```

## 必备环境

- Windows
- .NET Framework 4.8
- TIA Portal V16
- 当前用户属于 `Siemens TIA Openness`
- S7-PLCSIM V16

MCP 启动参数：

```text
--tia-major-version 16
--tia-portal-location "<TIA Portal V16 安装根目录>"
```

标准 PLCSIM API 的非默认安装目录可以通过工具参数 `apiPath` 或环境变量 `PLCSIM_V16_BIN` 指定。

## 工具职责

- `PowerOnStandardPlcSim`：创建或打开隔离的 PLCSIM 运行目录。
- `InspectDownloadRoutes`：确认并应用 `PLCSIM` 或 `Softbus` 下载路由。
- `DownloadToStandardPlcSim`：下载硬件和软件。
- `RunStandardPlcSim` / `StopStandardPlcSim`：控制仿真 CPU 模式。
- `ReadStandardPlcSimOutput`：只读过程输出区，不写变量、不切换 CPU 模式。

## 变量返回方式

标准 PLCSIM V16 该读取入口返回过程输出字节，而不是优化 DB 的符号成员。若需要把内部变量返回给 MCP，应在 PLC 程序中建立明确的输出桥接。例如：

```scl
%QB0 := INT_TO_BYTE(REAL_TO_INT("DB_PD_Sim".P_ProcessValue));
%QB1 := INT_TO_BYTE(REAL_TO_INT("DB_PD_Sim".PD_ProcessValue));
```

然后调用：

```text
ReadStandardPlcSimOutput(byteOffset=0, byteCount=2)
```

读取结果中的 `bytes[0]` 和 `bytes[1]` 分别对应 `P_ProcessValue` 和 `PD_ProcessValue`。该映射属于调用方工程契约，MCP 不会猜测任意 DB 布局。

## 发布边界

仓库只包含源码、文档和离线校验脚本。TIA `.ap16` 工程、PLCSIM 运行目录、`bin/obj`、日志和本机绝对路径均不提交。

静态校验：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Validate-Bundle.ps1
```

V16 构建需要在安装了 TIA Portal V16 Openness 组件的 Windows 机器上执行：

```powershell
msbuild .\tools\tiaportal-mcp\src\TiaMcpServer\TiaMcpServer.V16.csproj /t:Build /p:Configuration=Release
```
