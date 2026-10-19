# LumenGrid 工业设备数字孪生与事件平台

面向制造业设备管理场景的个人项目，通过虚拟设备模拟、实时状态看板、规则告警和事件历史，展示设备异常从发现、通知到恢复追溯的闭环；并通过 API 与 ERP/WMS、MES 联动。

## 目录结构

```text
src/
├── LumenGrid.Api  # ASP.NET Core API、设备模拟器与事件服务
└── LumenGrid.Web  # Vue 3 实时设备看板
docs/               # 方案、阶段实现记录与集成验收文档
LumenGrid-Digital-Twin.slnx
```

## 技术栈

C#、.NET 10、ASP.NET Core Web API、EF Core、SQL Server、Vue 3、Vite、SignalR、BackgroundService。

## 核心功能

- 虚拟设备持续生成运行指标，支持注入异常场景，无需 PLC 硬件即可演示。
- 实时设备看板、SignalR 状态推送、可配置告警、维护任务及设备事件历史回放。
- 对接 ERP/WMS 生产计划和 MES 设备可用性、设备事件；设备异常时可向 MES 提供工序暂停建议。
- 已验收演示 4 台模拟设备、4 条告警规则及跨系统联动流程。

## 本地运行

需要安装 .NET 10 SDK、Node.js 和可访问的 SQL Server。复制示例配置并填写本机数据库密码、ERP 演示账号和本地集成密钥；真实配置已被 Git 忽略，请勿提交。

```powershell
Copy-Item .\src\LumenGrid.Api\appsettings.example.json .\src\LumenGrid.Api\appsettings.json
dotnet restore .\LumenGrid-Digital-Twin.slnx --configfile .\NuGet.Config
dotnet build .\LumenGrid-Digital-Twin.slnx --no-restore
dotnet run --project .\src\LumenGrid.Api --urls http://localhost:6103
```

前端另开终端运行：

```powershell
cd .\src\LumenGrid.Web
npm install
npm run dev -- --host localhost --port 6175
```

若要验收跨系统流程，还需同时启动 [WarehouseFlow-ERP-MES](../WarehouseFlow-ERP-MES/README.md) 中的 ERP 与 MES API（默认端口 6101、6102）。原工作区保留的统一启动与验收脚本可用于本机联调；单独克隆本仓库时请按上述命令分别启动服务。

## 文档

`docs/` 收录数字孪生方案、告警与事件历史实现记录，以及 ERP/MES 集成和验收过程。
