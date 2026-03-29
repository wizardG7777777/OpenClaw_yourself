# MCP Agent → 游戏引擎 接口设计稿

> 来源：小组讨论整理（类《星露谷物语》MCP 草案）
> 范围：仅记录 Agent 主动发起到游戏引擎的 MCP 调用（工具/动作），用于"做什么"的指令层接口。
> 明确不在范围：游戏引擎 → Agent 的数据传输细节（返回 JSON 字段设计、effects/state_changes 结构、错误码表、UI 状态回传等）。

---

## 1. 总体原则（面向 Agent → 游戏引擎 的约束）

1. **只开放"游戏语义动作"**，不对 Agent 开放游戏引擎底层能力。
2. **LLM 负责做什么，游戏引擎负责怎么做**：Agent 发起高层动作；路径规划、动画、碰撞、交互判定等由游戏引擎执行。
3. **工具粒度优先通用**：优先用通用动作覆盖多种目标类型；只把极高频、语义稳定的动作单列出来。比如说："散步"，这样的独立且不太会有前置和后续的动作。
4. **禁止的"越权接口"**（禁止 Agent → 游戏引擎）：游戏引擎的操作过于底层且具备一定的风险，而 LLM 对于坐标数据的感知能力普遍不足。允许越权操作会导致虚拟人物出现：穿模，卡住，等等影响游戏体验的现象。

---

## 2. 游戏引擎侧三层处理结构（网关层 / 路由层 / 执行层）

目的：把 Agent 发来的 MCP JSON（`tool` + `args`）安全、可维护地落到游戏引擎的真实行为与查询上。

### 2.1 网关层（Gateway）

**定位**：MCP 的统一入口（HTTP/WebSocket/STDIO 均可），负责"接入与约束"，不负责具体玩法。不负责转化 Agent 的传入请求，但是网关层负责过滤所有的非法请求。

**职责清单（建议）**：

- **解析与校验**：仅执行结构层面/Schema 层面的校验：JSON 解析、必填字段存在性检查、字段类型检查、工具名白名单匹配。**网关层不检查值域范围或业务语义**（例如枚举合法性、数值上下限等），这些属于值级校验，由路由层在 Schema 校验通过后负责。如果出现结构层面的非法请求，则返回 Agent 友好的错误警告，基本的内容是：
  - 错误字段 + 错误值 + 错误原因（类型/缺失/非法工具名）+ 修改建议

- **权限与白名单**：只允许调用已注册的工具名，这个已注册的工具名会存放在工具注册表中。
  - 需要在入口处完成：
    - 工具名合法性检查（`tool` 是否在允许列表）—— 从路由层 Registry 读取只读白名单视图
    - 参数结构校验（字段存在性 + 类型）—— 同样基于 Registry 导出的 Schema 结构部分
    - **不做**参数值域校验（范围/枚举合法性等），该职责属于路由层
    - 权限校验。虽然单 Agent 形态，权限校验无所谓，但是如果要引入多个 Agent，这就很有必要了。
    - 频率限制与黑名单规则（本地游戏阶段不做，这个只是留空为了安全性着想）
  - 建议将"给 LLM 的 skills 文档"与"给程序的机器可读配置（schema/默认值/约束）"分离维护：
    - **说明文档**：面向 LLM 的自然语言解释、示例与最佳实践
    - **工具配置**：面向程序的参数类型/范围/默认值，由路由层 Registry 持有；网关层仅消费其中的结构/类型部分用于入口校验，值域约束由路由层在参数标准化阶段执行

- **会话与身份**：绑定 `player_id` / 存档 / 房间；做基础鉴权与 Agent 隔离。当然，如果游戏世界允许多人联机游玩，此时就这个机制就要拓展到玩家层面了。

- **并发与节流**：网关层的并发职责仅限于：基础 QPS 限制、请求去重、请求转发。网关层**不**独立判断工具是否排他——排他性的执行由路由层负责（路由层维护 `currentAction` 状态及工具的 `is_exclusive` 分类，据此决定是否拒绝/排队新动作）。网关层无需区分请求是"排他动作"还是"查询类"，该判断属于路由层职责。

- **请求追踪**：为每次请求分配 `request_id`（用于日志追踪和响应关联），并将请求交给路由层。注意：`request_id` 是网关层的通用标识，适用于所有请求（查询和动作）；`action_id` 仅在路由层判定为排他动作后才生成，写入 `ActionInstance`。两者可以使用相同值，但语义不同。

**输入/输出约定（不展开字段）**：
- 输入：`{ "tool": "move_to", "args": { ... } }`
- 输出：交由后续层生成；网关层只保证"请求被接受/被拒绝"的一致语义。

### 2.2 路由层（Router / Dispatcher）

**定位**：把"工具名"映射到对应处理器（Handler），实现解耦与可扩展。路由层会假设所有的请求都是合法的，而将过滤非法请求的职责交给网关层去做。

**职责清单**：

- **工具注册表（Tool Registry / Tool Catalog）**：`tool_name -> handler`（例如 `get_nearby_entities -> NearbyEntitiesHandler`）。虽然工具注册表的主要功能是配合网关层进行拦截，但是它的加载和维护是由路由层进行的。
  - 形式：通常是游戏引擎进程内的映射/注册代码（也可以由配置文件生成），由路由层持有；网关层可以读取其"可用工具白名单视图"。
  - 工具注册表的实际功能（按重要性从高到低）：
    1. **工具白名单与合法性校验（安全底线）**：定义哪些 `tool` 可被调用；网关/路由据此拦截 LLM 生成的非法工具名，或者是非法的参数。
    2. **路由绑定（分发依据）**：把 `tool_name` 绑定到具体 handler（或 handler factory），统一管理工具与实现的对应关系。
    3. **参数 Schema/约束声明（可执行性保障）**：字段类型、必填/可选、取值范围（如 `radius` 上限）、枚举集合、单位（tile/world）、以及"是否允许为空"等。注意该声明分两部分消费：**Schema 结构部分**（字段存在性、类型）以只读视图形式导出给网关层，用于入口结构校验；**值级约束部分**（范围、枚举合法性、语义正确性）由路由层在参数标准化阶段执行，不暴露给网关层。
    4. **策略与门禁（Policy）集中配置（稳定性/可控性）**：
       - 并发模型：查询可并发、动作互斥；可以独立地查询人物状态，人物正在执行的动作。
       - 频率限制：QPS、冷却时间、超时建议。（这和传统游戏开发不太一样，传统游戏开发会将频率限制放在执行层的方法去判断，所以这个没有定下来。）
       - 权限/场景门禁：例如仅室内可用、仅营业时间可用、仅在特定地图可用。用于将场景/时间段不可用的操作直接拦截到执行层之外。
    5. **工具自描述（Capabilities，工程可用性）**：为每个工具维护说明、用途、示例调用、参数说明/默认值、返回类型概述（不必展开字段）。用于 Agent 启动时拉取能力清单，减少硬编码。
  - 备注：注册表的"handler 绑定"属于路由层职责；网关层一般只消费其中的 **allowed_tools 白名单 + 基础约束**，避免网关侵入玩法实现。这个章节相关的技术描述会在正在撰写的《配置驱动的路由表》这份技术文档中描述。

- **参数标准化**：把 `args` 标准化为内部结构体/DTO（例如统一坐标单位、枚举值归一化）。
  - 例：用户说"回去干活"，LLM 可能生成：`{"tool":"move_to","args":{"destination":"back_work"}}`。
    - 在标准化阶段识别：`tool=move_to` 表示调用移动类工具；`destination` 是一个"语义目的地"字段（不是坐标/实体 ID）。
    - 将其转换为内部统一结构，例如：`MoveToRequest{target=SemanticDestination("back_work")}`

  **`target_id` 字段的路由分流逻辑**：Agent 传入的 `target_id` 可能是精确的 `entity_id`（如 `"tv_01"`）或语义字符串（如 `"电视"`）。路由层在参数标准化阶段按以下顺序判断：
  1. 先尝试 `EntityRegistry.GetById(target_id)` 精确查找——如果命中，直接构造 `ResolvedTarget`，跳过语义解析
  2. 如果未命中，将 `target_id` 作为语义字符串传入 `SemanticResolver.ResolveTarget()` 进行三级匹配和消歧

  这保证了 Agent 使用精确 ID（如从 `AMBIGUOUS_TARGET` 错误的 `candidates` 中拿到的 `entity_id`）重新调用时，不会再触发消歧流程。

- **语义解析与目标落地组件（Semantic Resolver & Grounder）**：
  - 该组件是**唯一**负责"语义级目标 → 可执行目标"的地方（查询与动作复用同一套逻辑），避免在执行层/各个 handler 内部重复实现。
  - 输入：
    - 语义字符串（名称/别名/POI 标签/描述性短语）
    - 场景约束与身份约束（地图、存档、玩家、语言等），每一个会话唯一。这个会话指代的是：Agent 与游戏引擎网关建立连接和进行通讯所使用的管道。场景约束和身份约束跟随会话的生命周期存在。
    - 可选过滤条件（类型、半径、只返回可交互等）
  - 输出（交给执行层的刚需）：
    - 一个明确的 `ResolvedTarget`（执行层接口默认只接收"一个准确目标"）：
      - `EntityRef`：实体引用/实体 ID（保证在当前场景可定位）
      - 或 `Point/TileSet`：一个坐标点或地块集合（保证在当前场景可解释）
  - 处理流程（概念步骤）：
    1. **语义解析（Resolve）**：通过"实体解析器/POI 解析器/别名表"等在当前场景（地图、存档、语言）中匹配，得到候选列表（0/1/N）。
    2. **消歧与选择（Disambiguation）**：使用确定性规则排序并选出一个首选目标（例如类型优先级、可交互性、距离、置信度等）。
    3. **目标落地（Grounding）**：
       - 由语义解析组件判断目标到底是实体还是点位/地块
       - 若为实体：产出 `EntityRef`（执行层再读取实时位置/边界/交互半径，用于距离计算、可达性与落点选择）
       - 若为点位/地块：产出坐标点或地块集合（例如建筑入口、区域中心、某块地的 tile 坐标），同样也是用于执行层的操作和计算
  - 关于多候选：解析得到多个候选时，优先在本组件内完成消歧；如确实需要"按候选顺序降级尝试"，应由路由层明确约定降级策略后再交付给执行层调度（而不是让执行层盲试）。

  #### 语义解析器实现决策（已确定）

  **决策 1：实体数据来源 — 运行时从场景自动生成**

  每个可交互 GameObject 挂载 `EntityIdentity` 组件（包含 `entity_id`、`display_name`、`aliases[]`、`entity_type`、`interactable` 等字段），由开发者在 Inspector 中填写。

  场景加载时由 `EntityRegistry` 单例通过 `FindObjectsByType<EntityIdentity>()` 自动扫描并注册到内存索引（`Dictionary<string, EntityIdentity>`）。实体在 `OnEnable` 时自动注册，`OnDisable` 时自动注销，支持运行时动态生成/销毁的实体。

  > 选择理由：场景即数据源，避免静态配置与场景脱节；MVP 实体少（电视、NPC、背包物品），在 prefab 上挂组件的工作量极低；后续若需集中管理别名（如多语言），可叠加静态配置覆盖层而不影响现有机制。

  涉及的类（预估代码量约 170 行，无外部依赖）：
  - `EntityIdentity.cs`（~20 行）：MonoBehaviour，挂载在每个可交互 GameObject 上，持有身份信息字段，OnEnable/OnDisable 时自动向 Registry 注册/注销。
  - `EntityRegistry.cs`（~80 行）：MonoBehaviour 单例，持有 `Dictionary<string, EntityIdentity>` 索引（以 `entity_id` 为键），提供 `GetById()`、`Search()`、`GetNearby()` 查询方法。场景加载时通过 `FindObjectsByType` 初始化。
  - `SemanticResolver.cs`（~60 行）：无状态工具类，接收语义字符串 + 玩家位置，调用 `EntityRegistry.Search()` 获取候选列表，执行消歧逻辑，返回 `ResolvedTarget` 或错误响应。
  - `ResolvedTarget.cs`（~10 行）：数据类，持有解析后的目标引用（EntityRef 或坐标）。

  **决策 2：消歧优先级排序**

  当语义字符串匹配到多个候选时，按以下优先级从高到低排序选出首选目标：

  1. **精确匹配 > 模糊匹配**：`entity_id` 或 `display_name` 完全一致的优先于别名/部分匹配
  2. **可交互性**：当前状态下 `interactable == true` 的优先
  3. **距离**：离角色近的优先
  4. **类型亲和**：如果工具调用自带上下文（比如 `use_tool_on` 传了 `tool_id=wrench`），则优先匹配能被该工具操作的实体类型

  消歧由路由层内的 `SemanticResolver` 完成，执行层收到的永远是一个确定的 `ResolvedTarget`。

  **决策 3：匹配算法 — MVP 阶段使用内置等级匹配**

  MVP 阶段不引入外部模糊匹配库和打分机制，使用三级匹配等级（优先级从高到低）：
  1. **精确匹配**（Tier 1）：查询字符串与 `entity_id` 或 `display_name` 完全一致（忽略大小写）
  2. **别名精确匹配**（Tier 2）：查询字符串与 aliases 数组中某项完全一致
  3. **包含匹配**（Tier 3）：`display_name` 或 aliases 中包含查询字符串

  匹配时按等级从高到低依次尝试，一旦某个等级命中了候选，就不再往下查找更低等级。

  > 后续扩展：当场景实体规模达到几十个以上或需要处理模糊描述时，可引入 FuzzySharp（Python fuzzywuzzy 的 C# 移植，NuGet 包，通过 DLL 放入 `Assets/Plugins/` 引入 Unity）替换 `Search()` 方法内部实现，接口不变。

  **决策 4：解析失败处理 — 等级区分 + 分场景策略**

  `SemanticResolver.ResolveTarget()` 的解析流程：

  1. 按匹配等级从 Tier 1 → Tier 2 → Tier 3 依次搜索，取**命中的最高等级**的候选集合
  2. 根据该等级的候选数量分三种情况处理：

  - **0 候选**（所有等级均无命中）：返回 `TARGET_NOT_FOUND` 错误 + `suggested_next_actions`（建议调用 `get_nearby_entities` 查看附近有什么）
  - **1 候选**：直接返回 `ResolvedTarget`，正常执行
  - **N 候选（同一等级内命中多个）**：返回 `AMBIGUOUS_TARGET` 错误 + `candidates` 列表（每个候选包含 `entity_id`、`display_name`、`type`、`distance`，按距离排序），Agent 在下一轮用精确 `entity_id` 重新调用即可避免再次触发消歧

  关键规则：**只有同一匹配等级内存在多个候选时才判定为歧义**。跨等级的不算——高等级的 1 个命中直接胜出，不受低等级候选数量的影响。

  示例：
  - 查询 `"电视"`，场景中有 1 个 `display_name` 为 `"电视机"` 的实体（Tier 3 包含匹配）+ 若干其他实体 → Tier 1/2 无命中，Tier 3 命中 1 个 → 消歧成功，直接选择
  - 查询 `"门"`，场景中有 3 个 aliases 包含 `"门"` 的实体（均为 Tier 2）→ 同等级 3 候选 → 歧义，返回 candidates
  - 查询 `"客厅门"`，场景中有 1 个 `display_name` 为 `"客厅门"` 的实体（Tier 1）+ 2 个 aliases 包含 `"门"` 的实体（Tier 2）→ Tier 1 命中 1 个 → 消歧成功，忽略 Tier 2

  > 选择理由：确定性强，同样的输入永远得到同样的判定；与三级匹配等级天然对齐，不需要额外的打分或阈值调优；MVP 场景实体少且别名由开发者控制，只要不给两个实体填相同的 `display_name` 或别名，基本不会出现同等级歧义。

- **场景快照装配**：注入 GameSnapShot（地图、实体索引、导航、背包、任务系统等）。
  - 目前这个阶段，这个场景快照机制主要功能是面向模块化测试设计的，若开发过程中发现确实没有必要在正式版中包含，可以直接删除。
  - 除了测试，预期这个机制在正式版的主要作用是：让角色在进行跨场景操作的时候，仍然保留出发时的快照，主要可以用于：
    - 操作回溯，在 Agent 操作出现了失败的时候，通过场景上下文快速恢复操作之前的场景。
    - 操作日志落档，在 Agent 执行了任何操作之后，利用上下文装配机制将操作落档至日志。

- **动作 vs 查询分流组件**：
  - 这个组件才是真正将 Agent 的请求转发给执行层的组件
  - 查询（如 `get_player_state`）：同步/轻量，直接调用执行层的状态查询方法并返回。
  - 动作（如 `move_to`）：创建并调度一个"动作实例"（Action Instance），进入执行层状态机。

### 2.3 执行层（Executor / Handlers）

**定位**：真正读取游戏世界并驱动角色/系统。这里才接触游戏引擎节点、导航、碰撞、动画与玩法规则。

执行层建议按两大类实现：

#### A) 查询类工具执行（同步快路径）

将原先拆分为多个具体查询工具（角色状态、附近实体、地块信息、世界摘要、背包、任务等）抽象为"查询类工具"的共性说明。

**查询类工具接受的输入**：
- 查询范围/过滤条件：例如半径、类型过滤、需要的字段集合、是否只返回可交互目标等
- 查询目标（执行层输入）：`ResolvedTarget`
  - 由路由层的"语义解析与目标落地组件"产出，执行层不再接收语义字符串
  - 形态可以是：实体引用（EntityRef/EntityId）或点位/地块（Point/TileSet）
- 上下文标识：例如玩家/存档/房间/当前地图（通常由网关/路由层从会话中注入）。注意：这个机制在单机单 Agent 环境下毫无意义，但是一旦放开多 Agent 或者多玩家机制，上下文标识就可以很好地按照玩家/角色 ID 进行隔离。一旦出现了错误操作角色等等问题，上下文表示也可以辅助进行故障排查。

**语义目标输入的处理说明（由路由层负责）**：
- 执行层的查询类工具被设计为不接受语义级别的查询输入，仅接受 `ResolvedTarget` 这样的被正确解析过的类。
- 执行层只接收路由层产出的 `ResolvedTarget`（一个明确目标），并在此基础上完成：过滤、可达性判定、排序与结果裁剪。

**查询类工具的主要目的**：
- 为 Agent 提供"可行动作空间"的信息基础，避免盲操作。
- 将分散在多个系统中的关键状态聚合/过滤/压缩为可用于决策的视图。
- 在不改变世界状态（或尽量少改变）的前提下，快速回答"现在是什么情况/附近有什么/某处能不能做某事"。

**查询类工具至少会返回什么内容**：
- 当前场景最小快照：位置/地图/时间等最基本的环境信息（用于让 Agent 确认自己所处的场景以及在场景中的具体位置和相对位置）
- 查询命中结果集合：
  - 若是"附近目标类查询"：返回候选目标列表（含类型、标识、位置/距离、可达/可交互性等最小字段）
  - 若是"地块/点位查询"：返回该点位的可通行/占用/可交互等关键状态
- 必要的裁剪信息：例如结果是否被截断、过滤条件是否生效、以及与查询相关的关键提示（用于后续决策）

#### B) 动作类工具执行（状态机 / 可中断）

以 `move_to` 为例（静止或移动目标都适用）：

- **目标输入（已由路由层落地）**：
  - 执行层接收路由层给出的"一个明确目标"（实体引用或坐标/地块），不再处理语义字符串。
  - 若目标为实体引用：可通过实体索引读取实体当前状态（位置/边界/交互半径等）。
  - 若目标为坐标/地块：直接作为导航/判定输入。
- **寻路与移动驱动**：使用 NavigationServer / NavigationAgent（2D/3D 视项目）持续驱动 Player 前进。
- **交互圈判定（每帧/定时）**：检测 `distance(player, target) <= interaction_range`，满足即结束。
- **移动目标跟踪（可选心跳）**：当 target 为 entity 且会移动时，执行层可每 `repath_interval` 重新取目标坐标并重规划路径；这属于执行层内部细节，不需要新增 MCP 工具。
- **终止/失败处理**：目标消失、不可达、超时、被更高优先级动作打断等。

#### 动作状态机实现决策（已确定）

**决策 1：生命周期状态**

```
         ┌──────────────────────────────┐
         │                              ▼
     Running ──→ Completed          Cancelled
         │                            ▲
         └──→ Failed                  │
                                      │
    （新排他动作到来时，当前 Running 动作 → Cancelled）
```

动作实例（`ActionInstance`）具有以下状态：

- **Running**：动作正在执行中（角色正在移动 / 正在播放交互动画）
- **Completed**：成功完成（到达目标 / 交互动画播放完毕）
- **Failed**：执行过程中失败（不可达、目标消失、超时等）
- **Cancelled**：被外部取消（新排他动作打断、Agent 发送取消指令）

> 不设 Pending 状态：因为打断策略采用 Last-Write-Wins（见决策 2），新动作到来时旧动作直接取消，新动作立即进入 Running，不存在排队等待的场景。

`ActionInstance` 数据结构：

```
ActionInstance {
    action_id: string          // 由路由层在判定为排他动作后生成的唯一标识
    tool_name: string          // 工具名，如 "move_to"
    status: ActionStatus       // Running / Completed / Failed / Cancelled
    target: ResolvedTarget     // 路由层落地后的目标
    created_at: float          // 创建时间（Time.time）
    timeout: float             // 超时时长（秒）
    result: object             // 完成/失败时的结果数据
    error_code: string         // 失败时的错误码（如 ACTION_TIMEOUT, TARGET_LOST）
}
```

**决策 2：打断策略 — 查询并发 + 动作 Last-Write-Wins**

请求被分为两类，分别适用不同的并发规则：

- **查询类**（`get_player_state`、`get_nearby_entities` 等）：**任何时候都可以并发执行**，不受排他限制，不影响正在执行的动作。查询是同步快路径，不创建 `ActionInstance`，直接返回结果。
- **动作类**（`move_to`、`interact_with`、`use_tool_on` 等）：**同一时间最多一个排他动作在执行**。当新的排他动作到来时：
  1. 将当前 Running 的动作标记为 `Cancelled`（保留在历史记录中供查询）
  2. 新动作立即进入 `Running`
  3. 返回给 Agent 的确认响应中包含 `cancelled_action_id`（如果有被取消的旧动作），让 Agent 知道发生了打断

> 选择理由：最贴近"LLM 负责做什么"的原则——Agent 发了新指令意味着它改主意了；MVP 场景下 Agent 的调用节奏是串行的 Observe → Decide → Act 循环，不太会出现连续快速发指令的情况；实现最简单，不需要队列管理。

**打断的后处理设计**：当一个排他动作被新动作取消时（如 `move_to` 被 `equip_item` 打断），引擎在游戏层面的表现是：角色停下当前动作，转而执行新动作（如打开背包、翻找、拿起装备）。打断本身在游戏语义上是合理的。被取消的动作的 ActionInstance 保留在 actionHistory 中，其 status 为 `Cancelled`，Agent 可以在后续轮询中发现打断事实，并决定是否重新发起被打断的动作（如重新调用 `move_to` 走向电视）。路由层不会自动恢复被打断的动作。

实现要点：

- 路由层维护一个 `currentAction: ActionInstance?` 字段，表示当前正在执行的排他动作
- 路由层同时维护一个 `actionHistory: List<ActionInstance>` 用于动作历史查询
- 动作 vs 查询的分类标记在工具注册表中声明（每个工具有一个 `is_exclusive: bool` 字段）

**决策 3：超时机制 — 工具自定义默认超时 + Agent 可覆盖**

每个动作类工具在工具注册表中声明一个默认超时值（秒），Agent 可通过 `args.timeout` 参数覆盖：

| 工具 | 默认超时（建议初始值，测试时调整） |
|------|------|
| `move_to` | 30s |
| `interact_with` | 10s |
| `use_tool_on` | 15s |
| `talk_to_npc` | 20s |
| `equip_item` | 5s |

超时检测由执行层 Handler 的 `Update()` 方法负责（见决策 5 的 `IActionHandler` 接口），每帧检查 `Time.time - action.created_at > action.timeout`。超时触发后：
- 动作状态 → `Failed`
- `error_code` = `ACTION_TIMEOUT`
- 返回的错误响应包含已执行时长和超时阈值，供 Agent 判断是否需要重试并设置更长超时

> 这些默认值是初始参考值，需要在实际测试中根据场景大小、移动速度、动画长度等因素调整。

**决策 4：进度反馈 — 立即确认 + 轮询（MVP）；未来改进为双向推送**

**MVP 阶段实现：立即确认 + 轮询**

Agent 发起动作类工具调用后，引擎立即返回确认响应，不阻塞等待动作完成：

```json
{
  "ok": true,
  "action_id": "act_001",
  "status": "running",
  "tool": "move_to",
  "cancelled_action_id": null
}
```

Agent 之后通过已有的查询工具获取动作进度和结果：
- `get_player_state`（查看角色状态和当前动作信息）：查看角色当前位置、状态，以及当前动作的 `status` 字段，直接获取 `running` / `completed` / `failed` / `cancelled` 状态和结果数据

这与设计稿推荐的 Observe → Decide → Act 循环天然契合——Act 之后 Agent 本来就要 Observe 查看结果。

> 选择理由：不需要额外的双向推送机制，网关层保持简单的请求-响应模式；轮询的 token 开销在 MVP 阶段可以接受。

**未来改进方案：完成回调 / 服务端推送（选项 III）**

当轮询的 token 开销成为瓶颈、或者需要支持更实时的 Agent 反应时，应升级为双向推送模式：

- **通信层改造**：网关层从纯请求-响应升级为支持双向消息的 WebSocket 长连接。引擎侧可以在任何时刻主动向 Agent 推送事件消息。
- **推送消息类型**：
  - `action_completed`：动作成功完成，附带结果数据
  - `action_failed`：动作失败，附带错误码和诊断信息
  - `action_cancelled`：动作被取消（被新动作打断）
  - `world_event`（可选扩展）：世界状态变化通知（NPC 移动到附近、物品出现/消失等），用于让 Agent 被动感知环境变化而不需要主动轮询
- **推送消息格式**（示例）：

```json
{
  "type": "action_completed",
  "action_id": "act_001",
  "tool": "move_to",
  "result": {
    "final_position": {"x": 5.2, "z": 10.1},
    "distance_to_target": 0.3,
    "duration": 4.2
  }
}
```

- **兼容性设计**：推送模式应作为轮询模式的**叠加增强**而非替代。Agent 连接时在握手阶段声明是否支持接收推送（`"capabilities": {"push": true}`）。不支持推送的 Agent 仍然可以用轮询模式正常工作，保证向后兼容。
- **实现路径**：当前的网关层如果基于 WebSocket 实现（如复用 unity-mcp 的 `WebSocketTransportClient` 架构模式），双向通信的底层能力已经具备，只需要在协议层增加"服务端主动发送消息"的约定。改造范围集中在网关层和路由层的事件分发，执行层不需要变动。

**决策 5：路由层 → 执行层交接协议**

1. **ActionInstance 的所有权**：ActionInstance 由路由层创建并持有。路由层将 ActionInstance 的引用传递给执行层的 Handler。执行层不创建自己的副本。

2. **执行层如何更新状态**：执行层的 Handler 通过直接修改传入的 ActionInstance 引用来更新状态（status、result、error_code）。由于 Unity 的单线程模型，路由层和执行层运行在同一个主线程上，不存在线程安全问题。

3. **查询工具读取路径**：当 Agent 调用 `get_player_state` 等查询工具时，路由层直接读取 `currentAction` 的 status 字段返回给 Agent，不需要经过执行层。

4. **动作完成的通知机制**：执行层的 Handler 在动作完成（Completed/Failed）时，将 ActionInstance 的 status 更新为终态，并填充 result 或 error_code 字段。路由层在下一次收到任何请求（查询或动作）时，检查 currentAction 是否已进入终态，如果是则将其移入 actionHistory 并清空 currentAction。

5. **执行层 Handler 的接口约定**：每个动作类 Handler 实现一个统一接口：
```
IActionHandler {
    void Start(ActionInstance action)    // 开始执行，由路由层调用
    void Cancel()                        // 取消执行，由路由层在 Last-Write-Wins 时调用
    void Update()                        // 每帧更新，由执行层管理器在 Update 中调用
}
```
Handler 在 Start 时保存 ActionInstance 引用，在 Update 中检查完成条件和超时，完成时直接修改 ActionInstance 的 status。

---

## 3. MCP 工具分类清单（Agent → 游戏引擎）

下面按讨论中推荐的功能域分门别类罗列 MCP 工具，并只保留**动作语义与输入（参数）**。

### A. 世界/状态查询类（Observe）

> 目的：为 Agent 决策提供"可行动作空间"，避免盲操作。

**1. 全局状态查询类**（预计可以从场景快照中直接拿到信息）：

| 工具名 | 说明 | 关键输入（建议） |
|--------|------|----------------|
| `get_player_state` | 查看主控角色的状态 | 无（返回当前主控角色的完整状态，包括位置、朝向、当前动作状态等） |
| `get_inventory` | 查看背包中的所有道具（查询类，`is_exclusive: false`，可与动作并发执行） | 无（返回背包中所有道具列表） |

**2. 锚点查询类**：

| 工具名 | 说明 | 关键输入（建议） |
|--------|------|----------------|
| `get_nearby_entities` | 查询锚点附近的所有可交互实体，在用户没有指明实体的时候，Agent 通过这个方法查询附近的可操作实体。预计在测试版本中，这个锚点范围会设计地非常巨大，以至于能包含整个场景。以此评估全场景查询是否会对 Agent 造成很大的认知压力。 | `radius`（搜索半径，可选，默认覆盖当前场景），`entity_types`（类型过滤数组，可选，如 `["npc", "appliance", "item"]`），`interactable_only`（是否只返回可交互实体，可选，默认 true） |
| `get_world_summary` | 为了缓解 LLM 上下文窗口有限而设计的方法，它能够返回一个包含：角色信息，世界关键信息，前 N 次历史操作记录，上下文清空之前的下达所有任务，等关键信息的回复。假如 Agent 那一侧已经通过机制极大缓解了上下文窗口有限的压力，实现了长时间的任务处理，那么这个方法就可以不用实现。 | 无（返回角色信息、世界关键信息、前 N 次历史操作记录等聚合摘要） |

**3. 目标查询类**：

| 工具名 | 说明 | 关键输入（建议） |
|--------|------|----------------|
| `resolve_target_in_scene` | 纯查询工具，供 Agent 在发送动作之前主动预查目标是否存在。输入语义字符串，返回匹配实体的完整信息（entity_id、坐标、类型、可交互性等执行层所需的一切信息）。本质上是 SemanticResolver 的查询接口暴露。与 `get_nearby_entities` 的区别：前者是"我知道名字，帮我找到它"，后者是"我不知道附近有什么，帮我列出来"。 | `query`（语义字符串，必填），`entity_type`（类型过滤，可选） |
| `resolve_target_in_world` | 在整个游戏世界中查询目标，返回值是目标所在的场景和具体目标 | `query`（语义字符串，必填），`entity_type`（类型过滤，可选） |

### B. 移动与导航类（Act-移动）

| 工具名 | 动作语义 | 关键输入（建议） |
|--------|---------|----------------|
| `move_to` | 移动到某个单一场景内部的某个实体或目标位置，不需要提供具体的坐标 | `target_id`（entity_id 或语义字符串，必填），`interaction_range`（到达判定距离，可选，默认 1.5，单位：世界坐标米） |
| `move_between_maps` | 跨场景移动（农场 ↔ 屋内 ↔ 镇上等） | `destination_map_id`，`entry_point_id` |

### C. 交互与动作执行类（Act-交互）

| 工具名 | 动作语义 | 关键输入（建议） |
|--------|---------|----------------|
| `interact_with` | 与目标交互（开门/睡觉/开箱/告示牌/柜台/工作台等） | `target_id` |
| `use_tool_on` | 对目标使用工具（锄地/浇水/砍树/采矿/钓鱼起手/镰刀收割等） | `tool_id`，`target_id` |
| `harvest_target` | 收获目标，这个收获方法主要面向收菜场景 | `target_id` |
| `pick_up_item` | 拾取地面掉落物 | `target_id` |

### D. 物品与背包管理类（Act-物品）

| 工具名 | 动作语义 | 关键输入（建议） |
|--------|---------|----------------|
| `equip_item` | 装备工具或物品 | `item_id` |
| `use_item` | 使用物品（食物/种子/礼包/药水等；可选目标） | `item_id`，`target_id`（可为空） |
| `transfer_item_to_container` | 将物品转移到容器 | `item_id`，`quantity`，`container_id`（或 `target_id`） |
| `transfer_item_from_container` | 从容器取回物品 | `item_id`，`quantity`，`container_id` |

> 说明：原讨论提到 `move_inventory_item` 更适合拆成"向容器放入/从容器取出"，以适配高频箱子交互。

### E. 社交与任务类（Act-社交/任务）

| 工具名 | 动作语义 | 关键输入（建议） |
|--------|---------|----------------|
| `talk_to_npc` | 与 NPC 对话（可带话题） | `npc_id`（必填），`topic`（可选，对话话题/意图，如不提供则触发默认对话） |
| `give_item_to_npc` | 给 NPC 送礼 | `npc_id`，`item_id`，`quantity` |
| `submit_quest_action` | MVP 阶段留空。其原本的目的是：一次性地向任务列表提交多个任务，这样能够降低 tool call 的 token 消耗 | 依任务系统定义（例如 `quest_id` + `action`） |

### F. 建造 / 种植 / 合成类（可选增强）

| 工具名 | 动作语义 | 关键输入（建议） |
|--------|---------|----------------|
| `plant_seed` | 在指定地块播种 | `seed_item_id`，`position` |
| `place_object` | 放置对象（箱子/家具/机器/栅栏等） | `object_item_id`（或 `item_id`），`position`，可选 `rotation` |
| `craft_item` | 合成物品 | `recipe_id`（或 `item_id`），`quantity` |

> 种地只是一个比方，这个类别的方法相比于交互方法，其目标明确，输入的变量较少。而且明确的名称和输入参数可以降低 tool call 的 token 消耗。

### G. 时间推进与睡眠类（Act-时间）

| 工具名 | 动作语义 | 关键输入（建议） |
|--------|---------|----------------|
| `sleep_until_next_day` | 睡到第二天（Stardew-like 关键动作） | 无；或可选 `bed_id` |

> 这个设计真的就是纯纯照搬星露谷了，因为项目的虚拟人应该是不需要睡觉的。但是如果我们的游戏确实设计了一个"下一回合"的设计，那么这个方法就要利用起来了。

---

## 4. MVP（最小可用）工具集（Agent → 游戏引擎）

讨论中给出的 **MVP 9 项**如下（保持原顺序）：

1. `get_player_state`
2. `get_world_summary`
3. `get_nearby_entities`
4. `move_to`
5. `interact_with`
6. `use_tool_on`
7. `talk_to_npc`
8. `get_inventory`
9. `equip_item`

> 这些接口组合起来，足以支撑几个基础的 Agent 操作核心循环验证。

### MVP 场景设计

> 至少 MVP 阶段还是使用 Unity 吧，官方 9.9 元一包的素材量大管饱到处都是。

这个 MVP 聚焦于单个场景内部的 Agent 决策可行性观察，仅包含以下内容：

- 三室一厅，室内场景
- 一个损坏的电视机
- 一个只会触发固定对话的 NPC（NPC 位于室内，不由 Agent 操作，仅仅用于测试对话的可行性）
- 背包，背包里面有：扳手，铲子，明信片。其中只有扳手在 MVP 场景中是需要使用的，剩下的两个就是完全的占位道具，用于检测 Agent 能不能根据说明拿起正确的工具。

这个 MVP 的验证至少包含以下方面，用于检测 Agent 是否可以正确进行游戏内操作：

- 角色在没有扳手的前提下和电视机交互，发现无法使用电视机
- 角色拿起扳手和电视机进行交互，成功修复了电视机
- 角色和修好的电视机进行交互。成功打开了电视机

---

## 5. 推荐的 Agent 调用节奏（仅列调用序列，不列回传）

讨论中推荐的稳定闭环：

1. **先查状态**：`get_world_summary` / `get_player_state`
2. **再查目标**：`get_nearby_entities`
   - （可选补充）当需要确认特定目标是否存在、获取其精确 ID 和状态时，调用 `resolve_target_in_scene`
3. **决定动作**
4. **执行动作**：`move_to` / `interact_with` / `use_tool_on` / ...
5. （随后通常需要读取返回以更新内部世界模型——**但返回字段细节属于 游戏引擎 → Agent，本文不展开**）
6. 继续下一轮 Observe → Decide → Act

---

## 6. 备注：MCP 回传（错误信息）应该包含什么

> 本文主体仅关注 Agent → 游戏引擎 的调用与分层；这里仅用"备注"形式，简单说明游戏引擎 → Agent 的回传在"报错"场景下应当传递什么，才能让 Agent 看懂、诊断并自恢复。

### 6.1 回传需要表达的 4 件事

1. **是否发生错误**
   - 用一个明确的布尔位或状态字段表达（例如 `ok=false` / `status="failed"`）。

2. **发生了什么样的错误（错误类型）**
   - 必须提供稳定的、可枚举的错误码（例如 `code`），而不是只给自然语言描述。
   - 这样 Agent 才能写出可复用的恢复策略（如"路径不可达就换入口点/换目标/等待"）。

3. **错误导致的原因（可诊断信息）**
   - 在 `details` 中提供"为什么失败"的关键证据：
     - 距离不够：`required_range`、`current_distance`
     - 目标不存在：`target_id`、搜索范围、最近相似项数量
     - 不可达：阻挡物/关闭的门/营业时间/任务锁等原因标识
     - 参数非法：哪个字段非法、允许范围
   - 原则：**让 Agent 不用猜**。

4. **可行的修改方案（下一步建议）**
   - 在回传里给出可执行的下一步建议（`suggested_next_actions`），例如：
     - "你是不是想要去 A？"（Maybe you mean xxx）
     - "先靠近目标再交互"（move closer）
     - "换一个入口点/等待到营业时间/换一个相近目标"
   - 这部分最好是结构化的（工具名 + 参数），而不是纯自然语言。

### 6.2 推荐的错误回传形态（示例）

> 字段名仅示意，关键是结构与语义。

**示例 1 — TARGET_NOT_FOUND（0 候选）**：

```json
{
  "ok": false,
  "error": {
    "code": "TARGET_NOT_FOUND",
    "message": "Target 'merchant_shop' not found in current scene.",
    "retryable": true,
    "details": {
      "query": "merchant_shop",
      "scene_id": "town",
      "reason": "NO_MATCH"
    },
    "suggested_next_actions": [
      {"tool": "get_nearby_entities", "args": {"radius": 50}},
      {"tool": "resolve_target_in_scene", "args": {"query": "merchant"}}
    ]
  }
}
```

**示例 2 — AMBIGUOUS_TARGET（N 候选）**：

```json
{
  "ok": false,
  "error": {
    "code": "AMBIGUOUS_TARGET",
    "message": "Found 3 matches for 'door' in current scene.",
    "retryable": true,
    "details": {
      "query": "door",
      "scene_id": "apartment",
      "match_tier": 2,
      "candidates": [
        {"entity_id": "door_bedroom", "display_name": "卧室门", "type": "furniture", "distance": 2.1},
        {"entity_id": "door_bathroom", "display_name": "浴室门", "type": "furniture", "distance": 4.5},
        {"entity_id": "door_kitchen", "display_name": "厨房门", "type": "furniture", "distance": 6.3}
      ]
    },
    "suggested_next_actions": [
      {"tool": "interact_with", "args": {"target_id": "door_bedroom"}}
    ]
  }
}
```

### 6.3 最小原则（便于 Agent 自恢复）

- **错误码稳定**：版本演进时尽量不改语义，废弃要给替代码。
- **细节足够**：至少能定位是"参数问题 / 距离问题 / 可达性问题 / 权限/门禁问题"。
- **建议可执行**：尽量给出下一步可直接调用的工具与参数（而不只是文字建议）。
