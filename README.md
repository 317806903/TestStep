# Snake Lockstep：C# 帧同步多人贪吃蛇

一个用来学习**帧同步（Lockstep）**的控制台多人贪吃蛇。服务器只转发玩家输入，所有客户端各自运行相同的确定性逻辑，因此画面保持一致。

- .NET 8，纯控制台，Windows / Linux / macOS 都能运行
- 网络库：[LiteNetLib](https://github.com/RevenantX/LiteNetLib)（UDP）
- 游戏逻辑全程使用整数，没有浮点数，也没有平台差异

## 快速开始

需要安装 [.NET 8 SDK](https://dotnet.microsoft.com/download) 或更高版本。

```powershell
# 1. 启动服务器（--players 人数，--tick 逻辑帧率，--port 端口）
dotnet run --project src/Snake.Server -- --players 2

# 2. 另开终端启动客户端，每个玩家一个
dotnet run --project src/Snake.Client
dotnet run --project src/Snake.Client -- --host 192.168.x.x --port 9050   # 连接其他机器
```

人数到齐后自动开局。操作：**方向键 / WASD** 移动，**Q** 退出。

| 参数 | 所属程序 | 默认值 | 说明 |
|---|---|---|---|
| `--players` | Server | 2 | 玩家数（1~8） |
| `--tick` | Server | 10 | 逻辑帧率（Hz），也就是蛇的速度 |
| `--port` | Server / Client | 9050 | 端口 |
| `--host` | Client | 127.0.0.1 | 服务器地址 |
| `--replay <文件>` | Client | | 播放录像 |
| `--desync-at <帧>` | Client | | 调试用：从该帧起故意篡改一次输入，演示不同步检测 |

### 录像回放

每局结束时，服务器会在当前目录保存 `replay_*.snkrep`：

```powershell
dotnet run --project src/Snake.Client -- --replay replay_20261109_182134.snkrep
```

空格键暂停。录像只包含初始配置和每帧输入，一局只有几 KB。

### 运行测试

```powershell
dotnet test
```

## 项目结构

```
src/
  Snake.Logic/     纯逻辑层，零依赖：游戏规则、二进制协议、录像
  Snake.Server/    帧同步服务器：收集输入、按固定帧率广播、比对 hash
  Snake.Client/    客户端：网络、键盘输入、控制台渲染、录像回放
tests/
  Snake.Tests/     确定性、录像、规则测试
```

## 帧同步是怎么工作的

```
客户端 A ──输入──┐                       ┌──> 客户端 A：执行第 N 帧
                 ├─> 服务器（每 100ms）──┤
客户端 B ──输入──┘   打包第 N 帧所有输入  └──> 客户端 B：执行第 N 帧
```

1. 客户端只把按键（方向）发给服务器。
2. 服务器按固定帧率推进，每帧把所有人的输入打包广播。**不等网络慢的玩家**，某人这一帧没有输入就按"无输入"处理，这就是乐观帧同步。
3. 客户端必须收到第 N 帧的全部输入才执行第 N 帧。逻辑是确定性的，所以每个客户端算出的结果完全相同。
4. 客户端每 10 帧上报一次状态 hash，服务器比对。不一致说明出现了**不同步（desync）**，服务器会记录发生的帧号。

### 保证确定性的规则（见 `GameState.cs`）

- 只用整数，不用 `float` / `double`
- 不读系统时间，不用 `System.Random`，改用自带种子的 xorshift 随机数
- 只遍历 `List` 或数组，不遍历 `Dictionary` / `HashSet`（它们的遍历顺序不固定）
- 碰撞在所有蛇都移动完以后统一判定，结果与遍历顺序无关

## 后续可以做

- [ ] 断线重连：服务器下发历史帧，客户端快速重算追上进度
- [ ] 锁定 hash 测试：固定种子和输入，最终 hash 写死在测试里，防止逻辑被无意改动
- [ ] 客户端预测加回滚，改善操作手感
