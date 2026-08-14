# ADR-0001 UIフレームワークの選定

## ステータス

採用（2026-08-15）

## 決定

初期リリースのAndroid UIには、**.NET MAUIのXAMLおよびC#**を採用する。TypeScriptおよびAngularは採用しない。Blazor Hybridおよび`HybridWebView`も初期リリースでは採用しない。

Domain層およびApplication層はUIフレームワークに依存しない.NETクラスライブラリとし、.NET MAUIのPresentation層からApplication層をプロジェクト参照して同一プロセス内で呼び出す。Android固有機能はApplication層のインターフェースをInfrastructure層で実装する。

採用バージョンは実装開始時点の最新安定版とする。調査時点の安定版は.NET MAUI 10だが、サポート終了は2027-05-11であるため、バージョンを固定したまま長期保守せず、サポート終了前に後継安定版へ更新する。

## 判断理由

このアプリはAndroid端末内で完結し、Web版とのUI共有を要件としていない。一方、業務ロジックをC#で実装することは制約である。したがって、次を優先した。

- UIからC#のApplication層を、シリアライズやJavaScriptブリッジを介さず直接呼び出せること
- Node.js、Angular、JavaScript↔C#契約を追加せず、言語、依存関係およびビルド工程を減らすこと
- Androidのネイティブコントロールを使用し、入力、文字サイズ変更、フォーカスおよびアクセシビリティの検証範囲を抑えること
- .NET MAUIのファイル選択、Android固有API呼び出し、APK生成および署名の公式な経路を利用できること

## 候補比較

| 候補 | C#業務ロジックとの接続 | Android保守性 | 判定 |
| --- | --- | --- | --- |
| .NET MAUI XAML + C# | 同一プロセスの通常のプロジェクト参照。JSON境界なし | .NET系の単一ビルドを基本とし、WebViewに依存しない | 採用 |
| .NET MAUI Blazor Hybrid | Razorコンポーネントは端末上の.NETで実行され、C#を直接利用できる | WebViewとHTML/CSSの表示差異を追加で検証する必要がある | Web版とのUI共有が必要になった場合の代替候補 |
| .NET MAUI HybridWebView + Angular | 公式のJavaScript↔C#呼び出しは可能 | JSONシリアライズ境界、Angular/Nodeと.NETの二重ツールチェーン、Android System WebViewの検証が必要 | 不採用 |
| Angular + Capacitor + C#ネイティブライブラリ | CapacitorのAndroidプラグインはJavaを基本とする。C# Native AOTのAndroid Java相互運用は組み込み対応がなく実験的 | 独自JNI／C ABIブリッジとABI別成果物の保守が必要 | 不採用 |

Angularを維持する技術的な経路自体は存在する。しかし、本システムにはWeb UI再利用の便益がなく、境界とツールチェーンの追加コストを相殺できないため採用しない。

## 成立性の確認結果

2026-08-15に、公式資料とローカル開発環境を用いて次を確認した。

| 要件 | 確認結果 | 実装時の方式 |
| --- | --- | --- |
| C#業務ロジックとUIの接続 | 成立 | UIからApplication層をプロジェクト参照し、DIでユースケースを注入する |
| 端末内データベース | 成立 | アプリデータ領域のSQLiteをInfrastructure層から利用する。ライブラリと移行方式はデータ設計で決定する |
| インポート | 成立 | .NET MAUI `FilePicker`で選択し、`FileResult.OpenReadAsync()`で`content://` URIを含めて読み込む |
| エクスポート | 成立 | Android Storage Access Frameworkの`ACTION_CREATE_DOCUMENT`をC#のAndroid実装から呼び、利用者が選択したURIへ書き込む |
| 署名済みAPK | 成立 | Releaseの`AndroidPackageFormats=apk`とAndroid署名プロパティを使用する。鍵情報はソース管理へ含めない |
| 更新インストール | 成立 | application IDと署名証明書を維持し、version codeを増加させる。更新前後のDB移行を端末試験する |
| オフライン／権限 | 成立 | Releaseマニフェストから`INTERNET`と`ACCESS_NETWORK_STATE`を除き、生成APKの最終マニフェストを検査する |

「成立」は、採用技術に公式な実装経路があり、要件と矛盾しないことを確認した結果を示す。現在のローカル環境には.NET SDK、Android SDK、JDK、Node.jsおよび接続済みAndroid端末がなく、一時SDKの導入も行わなかったため、コンパイル、APK生成および端末上の動作は未実施である。これらを実施済みとは扱わない。

## 実装開始時の実行検証

最初の機能実装より前に、最小アプリで次をすべて実行し、失敗した場合は本ADRを再検討する。

1. Android 10（API 29）端末またはエミュレーターで、XAML画面からC#の計算ユースケースを呼び出す。
2. SQLiteへ1件を書き込み、アプリ再起動後に読み出す。
3. Storage Access Frameworkで単一ファイルを出力し、そのファイルを`FilePicker`から読み戻す。
4. 本番用とは別の検証鍵でRelease APKを署名し、同じapplication ID、同じ鍵、増加したversion codeのAPKを上書きインストールしてDBが保持されることを確認する。
5. Release APKの最終マニフェストに`INTERNET`、`ACCESS_NETWORK_STATE`および不要なストレージ権限がないことを確認する。
6. `android:allowBackup="false"`とAndroid 12以降向けdata extraction rulesを設定し、クラウドバックアップ対象外であることを確認する。

## 影響

- UI実装者にもC#およびXAMLの習得が必要になる。
- Angular向け資産を直接再利用できない。
- .NET MAUIのサポート期間に合わせ、少なくとも年1回はSDK更新可否を確認する運用が必要になる。
- 将来Web版とのUI共有が必須になった場合は、Blazor Hybridを第一の再検討候補とする。

## 参照資料

すべて2026-08-15参照。

- [What is .NET MAUI?（Microsoft）](https://learn.microsoft.com/en-us/dotnet/maui/what-is-maui?view=net-maui-10.0)
- [ASP.NET Core Blazor Hybrid（Microsoft）](https://learn.microsoft.com/en-us/aspnet/core/blazor/hybrid/?view=aspnetcore-10.0)
- [HybridWebView（Microsoft）](https://learn.microsoft.com/en-us/dotnet/maui/user-interface/controls/hybridwebview?view=net-maui-10.0)
- [.NET MAUI local databases（Microsoft）](https://learn.microsoft.com/en-us/dotnet/maui/data-cloud/database-sqlite?view=net-maui-10.0)
- [File picker（Microsoft）](https://learn.microsoft.com/en-us/dotnet/maui/platform-integration/storage/file-picker?view=net-maui-10.0)
- [Access documents and other files from shared storage（Android Developers）](https://developer.android.com/training/data-storage/shared/documents-files)
- [Publish an Android app using the command line（Microsoft）](https://learn.microsoft.com/en-us/dotnet/maui/android/deployment/publish-cli?view=net-maui-10.0)
- [Android app manifest（Microsoft）](https://learn.microsoft.com/en-us/dotnet/maui/android/manifest?view=net-maui-10.0)
- [How app updates work（Android Developers）](https://developer.android.com/google/play/app-updates)
- [Back up user data with Auto Backup（Android Developers）](https://developer.android.com/identity/data/autobackup)
- [.NET MAUI Support Policy（Microsoft）](https://dotnet.microsoft.com/en-us/platform/support/policy/maui)
- [Capacitor documentation（Ionic）](https://capacitorjs.com/docs)
- [Native AOT deployment overview（Microsoft）](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/)
