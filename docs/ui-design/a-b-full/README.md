# 万象录 Mod 界面：A / B 完整装饰方案

本目录归档 Linux Claude「Mod 界面重新设计方案」中已生成的两套完整装饰设计，只供设计评审。

| 方案 | 高清示意图 | 可编辑网页预览 | 原始画板导出片段 |
| --- | --- | --- | --- |
| A · 原作融合 | [PNG](a-original/full-decoration.png) | [HTML](a-original/index.html) | [artboard.html](a-original/artboard.html) |
| B · 清爽现代 | [PNG](b-modern/full-decoration.png) | [HTML](b-modern/index.html) | [artboard.html](b-modern/artboard.html) |

## 查看方法

下载分支后，用浏览器打开本目录 `index.html`，或直接打开任一方案的 `index.html`。GitHub 文件页只显示 HTML 源码，不运行预览。无需 npm、构建工具或网络请求。预览按窗口空间等比缩放，最大 2 倍；PNG 可独立放大阅读。

## 内容与来源

- 原设计来源：Claude Design「万象录界面重设计 · 三套方案」。本次仅归档 A / B 完整装饰，不包含弱化装饰或 C 方案。
- PNG 来自原画板 PDF 的高清渲染，A 为 1280×1360，B 为 1560×1320。
- `artboard.html` 通过原画板菜单 **Copy as HTML** 完整导出；保留原设计的实际 DOM、内联计算样式及 SVG 纹理/图标。它是设计工具的静态导出，不是 Claude 对话中的原始手写源文件，也不包含设计工具运行时。
- `index.html` 为该导出片段补充 UTF-8 页面外壳及等比缩放代码，方便离线查看，并补回设计导出时丢失的原生滑块静态轨道。样式和 SVG 均可编辑；没有用整张截图冒充网页实现。
- 展示现有「数值编辑」页：角色属性与分类筛选、门派资产/名声/人数、向心力、个人贡献度、物品数量、世界与规则速度、界面透明度，以及原设计中的导航与提示。
- 这是静态视觉方案；设计工具导出会将部分表单控件转为展示元素，按钮、输入框、折叠项等不连接游戏数据，不能视为可运行 Mod。未新增或改变游戏业务功能。
- 字体沿用原设计的字体族声明（含 Noto Sans SC / Microsoft YaHei UI 等系统回退）。字体未打包，系统可用字体不同可能带来少量字形差异。装饰 SVG 和纹理已内嵌，无外部图片依赖。

本提交只增加 `docs/ui-design/a-b-full/` 下的设计文件；不修改 Mod 实现，不合并主分支，不创建 PR。
