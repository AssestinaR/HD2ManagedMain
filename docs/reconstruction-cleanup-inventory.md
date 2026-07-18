# 重建链路清理盘点（归档）

本文件记录 2026-07-18 以前的清理过程和当时的迁移计划；其中部分“剩余项”和“推荐批次”已经完成或已被后续验证结果替代，不再作为实施依据。

从 `2bd4ec8 refactor: remove unused core material closure resolver` 起，后续清理仅以 [reconstruction-cleanup-baseline.md](reconstruction-cleanup-baseline.md) 为准。该基线已记录：

- 已完成的 same-key / cross-armor execution 收敛及实际验收；
- 当前 Core、Adaptation、Manager 的职责边界；
- 剩余项目、风险等级、前置验证和不可违反的回归条件；
- 后续清理的建议顺序。

后续新增发现应更新基线文档，不再向本归档文件追加新的分析或计划。
