# Flow Engine 节点插件目录

本目录用于放置 Flow Engine 节点插件 DLL。后端启动时会扫描本目录下的 `.dll` 文件，并通过独立的 `AssemblyLoadContext` 加载节点类型。

## 放置规范

1. 将编译好的插件 DLL 直接放入本目录（不要嵌套子目录）。
2. 插件项目只允许引用 `FlowEngine.Core`，禁止引用 `FlowEngine.Runtime`、`FlowEngine.Application` 或 `FlowEngine.Infrastructure`。
3. 插件 DLL 应尽量自包含其依赖；若存在多个插件共用依赖，请确保版本兼容，避免依赖冲突导致加载失败。
4. 单个插件加载失败不会影响主程序启动，系统会记录警告日志并跳过该 DLL。

## 配置

插件路径通过 `appsettings.json` 中的 `Plugins:Path` 配置，默认值为 `./plugins`。

```json
{
  "Plugins": {
    "Path": "./plugins"
  }
}
```
