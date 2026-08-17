# 依 Bilibili 原文重設 CHUNITHM 判定保護

## 權威來源

本設計以《中二節奏縱連判定特性（按鍵保護）觸摸板遊戲特性解說——上篇》為最高準則：

- https://www.bilibili.com/opus/852444793734692869

當本文件與過去企劃、測試或口頭假設衝突時，以本文件為準。

## 核心規則

1. 保護只在兩個 TAP 類 Note 的譜面區域具有正面積重疊，且時間判定區間足夠接近時成立。
2. TAP 類包含普通 TAP、ExTAP／Critical TAP、FLICK，以及匯入後以 TAP 類表示的 HOLD／SLIDE 頭。
3. HOLD／SLIDE 中繼點、尾點與持續接觸 Checkpoint 不參與判定保護。
4. 一旦 TAP 類 Note 形成保護對，Justice 與 Attack 判定帶對整個 Note 寬度縮減，不只限於空間重疊部分。
5. Justice Critical 核心帶只在兩 Note 的空間共享區域縮減；輸入位於各自非共享區域時，完整 JC 區間保留。
6. 判定帶以相鄰 Note 時間中點裁切：前 Note 不接受中點之後的受保護帶，後 Note 不接受中點之前的受保護帶。
7. 被裁掉的候選不降級、不改判到其他等級；若沒有其他合法候選，結果就是 Miss。
8. 保護由譜面配置決定，不因相鄰 Note 已經判定或 Miss 而消失。

## 判定帶與可見結果

時間帶沿用目前已確認的 CHUNITHM 視窗：

- JC 核心帶：`|delta| <= 2/60 s`。
- Justice 帶：`2/60 s < |delta| <= 4/60 s`。
- Attack 帶：`4/60 s < |delta| <= 6/60 s`。
- 之外：Pending，超過結算期限後為 Miss。

保護使用內部判定帶而不是最終顯示 Grade：

- 普通 TAP：JC → Perfect、Justice → Great、Attack → Good。
- Critical TAP／ExTAP：三個內部判定帶最後都顯示 Perfect，但 Justice／Attack 仍依全寬規則裁切。
- FLICK：保留現有早側 Perfect 映射，但保護仍以輸入時間落入的內部 JC／Justice／Attack 帶決定裁切方式。

## 空間判定

- 建立保護對只使用譜面原始範圍 `Lane - Size` 至 `Lane + Size`，不得加入 `LaneForgiveness`。
- 共享區域為兩個譜面原始範圍的交集。
- 只有嚴格正寬度交集才算重疊；僅邊界接觸不形成保護對。
- `LaneForgiveness` 只影響一般輸入能否命中 Note，不得創造保護對或擴大 JC 共享區域。

## 多 Note 與配對生命週期

- Note 依時間與 Index 穩定排序。
- 每個 Note 可同時與前後多個時間視窗相交的 TAP 類 Note 形成保護對。
- 候選必須通過所有適用保護對的裁切條件，才進入既有多指配對流程。
- 保護配對在 `JudgmentEngine` 建構時預先計算，不在每次輸入時掃描整張譜面。

## 實作邊界

- 以新的 `ProtectionBand` 表示 `Critical`、`Justice`、`Attack`、`Outside`。
- `GradeFor` 與保護帶計算共用相同邊界常數，避免顯示判定與保護漂移。
- 移除目前暫時的 `GUGARYTHM_INPUT` 與 `GUGARYTHM_PROTECTION_REJECT` 日誌。
- 不修改分數、Combo、Hold Checkpoint、音訊延遲或渲染邏輯。

## 驗證案例

1. 無空間重疊與僅邊界接觸：所有 JC／Justice／Attack 視窗保持原狀。
2. 任意正面積空間重疊：Justice／Attack 在整個 Note 寬度都按中點裁切。
3. JC 輸入位於共享區域：按中點裁切。
4. JC 輸入位於非共享區域：完整保留。
5. Critical TAP／ExTAP：可見結果只有 Perfect／Miss，但內部 Justice／Attack 帶仍受全寬保護。
6. FLICK：早側可見 Perfect 不得繞過其內部 Justice／Attack 保護帶。
7. 三連與多連：每個 Note 僅在所有前後中點共同形成的區間內有效。
8. 相鄰 Note 已判定或 Miss：後續 Note 的保護區間不改變。
9. 既有 Rub、多指、Hold、Auto Play 與完整譜面 RuntimeValidation 全部通過。
