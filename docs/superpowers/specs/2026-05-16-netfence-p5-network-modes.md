# NetFence P5 — 联网模式 + 规则档案增强

## 目标

增强规则档案系统，支持四种联网模式，加载档案时按模式自动创建对应的防火墙规则组合。

## 联网模式

| 模式 | 键名 | 防火墙规则 |
|---|---|---|
| 禁止全部 | `block_all` | 出站 Block + 入站 Block |
| 允许全部 | `allow_all` | 删除该 profile 所有 NetFence 规则 |
| 仅局域网 | `lan_only` | 出站 Block Internet（允许 Intranet）+ 入站 Block |
| 指定 IP/CIDR | `custom` | 出站 Allow 指定 IP + 入站 Block |

> ⚠️ Custom 模式受 Windows 防火墙 Block > Allow 限制，仅创建出站 Allow 规则 + 入站 Block。出站非允许 IP 的流量可能仍可达。

### LAN 范围

`127.0.0.0/8`, `10.0.0.0/8`, `172.16.0.0/12`, `192.168.0.0/16`

LAN Only 模式使用 `-RemoteAddress Internet` 关键字精确阻断互联网，同时保留 Intranet 访问。

### Allow 规则命名

`NetFence {profileName} Outbound Allow {hash}`（区别于 Block 规则）

## JSON 格式（v1.0 扩展）

```json
{
  "version": "1.0",
  "exportedAt": "2026-05-16T10:00:00+08:00",
  "name": "ProfileName",
  "paths": ["D:\\Apps\\Example"],
  "programs": ["helper.exe"],
  "mode": "block_all",
  "allowedIps": []
}
```

custom 模式：
```json
{
  "mode": "custom",
  "allowedIps": ["192.168.1.100", "10.0.0.0/8"]
}
```

## 新增/修改文件

### Core

| 文件 | 操作 | 职责 |
|---|---|---|
| `FirewallModeService.cs` | 新增 | 按模式创建/删除规则、LAN Internet Block、Custom IP Allow |
| `Models.cs` | 修改 | RuleProfile 增加 AllowedIps 字段 |
| `RuleProfileStore.cs` | 修改 | Save/Get 支持新字段，数据库 DDL 加列 |
| `Database.cs` | 修改 | 迁移：ALTER TABLE RuleProfiles ADD COLUMN AllowedIpsJson |

### App

| 文件 | 操作 |
|---|---|
| `Pages/RuleProfilesPage.xaml` | 修改：增加模式下拉框 + IP/CIDR 编辑 |
| `Pages/RuleProfilesPage.xaml.cs` | 修改：模式切换逻辑、保存/加载适配 |
| `Services/LocaleService.cs` | 修改：新增翻译键 |

## FirewallModeService API

```csharp
public static class FirewallModeService
{
    void ApplyMode(string profileName, IEnumerable<string> targets,
        string mode, IReadOnlyList<string> allowedIps);

    IReadOnlyList<string> LanRanges { get; }
}
```

## RuleProfilesPage UI 增强

在档案 save 按钮旁增加模式选择区：
```
[保存档案] [加载] [删除] [导出] [导入]  │  模式: [禁止全部 ▾]  [✎ IP/CIDR]
```

选择 custom 时展开文本框：
```
允许的 IP/CIDR（每行一个）:
┌─────────────────────────────┐
│ 192.168.1.100               │
│ 10.0.0.0/8                  │
└─────────────────────────────┘

⚠ 受 Windows 防火墙限制，仅创建出站 Allow + 入站 Block
```

## 翻译键新增

en-US / zh-CN：
- `networkMode` / "Network Mode" / "联网模式"
- `modeBlockAll` / "Block All" / "禁止全部"
- `modeAllowAll` / "Allow All" / "允许全部"
- `modeLanOnly` / "LAN Only" / "仅局域网"
- `modeCustom` / "Custom IP/CIDR" / "指定 IP/CIDR"
- `allowedIpsLabel` / "Allowed IPs/CIDRs (one per line)" / "允许的 IP/CIDR（每行一个）"
- `customModeNote` / "Note: due to Windows Firewall limitations..."
- `modeApplied` / "Mode '{0}' applied to {1} target(s)." / "模式 '{0}' 已应用于 {1} 个目标。"

## 验证标准

1. 四种模式可正常选择和保存
2. block_all → 创建出站+入站 Block
3. allow_all → 删除该 profile 所有 NetFence 规则
4. lan_only → 创建 Internet Block 出站 + 入站 Block，局域网可访问
5. custom → 创建指定 IP 出站 Allow + 入站 Block
6. 档案导出/导入保留 allowedIps
7. 构建 0 错误 0 警告
