# APK配布手順

Release APKは必ず正式な配布鍵で署名する。`bin`および`obj`配下にある
`*.apk`はビルド中間成果物であり、配布してはならない。配布対象は
`eng/build-release-apk.ps1`が署名検証後に`artifacts/`へ出力したAPKだけとする。

## 初回準備

1. 配布鍵（keystore）を安全な場所へ保管し、鍵ファイルと二つのパスワードを
   チームのパスワードマネージャーへバックアップする。鍵を紛失すると既存アプリを更新できない。
2. `eng/release-signing.local.ps1.example`を
   `eng/release-signing.local.ps1`へコピーし、実値を設定する。このファイルと
   keystoreはGit管理対象外である。
3. PowerShellで設定を読み込む。

   ```powershell
   Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
   . .\eng\release-signing.local.ps1
   ```

## ビルドと配布

```powershell
.\eng\build-release-apk.ps1
```

この処理はRelease APKを署名し、`apksigner`で署名を検証し、権限・バックアップ・
最小SDKを検査する。成功時に表示される`artifacts\TkpSalaryCalculator-<version>.apk`だけを配布する。

更新版では、`ApplicationVersion`を前版より大きい値にし、同じ`ApplicationId`と
同じ配布鍵を必ず使用する。

## 端末でのインストール

APKを個別配布する場合、Androidは配布元（Files、ブラウザ等）ごとに
「この提供元から許可」を利用者が明示的に有効化するよう要求する。これはアプリから
無効化・回避できないOSの安全機能である。許可後もPlay Protectの警告が表示される場合は、
APKが上記手順で署名・検証済みであることを確認した上で、端末所有者が表示内容を確認して
インストールを許可する。警告を配布側だけで完全になくすには、Google Playでの配布が必要になる場合がある。
