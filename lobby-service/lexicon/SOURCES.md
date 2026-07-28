# 敏感词库来源说明

本目录词表快照自 https://github.com/konsheng/Sensitive-lexicon （MIT License）。

- 上游 commit: `5a8da94c61c160e203a6b2fcfafbea642404d50c`
- 快照日期: 2026-07-27

## 类别映射

| 文件 | 上游来源（Vocabulary/） |
|---|---|
| politics.txt | 政治类型.txt + 反动词库.txt |
| porn.txt | 色情类型.txt + 色情词库.txt |
| violence.txt | 暴恐词库.txt + 涉枪涉爆.txt |
| ads.txt | 广告类型.txt + 非法网址.txt |
| misc.txt | GFW补充词库.txt + 补充词库.txt + 其他词库.txt + 网易前端过滤敏感词库.txt + 零时-Tencent.txt |

## 明确排除

COVID-19词库.txt、民生词库.txt、贪腐词库.txt、新思想启蒙.txt —— 与游戏大厅场景相关度低、误报率高。

## 处理方式

合并后按行去重（sort -u），每行一个词。运行时加载器会跳过空行与 `#` 开头的注释行，对每个词做与输入文本一致的归一化（NFC、全半角、大小写折叠、符号剔除、重复压缩），并丢弃归一化后不足 2 字符的词（上游混入了大量单字符条目如 "1"、"日"、"妈"，单字符子串匹配必然误报）。
