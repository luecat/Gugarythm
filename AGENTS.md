# Gugarythm 開發指引

## 環境

- 使用 Unity 6.3 LTS 與 Android Build Support。
- 專案位於外接硬碟的 `GugarythmWorkspace` 工作磁碟；開發前確認該磁碟已掛載。
- 請勿將 Unity 專案直接放回大小寫敏感的外接硬碟根目錄。

## 專案結構

- 主要場景：`Assets/Scenes/RhythmPrototype.unity`
- 遊戲邏輯：`Assets/Scripts/`
- Android 測試版由 Unity 的 Android Build Support 建置。

## 工作交付

- 除非使用者明確要求確認或測試，否則不要額外執行驗證；完成修改並確保使用者在 Unity 按下 Play 後能看到結果即可。

## 版本控制

- 不要提交或推送本檔、`.gitignore`、任何 agent 設定檔、`Library/`、`Logs/`、`UserSettings/` 或建置輸出。
