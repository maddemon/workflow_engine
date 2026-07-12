# n8n 节点调研：缺失节点对照清单（plan-node-gap-analysis）

> **参考基线：**
> - n8n `packages/nodes-base/nodes/` 目录（~250 个通用集成节点）
> - n8n `packages/@n8n/nodes-langchain/nodes/` 目录（AI/Agent/Tool 生态节点，基于 LangChain.js）
> - n8n `packages/@n8n/agents/`（Agent 框架 SDK）
> - 对比 Flow Engine `plugins/FlowEngine.Plugins.Standard/`（33 个节点）
>
> **用途：** 避免重复调研。后续开发时直接从此文档筛选优先级启动。

---

## 1. 现有节点速览

### 1.1 通用节点（对标 n8n nodes-base）

| 分类 | 已有节点 | 对标 n8n |
|------|---------|----------|
| **流程** | IfNode, SwitchNode, MergeNode, LoopNode, WaitNode, FilterNode | If, Switch, Merge, Loop, Wait, Filter |
| **数据** | SetNode, SortNode, LimitNode, AggregateNode, DeduplicateNode | Set, Sort, Limit, Summarize, RemoveDuplicates |
| **脚本** | JSNode | Code / Function / FunctionItem |
| **DB 写入** | DbUpsertNode（insert/update/upsert） | MySQL / Postgres 写入操作 |
| **DB 基础设施** | 已支持 Postgres / MySQL / SQL Server / SQLite 四种方言 | — |
| **HTTP** | HttpRequestNode, HttpToolNode | HTTP Request |
| **触发器** | WebhookNode, ManualTriggerNode, ScheduleTriggerNode | Webhook, Manual Trigger, Schedule/Cron |
| **认证** | OAuth2Node | OAuth2 |
| **杂项** | DataQualityNode, PaginateNode | — |

### 1.2 AI/Agent/Tool 节点（对标 n8n `@n8n/nodes-langchain`）

n8n 的 AI 生态全部基于 **LangChain.js**，有大量专门的子节点。我们采用了**更简洁的统一节点设计**（1个LlmNode + 1个AgentNode 覆盖多种场景），功能等价但粒度不同。

| 分类 | 已有节点 | 对标 n8n 多个节点的组合 | 说明 |
|------|---------|------------------------|------|
| **LLM 调用** | LlmNode | LLM Chain + LmChatOpenAi / LmChatAnthropic / Claude / Gemini 等 20+ 个独立 LLM 节点 | 我们统一接口，n8n 每个模型一个节点 + LangChain 基底 |
| **AI Agent** | AgentNode | Agent (ToolsAgent V3) + ToolExecutor + OpenAiAssistant | 功能等价：工具路由 + 多轮推理 |
| **工具—HTTP** | HttpToolNode | ToolHttpRequest | 等价 |
| **工具—计算器** | CalculatorToolNode | ToolCalculator | 等价 |
| **工具—代码** | CodeSnippetToolNode | ToolCode | 等价 |
| **工具—思考** | ThinkToolNode | ToolThink | 等价 |
| **工具—Web搜索** | WebSearchToolNode | ToolSerpApi / ToolSearXNG | n8n 用第三方搜索 API |
| **工具—子工作流** | SubWorkflowToolNode | ToolWorkflow | 等价 |
| **工具—Shell** | ShellToolNode | — | n8n 无直接对应（用 ExecuteCommand 节点） |
| **工具—子 Agent** | SubAgentToolNode | Agent (sub-agent 模式) | 等价 |
| **记忆** | MemoryNode | MemoryBufferWindow + MemoryManager + MemoryPostgresChat / MemoryRedisChat 等 | 我们统一，n8n 有多种持久化实现 |
| **Agent 入口** | — | ManualChatTrigger + ChatTrigger | n8n 有专门的聊天交互入口节点 |
| **提示词结构** | — | OutputParserStructured + OutputParserItemList | n8n 专门拆分输出解析 |
| **文档处理** | — | DocumentBinaryInputLoader + TextSplitter* | n8n 有 RAG 文档管线 |
| **向量存储** | — | VectorStorePinecone + VectorStorePGVector + VectorStoreQdrant 等 20+ | n8n 有完整的向量库生态 |
| **MCP** | — | McpClient + McpClientTool + McpTrigger | n8n 已实现 MCP 协议集成 |

---

## 2. 缺失节点清单

按缺失严重程度和通用价值分为三级。

### 2.1 🔴 High — 高频通用

这些节点在工作流自动化中出现频率极高，不论用户场景是什么都容易用到。

| # | 缺失节点 | 建议 TypeName | n8n 对标 | 功能简述 | 备注 |
|---|---------|--------------|---------|---------|------|
| 1 | **数据库查询节点** | `dbQuery` | MySQL / Postgres 的 Read 操作 | 执行 SELECT SQL，返回 DataBatch 给下游。参数：凭据、SQL、超时 | 已有 DbUpsert（写），缺读。DbExecutor 已就绪，可复用 |
| 2 | **发送邮件节点** | `emailSend` | EmailSend (SMTP) | 通过 SMTP 发送邮件。参数：收件人、主题、正文（纯文本/HTML）、附件 | 最常用的通信节点之一 |
| 3 | **分批处理节点** | `splitInBatches` | SplitInBatches | 将输入数据分批，每批 N 条送入下游循环处理 | Flow 控制核心模式，类似 Loop 但非 1 条/次 |
| 4 | **电子表格读写节点** | `spreadsheetFile` | SpreadsheetFile | 读写 CSV、XLSX、ODS 文件。读出转为 DataBatch，写入从 DataBatch 生成文件 | 数据处理最高频场景之一 |
| 5 | **列表操作节点** | `itemLists` | ItemLists | 操作：数组 ↔ 列表互转、字段拆成多行、多行合并为数组、汇总统计（sum/count/avg/min/max） | 对标 n8n 的 SplitOut, Summarize, ConvertToItems |
| 6 | **二进制文件读取节点** | `readBinaryFile` | ReadBinaryFile | 从磁盘/URL 读取二进制文件（图片、PDF、zip 等），放入 binary 字段供下游使用 | 文件处理基础能力 |
| 7 | **二进制文件写入节点** | `writeBinaryFile` | WriteBinaryFile | 将 binary 数据写入磁盘文件。参数：路径、文件名、写入模式 | 配合读取节点组成完整文件 I/O |
| 8 | **日期时间节点** | `dateTime` | DateTime | 格式化日期、加减时间、时区转换、计算时间差、获取当前时间戳 | 几乎每个工作流都会用到 |
| 9 | **停止并报错节点** | `stopAndError` | StopAndError | 主动抛错中止流程，支持自定义错误消息或错误对象。无输出端口 | 错误处理的关键节点 |
| 10 | **压缩/解压节点** | `compression` | Compression | Zip / Gzip / Tar 压缩和解压文件或二进制数据 | 文件处理常见需求 |
| 11 | **加密/解密节点** | `crypto` | Crypto | 哈希（SHA-256/MD5）、Base64 编码、AES 加密解密、HMAC 签名 | 数据安全与签名验证基础 |

### 2.2 🔴 AI/Agent/MCP 生态（n8n `@n8n/nodes-langchain`）

> 这些节点基于 LangChain.js 构建，形成完整的 AI Agent 开发生态。我们的 LlmNode + AgentNode 已覆盖了最核心的"调用 LLM + 工具路由"场景。以下列出 n8n 有但我们没有的细分节点，按"扩展我们现有 AI 能力"的价值排序。

| # | 缺失节点 | n8n 路径 | 功能简述 | 备注 |
|---|---------|---------|---------|------|
| 12 | **聊天触发器** | `trigger/ChatTrigger` | 嵌入聊天窗口到页面，用户可以对话方式触发工作流 | n8n 最重要的 AI 入口之一 |
| 13 | **手动聊天触发器** | `trigger/ManualChatTrigger` | 手动输入聊天消息触发（用于调试） | ChatTrigger 的调试版 |
| 14 | **结构化输出解析器** | `output_parser/OutputParserStructured` | 让 LLM 按预定义 JSON Schema 返回结构化数据 | 当前需在 LlmNode 提示词中手写格式要求 |
| 15 | **列表输出解析器** | `output_parser/OutputParserItemList` | 让 LLM 返回列表，自动拆分成多条 DataItem | 批量处理场景 |
| 16 | **自动修复输出解析器** | `output_parser/OutputParserAutofixing` | LLM 输出格式错误时自动重试修正 | 提高稳定性 |
| 17 | **文档加载器—二进制** | `document_loaders/DocumentBinaryInputLoader` | 从二进制文件（PDF/Word/CSV）加载文本供 LLM 处理 | RAG 管线的入口 |
| 18 | **文档加载器—JSON** | `document_loaders/DocumentJSONInputLoader` | 从 DataItem 的 JSON 字段加载文档 | 中间数据进入 RAG 管线 |
| 19 | **文档加载器—GitHub** | `document_loaders/DocumentGithubLoader` | 从 GitHub 仓库加载文档 | 代码知识库场景 |
| 20 | **文本分割器—递归字符** | `text_splitters/TextSplitterRecursiveCharacterTextSplitter` | 按分隔符层级递归切分文本（最常用） | RAG 预处理核心 |
| 21 | **文本分割器—Token** | `text_splitters/TextSplitterTokenSplitter` | 按 Token 数切分文本（精确控制上下文窗口） | 适配 LLM 上下文限制 |
| 22 | **文本分割器—字符** | `text_splitters/TextSplitterCharacterTextSplitter` | 按字符数 + 分隔符切分文本 | 基础分割 |
| 23 | **向量存储—PGVector** | `vector_store/VectorStorePGVector` | 基于 PostgreSQL + pgvector 的向量存储 | 自托管首选 |
| 24 | **向量存储—ChromaDB** | `vector_store/VectorStoreChromaDB` | 轻量级嵌入向量数据库 | 开发/小规模场景 |
| 25 | **向量存储—Pinecone** | `vector_store/VectorStorePinecone` + Insert + Load | Pinecone 向量数据库（SaaS） | 生产级托管方案 |
| 26 | **向量存储—Qdrant** | `vector_store/VectorStoreQdrant` | Qdrant 向量数据库 | 自托管/SaaS |
| 27 | **向量存储—Weaviate** | `vector_store/VectorStoreWeaviate` | Weaviate 向量数据库 | 自托管/SaaS |
| 28 | **向量存储—Redis** | `vector_store/VectorStoreRedis` | Redis 向量搜索 | 低延迟场景 |
| 29 | **向量存储—Supabase** | `vector_store/VectorStoreSupabase` + Insert + Load | Supabase pgvector 封装 | SaaS 方案 |
| 30 | **向量存储—Milvus** | `vector_store/VectorStoreMilvus` | Milvus 大规模向量库 | 企业级 |
| 31 | **向量存储—MongoDB Atlas** | `vector_store/VectorStoreMongoDBAtlas` | MongoDB Atlas Vector Search | SaaS 方案 |
| 32 | **向量存储—InMemory** | `vector_store/VectorStoreInMemory` + Insert + Load | 内存向量存储，重启丢失 | 测试/开发 |
| 33 | **Embeddings—OpenAI** | `embeddings/EmbeddingsOpenAI` | 调用 OpenAI Embeddings API | 生成文本向量 |
| 34 | **Embeddings—Azure OpenAI** | `embeddings/EmbeddingsAzureOpenAi` | Azure OpenAI Embeddings | 企业 Azure 场景 |
| 35 | **Embeddings—Cohere** | `embeddings/EmbeddingsCohere` | Cohere Embed API | 备选模型 |
| 36 | **Embeddings—Google** | `embeddings/EmbeddingsGoogleGemini` + Vertex | Google Embeddings | GCP 场景 |
| 37 | **Embeddings—HuggingFace** | `embeddings/EmbeddingsHuggingFaceInference` | HuggingFace Inference API | 开源模型 |
| 38 | **Embeddings—Mistral** | `embeddings/EmbeddingsMistralCloud` | Mistral Embed API | 备选 |
| 39 | **Embeddings—Ollama** | `embeddings/EmbeddingsOllama` | 本地 Ollama 模型向量 | 本地开发 |
| 40 | **Embeddings—AWS Bedrock** | `embeddings/EmbeddingsAwsBedrock` | AWS Bedrock Titan Embed | AWS 场景 |
| 41 | **Embeddings—Nvidia** | `embeddings/EmbeddingsNvidia` | Nvidia Embed API | Nvidia 场景 |
| 42 | **检索器—向量库** | `retrievers/RetrieverVectorStore` | 从向量库检索相关文档 | RAG 检索核心 |
| 43 | **检索器—多查询** | `retrievers/RetrieverMultiQuery` | LLM 生成多个检索查询，合并结果 | 提高召回率 |
| 44 | **检索器—上下文压缩** | `retrievers/RetrieverContextualCompression` | 检索后压缩/过滤不相关内容 | 减少 Token 消耗 |
| 45 | **检索器—工作流** | `retrievers/RetrieverWorkflow` | 将检索交给另一个工作流执行 | 自定义检索逻辑 |
| 46 | **Chain—检索 QA** | `chains/ChainRetrievalQA` | 基于检索文档回答问题的完整 Chain | RAG 最常用 Chain |
| 47 | **Chain—文本摘要** | `chains/ChainSummarization` | 长文本摘要（map-reduce / refine） | 文档处理 |
| 48 | **Chain—信息提取** | `chains/InformationExtractor` | 从非结构化文本提取结构化信息 | 文档处理 |
| 49 | **Chain—文本分类** | `chains/TextClassifier` | 按标签对文本分类 | 内容路由 |
| 50 | **Chain—情感分析** | `chains/SentimentAnalysis` | 文本情感判断 | 客服场景 |
| 51 | **Guardrails—安全护栏** | `Guardrails/Guardrails` | 输入/输出安全检测：Jailbreak, PII, NSFW, Keywords, Topical, URLs | 生产安全 |
| 52 | **记忆—Postgres** | `memory/MemoryPostgresChat` | PostgreSQL 持久化聊天记忆 | 记忆持久化 |
| 53 | **记忆—Redis** | `memory/MemoryRedisChat` | Redis 持久化聊天记忆 | 低延迟持久化 |
| 54 | **记忆—MongoDB** | `memory/MemoryMongoDbChat` | MongoDB 持久化聊天记忆 | 文档数据库 |
| 55 | **记忆—Zep** | `memory/MemoryZep` | Zep 长期记忆（AI 专用记忆服务） | 专业记忆服务 |
| 56 | **记忆—Xata** | `memory/MemoryXata` | Xata Serverless 持久化 | Serverless 场景 |
| 57 | **工具—Wikipedia** | `tools/ToolWikipedia` | 查询 Wikipedia 内容 | 知识检索工具 |
| 58 | **工具—SerpAPI** | `tools/ToolSerpApi` | Google 搜索 API 工具 | Web 搜索（凭据） |
| 59 | **工具—SearXNG** | `tools/ToolSearXNG` | 自托管 SearXNG 搜索工具 | 自托管搜索 |
| 60 | **工具—WolframAlpha** | `tools/ToolWolframAlpha` | WolframAlpha 知识计算工具 | 数学/科学计算 |
| 61 | **工具—向量库** | `tools/ToolVectorStore` | 向量库作为 Agent 工具 | Agent 可调用 RAG |
| 62 | **MCP Client** | `mcp/McpClient` | 连接 MCP Server 获取工具列表 | MCP 协议集成 |
| 63 | **MCP Client Tool** | `mcp/McpClientTool` | 将 MCP Server 的工具封装为 Agent 可调用的工具 | MCP 工具路由 |
| 64 | **MCP Server（触发器）** | `mcp/McpTrigger` | 将工作流以 MCP Server 形式暴露 | MCP 对外服务 |
| 65 | **Vendor—OpenAI** | `vendors/OpenAi` | OpenAI 全功能节点（assistants / files / audio / image / text） | 深度 OpenAI 集成 |
| 66 | **Vendor—Anthropic** | `vendors/Anthropic` | Anthropic Claude 全功能（documents / files / prompts） | 深度 Anthropic 集成 |
| 67 | **Vendor—Google Gemini** | `vendors/GoogleGemini` | Gemini 全功能（audio / video / image / fileSearch） | 深度 Google 集成 |
| 68 | **Vendor—Ollama** | `vendors/Ollama` | Ollama 本地模型消息接口 | 本地 LLM |
| 69 | **Vendor—AlibabaCloud** | `vendors/AlibabaCloud` | 阿里云通义千问（文本/图片/视频） | 中国区场景 |
| 70 | **Vendor—Moonshot** | `vendors/Moonshot` | Moonshot 月之暗面（文本/图片） | 中国区场景 |
| 71 | **Vendor—MiniMax** | `vendors/MiniMax` | MiniMax（文本/图片/音频/视频） | 中国区场景 |
| 72 | **重排序器—Cohere** | `rerankers/RerankerCohere` | Cohere Rerank API，对检索结果重排序 | RAG 精度提升 |
| 73 | **AI Transform** | `nodes-base/AiTransform/AiTransform.node.ts` | 用自然语言描述数据变换，AI 自动生成 JS 代码 | **低代码高级功能** |
| 74 | **Airtop** | `nodes-base/Airtop` | AI 驱动的浏览器自动化（云浏览器） | 需要外部服务 |
| 75 | **模型选择器** | `ModelSelector/ModelSelector` | 按模型能力选择性调用不同模型 | 模型路由/fallback |

### 2.3 🟡 Medium — 中等价值

这些节点场景稍窄，但也在不少工作流中出现。

| # | 缺失节点 | 建议 TypeName | n8n 对标 | 功能简述 |
|---|---------|--------------|---------|---------|
| 76 | **错误触发器** | `errorTrigger` | ErrorTrigger | 作为工作流的入口节点，当其他工作流产生错误时触发。用于错误通知/告警 |
| 77 | **空操作节点** | `noOp` | NoOp | 空操作：输入直接透传到输出。用于调试、占位、分支布线 |
| 78 | **画布便签节点** | `stickyNote` | StickyNote | 仅在画布上显示文字，运行时跳过。用于工作流注释和文档 |
| 79 | **数据集比较节点** | `compareDatasets` | CompareDatasets | 比较两个分支输入的数据差异：新增、删除、变更记录 |
| 80 | **HTML 模板节点** | `html` | Html | 用数据填充 HTML 模板，生成最终 HTML（邮件/报告场景） |
| 81 | **HTML 提取节点** | `htmlExtract` | HtmlExtract | 从 HTML 中通过 CSS 选择器提取数据 |
| 82 | **XML 节点** | `xml` | Xml | XML ↔ JSON 互转，XML 格式化 |
| 83 | **PDF 读取节点** | `readPdf` | ReadPdf | 从 PDF 文件提取文本内容 |
| 84 | **文件移动节点** | `moveBinaryData` | MoveBinaryData | 在 DataItem 的 JSON 和 Binary 字段之间移动数据 |
| 85 | **Airtable 节点** | `airtable` | Airtable | 基于 HTTP API 读写 Airtable 表格（Airtable REST API） |
| 86 | **NocoDB 节点** | `nocoDb` | NocoDB | 基于 HTTP API 读写 NocoDB 表格 |
| 87 | **Supabase 节点** | `supabase` | Supabase | 通过 Supabase REST API 或直接数据库方式操作 |
| 88 | **Redis 节点** | `redis` | Redis | Redis 缓存读写（GET/SET/DEL/EXPIRE），可基于 StackExchange.Redis |
| 89 | **MongoDB 节点** | `mongoDb` | MongoDb | MongoDB 文档 CRUD（非 EF Core 关系型范围） |
| 90 | **Elasticsearch 节点** | `elasticsearch` | Elasticsearch | 文档索引、搜索、删除，基于 Elastic.Clients.Elasticsearch |
| 91 | **Snowflake 节点** | `snowflake` | Snowflake | Snowflake 数据仓库查询与写入 |
| 92 | **Object Storage 节点** | `s3` | S3 | S3 兼容对象存储（MinIO、AWS S3、Cloudflare R2）：上传/下载/删除/列表 |
| 93 | **SSH 节点** | `ssh` | Ssh | 通过 SSH 执行远程命令或传输文件（SSH.NET） |
| 94 | **FTP/SFTP 节点** | `ftp` | Ftp | FTP/SFTP 文件上传、下载、删除、列表 |
| 95 | **Git 节点** | `git` | Git | Git 仓库操作：clone / pull / commit / push / status |
| 96 | **JWT 节点** | `jwt` | Jwt | JWT Token 编码、解码、验证（System.IdentityModel.Tokens.Jwt 已在项目依赖中） |
| 97 | **TOTP 节点** | `totp` | Totp | 基于时间的一次性密码生成和验证 |
| 98 | **RSS 读取节点** | `rssFeedRead` | RssFeedRead | 读取 RSS/Atom Feed，解析为数据条目 |
| 99 | **GraphQL 节点** | `graphql` | GraphQL | 向 GraphQL API 发送 query/mutation 请求并处理响应 |
| 100 | **RabbitMQ 节点** | `rabbitmq` | RabbitMQ | 消息发送与消费（基于 RabbitMQ.Client） |
| 101 | **Kafka 节点** | `kafka` | Kafka | 消息生产与消费（基于 Confluent.Kafka） |
| 102 | **MQTT 节点** | `mqtt` | MQTT | MQTT 发布与订阅（基于 MQTTnet） |
| 103 | **AMQP 节点** | `amqp` | AMQP | 通用 AMQP 消息收发 |
| 104 | **表单触发器节点** | `formTrigger` | Form | Web 表单入口，用户填写后触发工作流 |
| 105 | **SSE 触发器节点** | `sseTrigger` | SseTrigger | Server-Sent Events 触发器 |
| 106 | **IMAP 邮件读取节点** | `emailReadImap` | EmailReadImap | 从 IMAP 邮箱读取邮件，按条件筛选后输出为 DataBatch |
| 107 | **发送短信节点** | `sms` | Twilio / Plivo / Sms77 / MessageBird / Vonage / Mocean / Msg91 | 通过短信服务商发送短信通知 |
| 108 | ~~执行命令节点~~ | ~~`executeCommand`~~ | ~~ExecuteCommand~~ | ~~在服务器本地执行命令行并获取输出~~ | **已有 ShellToolNode 覆盖** |

### 2.4 🟢 Low — 第三方集成（按需补充）

以下约 **150+ 个** 节点为 n8n 的第三方 SaaS 服务集成，属于"用户需要特定服务时再开发"类型。列出大类以便决策：

| 类别 | 包括的服务 | 备注 |
|------|----------|------|
| **CRM** | Salesforce, HubSpot, Zendesk, Freshworks, Pipedrive, Copper, Zoho CRM, Monday.com | 最常被请求的集成类 |
| **办公协作** | Slack, Discord, Microsoft Teams（Microsoft 分类下）, Mattermost, Rocketchat, Google Workspace, Notion | 通知 / 自动化的入口 |
| **项目管理** | Jira, Trello, Asana, ClickUp, Taiga, Wekan, Linear | 任务创建、状态更新 |
| **即时通讯** | Telegram, WhatsApp, Line, Twilio, Vonage, Mocean | 客户通知通道 |
| **邮件营销** | Mailchimp, SendGrid, Brevo (Sendinblue), MailerLite, Mailgun, Mailjet, Mandrill, Sendy, GetResponse, ConvertKit | 订阅管理、邮件群发 |
| **电商** | Shopify, WooCommerce, Magento, Saleor（未列出）, PayPal, Stripe | 订单同步、支付回调 |
| **社交媒体** | Twitter/X, LinkedIn, Facebook, Instagram（未单独列出）, Reddit, YouTube（未列出） | 内容发布、监控 |
| **文件存储** | Google Drive, Dropbox, Box, OneDrive（Microsoft 分类下）, NextCloud | 文件触发同步 |
| **DevOps** | GitHub, GitLab, Bitbucket, Docker, Jenkins, CircleCI, TravisCI, Sentry, Datadog（未列出） | CI/CD 触发、代码事件 |
| **数据库服务** | Supabase, NocoDB, Airtable, Baserow, SeaTable, Grist, QuickBase, Stackby | 托管数据库/电子表格混合服务 |
| **营销工具** | Google Ads, Google Analytics, Facebook Ads, LinkedIn Ads, HubSpot Marketing, ActiveCampaign, Customer.io, Iterable | 广告与营销自动化 |
| **AI 服务** | OpenAI, MistralAI, Perplexity, JinaAI, DeepL, Stability AI（未列出）, Hugging Face（未列出） | AI 模型调用（LlmNode 已提供统一接口） |
| **支付** | Stripe, PayPal, Paddle, Chargebee, QuickBooks, Xero, ProfitWell | 支付 / 订阅 / 财务 |
| **表单** | Typeform, JotForm, Google Forms, Formstack, Wufoo | 表单提交触发工作流 |
| **身份认证** | Okta, Auth0（未列出）, LDAP, Bitwarden | 用户管理、SSO |
| **媒体** | Cloudinary（未列出）, Figma, Spotify, Bannerbear, QuickChart | 图片/音视频/设计 |
| **其他** | CoinGecko, OpenWeatherMap, Nasa, HackerNews, Strava, Todoist, PostHog, GitBook（未列出） | 垂直场景 |

---

## 3. 现有数据库基础设施（可供复用）

`plugins/FlowEngine.Plugins.Standard/Data/` 已有完整的基础设施，任何新数据库节点可直接复用：

| 文件 | 职责 |
|------|------|
| `DbExecutor.cs` | ADO.NET 执行器（ExecuteReader / ExecuteNonQuery / ExecuteScalar），支持事务 |
| `DbConnectionFactory.cs` | 按方言创建 `DbConnection` |
| `DbDialect.cs` + `DbDialectResolver.cs` | 方言枚举与解析 |
| `IConnectionStringBuilder.cs` + 各实现 | 从凭据字段生成连接字符串（已支持 SQLite / Postgres / MySQL / SQL Server） |
| `IDbSqlGenerator.cs` + `SqlGeneratorFactory.cs` + 各实现 | SQL 方言生成器（标识符引用、分页、INSERT/UPDATE/UPSERT 语句） |
| `IdentifierValidator.cs` | SQL 标识符校验（防注入） |

已引入的 NuGet 依赖：
- `Npgsql`（PostgreSQL）
- `MySqlConnector`（MySQL）
- `Microsoft.Data.SqlClient`（SQL Server）
- `Microsoft.Data.Sqlite`（SQLite）

> **简单复用示意**：新建 `DbQueryNode.cs` 只需依赖 `DbExecutor` + `IDbSqlGenerator`，从凭据获取连接串，执行 `SELECT` 并通过 `DbDataReader` 读取为 `DataBatch`。配套设施全部就绪。

---

## 4. 新增一个节点的标准骨架

所有新节点均需：

1. 实现 `INodeType` 接口
2. 注册到 `FlowEngine.Plugins.Standard` 项目（或新插件项目）
3. 在 `tests/FlowEngine.Runtime.Tests/Plugins/` 添加测试
4. `dotnet build && dotnet test` 通过

```csharp
// 节点骨架示例
public sealed class MyNewNode : INodeType
{
    public string TypeName => "myNewNode";
    public string DisplayName => "My New Node";
    public string Category => "Data";        // 使用已有分类
    public string Icon => "some-icon";
    public ExecutionMode ExecutionMode => ExecutionMode.OnceForAll;

    public IReadOnlyList<PortDefinition> Ports { get; } =
    [
        new() { Name = "Input",  Direction = PortDirection.Input,  Type = PortType.Main },
        new() { Name = "Output", Direction = PortDirection.Output, Type = PortType.Main }
    ];

    public bool DefaultIsEntry => false;

    public async Task<NodeExecutionResult> ExecuteAsync(
        NodeExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        var input = context.GetInputBatch();
        // ... 业务逻辑 ...
        return context.CreateResult(/* ... */);
    }
}
```

---

## 5. 节点放置策略

| 情形 | 放置位置 |
|------|---------|
| 通用数据/操作/流程节点 | `plugins/FlowEngine.Plugins.Standard/`（已有项目） |
| 特定数据库读写节点 | `plugins/FlowEngine.Plugins.Database/` （新建项目）或标准插件内 |
| AI/Agent/RAG 管线节点 | `plugins/FlowEngine.Plugins.AI/`（新建项目）或扩展 `FlowEngine.Plugins.Standard` |
| 文件存储/传输节点 | `plugins/FlowEngine.Plugins.Storage/`（新建项目，含 S3/FTP/SSH/Redis） |
| 第三方服务集成 | `plugins/FlowEngine.Plugins.Integration/` （新建项目，可选按服务拆分） |

> **当前建议**：
> - **High 通用节点**（DbQuery, EmailSend, SplitInBatches, SpreadsheetFile, ItemLists 等）直接放进 `FlowEngine.Plugins.Standard`
> - **AI 扩展节点**（ChatTrigger, OutputParser, DocumentLoader, TextSplitter, VectorStore, Embeddings 等）建议新建 `FlowEngine.Plugins.AI`，因为数量多且依赖复杂
> - **存储/传输节点**（Redis, MongoDB, S3, FTP, SSH）建议新建 `FlowEngine.Plugins.Storage`
> - **第三方集成**统一放 `FlowEngine.Plugins.Integration`

---

## 6. 变更记录

| 日期 | 修改内容 | 关联任务 |
|------|----------|---------|
| 2026-07-12 | 初版：基于 n8n `nodes-base` + `@n8n/nodes-langchain` 完整调研，列出缺失节点分级清单 | — |
